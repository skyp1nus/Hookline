using Hookline.SharedKernel.Secrets;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Hookline.SharedKernel.Persistence;

/// <summary>Normalizes <see cref="DateTime"/> values to UTC on read/write.</summary>
public sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

/// <summary>
/// Transparently encrypts a string column at rest via <see cref="ISecretProtector"/>.
/// Apply with <c>builder.Property(x =&gt; x.Token).IsEncrypted(protector)</c>.
/// </summary>
public sealed class EncryptedStringConverter(ISecretProtector protector)
    : ValueConverter<string, string>(
        plaintext => protector.Protect(plaintext),
        ciphertext => protector.Unprotect(ciphertext));

public static class EncryptedPropertyExtensions
{
    /// <summary>Encrypt this string property at rest through the shared protector.</summary>
    public static PropertyBuilder<string> IsEncrypted(this PropertyBuilder<string> builder, ISecretProtector protector)
        => builder.HasConversion(new EncryptedStringConverter(protector));
}
