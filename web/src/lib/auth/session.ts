import { createHmac, timingSafeEqual } from "node:crypto";

import { cookies } from "next/headers";

import { SESSION_COOKIE_NAME } from "@/lib/auth/cookie";

/**
 * The browser-facing session: an httpOnly cookie minted by the BFF, signed (HMAC-SHA256)
 * with SESSION_SECRET. It carries the user's id + role so the BFF can mint a fresh
 * identity assertion per backend call. The cookie itself is NEVER forwarded to the backend.
 */
const COOKIE_NAME = SESSION_COOKIE_NAME;
const SESSION_SECRET = process.env.SESSION_SECRET ?? "";
const SECURE = process.env.COOKIE_SECURE === "true";
const TTL_SECONDS = 8 * 60 * 60;

export interface Session {
  sub: string;
  email: string;
  role: string;
  exp: number;
}

function base64url(input: Buffer): string {
  return input.toString("base64").replace(/=+$/, "").replace(/\+/g, "-").replace(/\//g, "_");
}

function base64urlToBuffer(value: string): Buffer {
  const padded = value.replace(/-/g, "+").replace(/_/g, "/");
  return Buffer.from(padded, "base64");
}

function sign(part1: string): string {
  return base64url(createHmac("sha256", SESSION_SECRET).update(part1, "ascii").digest());
}

export function createSessionToken(data: Omit<Session, "exp">): string {
  const payload: Session = { ...data, exp: Math.floor(Date.now() / 1000) + TTL_SECONDS };
  const part1 = base64url(Buffer.from(JSON.stringify(payload), "utf8"));
  return `${part1}.${sign(part1)}`;
}

export function verifySessionToken(token: string | undefined): Session | null {
  if (!token) return null;
  const dot = token.indexOf(".");
  if (dot <= 0) return null;

  const part1 = token.slice(0, dot);
  const provided = base64urlToBuffer(token.slice(dot + 1));
  const expected = base64urlToBuffer(sign(part1));
  if (provided.length !== expected.length || !timingSafeEqual(provided, expected)) {
    return null;
  }

  try {
    const session = JSON.parse(base64urlToBuffer(part1).toString("utf8")) as Session;
    if (!session.sub || session.exp <= Math.floor(Date.now() / 1000)) return null;
    return session;
  } catch {
    return null;
  }
}

export async function getSession(): Promise<Session | null> {
  const cookie = (await cookies()).get(COOKIE_NAME)?.value;
  return verifySessionToken(cookie);
}

export async function setSessionCookie(data: Omit<Session, "exp">): Promise<void> {
  (await cookies()).set(COOKIE_NAME, createSessionToken(data), {
    httpOnly: true,
    secure: SECURE,
    sameSite: "lax",
    path: "/",
    maxAge: TTL_SECONDS,
  });
}

export async function clearSessionCookie(): Promise<void> {
  (await cookies()).delete(COOKIE_NAME);
}
