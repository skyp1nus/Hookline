import { NextResponse, type NextRequest } from "next/server";

import { SESSION_COOKIE_NAME } from "@/lib/auth/cookie";

/**
 * UX auth wall: redirect unauthenticated users to /login (and away from the auth screens
 * once signed in). This is a cheap presence check — the real signature verification
 * happens server-side in the BFF before any identity is minted. The fast-dev no-auth
 * toggle skips the wall entirely.
 */
export function middleware(req: NextRequest) {
  if (process.env.NEXT_PUBLIC_NO_AUTH === "true") {
    return NextResponse.next();
  }

  const hasSession = req.cookies.has(SESSION_COOKIE_NAME);
  const { pathname } = req.nextUrl;
  const isAuthScreen = pathname.startsWith("/login") || pathname.startsWith("/bootstrap-owner");

  if (!hasSession && !isAuthScreen) {
    return NextResponse.redirect(new URL("/login", req.url));
  }
  if (hasSession && pathname.startsWith("/login")) {
    return NextResponse.redirect(new URL("/", req.url));
  }
  return NextResponse.next();
}

export const config = {
  // Skip the auth wall for API, Next internals, and any static file (has an extension):
  // otherwise public assets like /hookline-icon.png and the App-Router /icon.png favicons
  // get 307'd to /login, which also breaks /_next/image (the optimizer fetches the source
  // asset internally and receives the login HTML instead of an image -> 400).
  matcher: [
    "/((?!api|_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp|avif|ico|woff2?|ttf)).*)",
  ],
};
