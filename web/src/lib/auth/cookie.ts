/** The BFF session cookie name. Kept dependency-free so the Edge middleware can
 *  import it without pulling in node:crypto (which lives in session.ts). */
export const SESSION_COOKIE_NAME = "hl_session";
