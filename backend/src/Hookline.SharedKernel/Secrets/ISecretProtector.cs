namespace Hookline.SharedKernel.Secrets;

/// <summary>
/// One protector for the whole hub. All external secrets (Slack tokens, Google
/// refresh tokens, API keys) are encrypted at rest through it. The Infrastructure
/// implementation is AES-256-GCM with a 1-byte key-version header so the key can
/// be rotated later without breaking older ciphertext.
/// </summary>
public interface ISecretProtector
{
    string Protect(string plaintext);
    string Unprotect(string ciphertext);
    bool TryUnprotect(string ciphertext, out string plaintext);
}
