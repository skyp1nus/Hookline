import { createHmac } from "node:crypto";

/**
 * Mints the short-TTL identity assertion the BFF forwards to the backend, matching the
 * .NET `IdentityTokenService` format exactly:
 *   base64url(JSON{sub,role,exp}) "." base64url(HMAC_SHA256(part1, Identity__SigningKey))
 * Signed with the dedicated identity signing key — never the AES master key. The backend
 * verifies the signature + expiry on every request; this is what establishes identity
 * (the X-Admin-Token only proves the BFF is the caller).
 */
const SIGNING_KEY = process.env.IDENTITY_SIGNING_KEY ?? "";

function base64url(input: Buffer): string {
  return input.toString("base64").replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
}

export function mintIdentity(userId: string, role: string, ttlSeconds = 120): string {
  const payload = JSON.stringify({
    sub: userId,
    role,
    exp: Math.floor(Date.now() / 1000) + ttlSeconds,
  });
  const part1 = base64url(Buffer.from(payload, "utf8"));
  const signature = base64url(createHmac("sha256", SIGNING_KEY).update(part1, "ascii").digest());
  return `${part1}.${signature}`;
}
