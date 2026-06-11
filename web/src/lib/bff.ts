import { mintIdentity } from "@/lib/auth/identity";
import { getSession } from "@/lib/auth/session";

/**
 * Server-side call to the backend. Injects the admin token (proves the BFF) + a freshly
 * minted identity assertion from the session (establishes the user), and STRIPS any
 * incoming cookie so the session cookie is never forwarded to the backend.
 */
const BACKEND_URL = process.env.BACKEND_URL ?? "http://localhost:8080";
const ADMIN_TOKEN = process.env.BACKEND_ADMIN_TOKEN ?? "";

export async function backendFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const session = await getSession();

  const headers = new Headers(init.headers);
  headers.set("X-Admin-Token", ADMIN_TOKEN);
  if (session) {
    headers.set("X-Hookline-Identity", mintIdentity(session.sub, session.role));
  }
  headers.delete("cookie"); // never forward the browser session cookie to the backend

  return fetch(`${BACKEND_URL}${path}`, { ...init, headers, cache: "no-store" });
}
