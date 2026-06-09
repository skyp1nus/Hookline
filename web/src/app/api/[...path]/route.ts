import type { NextRequest } from "next/server";

import { backendFetch } from "@/lib/bff";

/**
 * Pathless BFF proxy: forwards `/api/*` to the backend's `/api/*`, attaching the admin
 * token + signed identity server-side (see backendFetch). `login` / `logout` are handled
 * by their own, more-specific route handlers and never reach here.
 */
async function proxy(req: NextRequest, ctx: { params: Promise<{ path: string[] }> }) {
  const { path } = await ctx.params;
  const backendPath = `/api/${(path ?? []).join("/")}${req.nextUrl.search}`;

  const hasBody = req.method !== "GET" && req.method !== "HEAD";
  const res = await backendFetch(backendPath, {
    method: req.method,
    body: hasBody ? await req.text() : undefined,
    headers: { "content-type": req.headers.get("content-type") ?? "application/json" },
  });

  const body = await res.text();
  return new Response(body || null, {
    status: res.status,
    headers: { "content-type": res.headers.get("content-type") ?? "application/json" },
  });
}

export const GET = proxy;
export const POST = proxy;
export const PATCH = proxy;
export const PUT = proxy;
export const DELETE = proxy;
