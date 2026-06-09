import type { NextRequest } from "next/server";

import { backendFetch } from "@/lib/bff";
import { setSessionCookie } from "@/lib/auth/session";

interface BackendUser {
  id: string;
  email: string;
  role: string;
}

/** Validates credentials against the backend, then mints the BFF session cookie. */
export async function POST(req: NextRequest) {
  const { email, password } = (await req.json()) as { email?: string; password?: string };
  if (!email || !password) {
    return Response.json({ title: "validation", detail: "Email and password are required." }, { status: 400 });
  }

  const res = await backendFetch("/api/auth/login", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify({ email, password }),
  });

  if (!res.ok) {
    const problem = await res.text();
    return new Response(problem || null, {
      status: res.status,
      headers: { "content-type": "application/problem+json" },
    });
  }

  const user = (await res.json()) as BackendUser;
  await setSessionCookie({ sub: user.id, email: user.email, role: user.role });
  return Response.json({ id: user.id, email: user.email, role: user.role });
}
