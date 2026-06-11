using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Hookline.SharedKernel.Auth;

namespace Hookline.Infrastructure.Auth;

/// <summary>A verified identity extracted from a BFF assertion.</summary>
public sealed record AssertedIdentity(Guid UserId, UserRole Role, DateTimeOffset ExpiresAt);

/// <summary>
/// Mints and verifies the short-TTL identity assertion the BFF forwards to the backend.
/// Format (matched by the Next BFF): <c>base64url(JSON{sub,role,exp})."."base64url(HMACSHA256(part1, key))</c>.
/// Signed with a DEDICATED signing key (<c>Identity__SigningKey</c>) — never the AES master key.
/// X-Admin-Token only proves the BFF is calling; this assertion is what establishes identity.
/// </summary>
public sealed class IdentityTokenService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly byte[] _key;

    public IdentityTokenService(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException(
                "Identity__SigningKey is required — refusing to start without an identity signing key.");
        }

        _key = Encoding.UTF8.GetBytes(signingKey);
    }

    public string Sign(Guid userId, UserRole role, TimeSpan ttl)
    {
        var payload = new Payload(
            userId.ToString("N"),
            role.ToString(),
            DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds());

        var part1 = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions));
        var signature = Base64UrlEncode(HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(part1)));
        return $"{part1}.{signature}";
    }

    public AssertedIdentity? Verify(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var dot = token.IndexOf('.');
        if (dot <= 0 || dot == token.Length - 1)
        {
            return null;
        }

        var part1 = token[..dot];
        var providedSig = token[(dot + 1)..];

        var expectedSig = HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(part1));
        byte[] providedSigBytes;
        try
        {
            providedSigBytes = Base64UrlDecode(providedSig);
        }
        catch (FormatException)
        {
            return null;
        }

        if (!CryptographicOperations.FixedTimeEquals(providedSigBytes, expectedSig))
        {
            return null;
        }

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(Base64UrlDecode(part1), JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            return null;
        }

        if (payload is null
            || !Guid.TryParse(payload.Sub, out var userId)
            || !Enum.TryParse<UserRole>(payload.Role, out var role))
        {
            return null;
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.Exp);
        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            return null; // expired
        }

        return new AssertedIdentity(userId, role, expiresAt);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch { 2 => s + "==", 3 => s + "=", _ => s };
        return Convert.FromBase64String(s);
    }

    private sealed record Payload(
        [property: JsonPropertyName("sub")] string Sub,
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("exp")] long Exp);
}
