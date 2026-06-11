using System.Security.Cryptography;
using System.Text;

using Hookline.SharedKernel.Secrets;

namespace Hookline.Infrastructure.Secrets;

/// <summary>
/// AES-256-GCM secret protector. Key = SHA-256(TokenEncryption__Key).
/// Ciphertext layout: <c>[version(1)][nonce(12)][tag(16)][ciphertext(n)]</c>, base64-encoded.
/// A fresh 96-bit CSPRNG nonce is used per encryption; the leading version byte keeps key/format
/// rotation possible later. NOTE: that version byte is a deliberate format change — ciphertext from
/// the pre-port app (which had no version prefix) is NOT decodable here. Phase 1 starts fresh via
/// re-OAuth, so there is no legacy ciphertext to import. Fails fast if the master key is missing and
/// never swallows an authentication-tag failure on decrypt.
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const byte Version = 0x01;
    private const int NonceSize = 12; // 96-bit, recommended for GCM
    private const int TagSize = 16;   // 128-bit
    private const int HeaderSize = 1 + NonceSize + TagSize;

    private readonly byte[] _key;

    public AesGcmSecretProtector(string? masterKey)
    {
        if (string.IsNullOrWhiteSpace(masterKey))
        {
            throw new InvalidOperationException(
                "TokenEncryption__Key is required — refusing to start without an encryption key.");
        }

        // SHA-256 of the configured key → a stable 32-byte AES-256 key.
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(masterKey));
    }

    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(nonce, plainBytes, cipher, tag);
        }

        var output = new byte[HeaderSize + cipher.Length];
        output[0] = Version;
        Buffer.BlockCopy(nonce, 0, output, 1, NonceSize);
        Buffer.BlockCopy(tag, 0, output, 1 + NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, output, HeaderSize, cipher.Length);
        return Convert.ToBase64String(output);
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrEmpty(ciphertext);

        var bytes = Convert.FromBase64String(ciphertext);
        if (bytes.Length < HeaderSize)
        {
            throw new CryptographicException("Ciphertext is too short to contain the header.");
        }

        var version = bytes[0];
        if (version != Version)
        {
            throw new CryptographicException($"Unsupported secret version byte: 0x{version:X2}.");
        }

        var nonce = bytes.AsSpan(1, NonceSize);
        var tag = bytes.AsSpan(1 + NonceSize, TagSize);
        var cipher = bytes.AsSpan(HeaderSize);
        var plain = new byte[cipher.Length];

        using (var aes = new AesGcm(_key, TagSize))
        {
            // Throws CryptographicException on tag mismatch — never caught here.
            aes.Decrypt(nonce, cipher, tag, plain);
        }

        return Encoding.UTF8.GetString(plain);
    }

    public bool TryUnprotect(string ciphertext, out string plaintext)
    {
        try
        {
            plaintext = Unprotect(ciphertext);
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            plaintext = string.Empty;
            return false;
        }
    }
}

/// <summary>
/// Design-time / no-secret passthrough used only by EF migrations, where the
/// encryption converter doesn't change the column schema. Never registered at runtime.
/// </summary>
public sealed class PassthroughSecretProtector : ISecretProtector
{
    public string Protect(string plaintext) => plaintext;
    public string Unprotect(string ciphertext) => ciphertext;
    public bool TryUnprotect(string ciphertext, out string plaintext)
    {
        plaintext = ciphertext;
        return true;
    }
}
