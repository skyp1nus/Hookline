import type { ComponentProps } from "react";

/**
 * Brand glyphs. This build's lucide fork dropped the brand icons
 * (youtube/slack/linkedin), so we ship the design's hand-drawn versions.
 * Sized by `size` (default 16) or by a CSS `size-*` class from the parent.
 */
type IconProps = { size?: number } & ComponentProps<"svg">;

export function SlackIcon({ size = 16, ...props }: IconProps) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="currentColor" {...props}>
      <path d="M6 15a2 2 0 1 1-2-2h2zM7 15a2 2 0 1 1 4 0v5a2 2 0 1 1-4 0z" />
      <path d="M9 6a2 2 0 1 1 2-2v2zM9 7a2 2 0 1 1 0 4H4a2 2 0 1 1 0-4z" />
      <path d="M18 9a2 2 0 1 1 2 2h-2zM17 9a2 2 0 1 1-4 0V4a2 2 0 1 1 4 0z" />
      <path d="M15 18a2 2 0 1 1-2 2v-2zM15 17a2 2 0 1 1 0-4h5a2 2 0 1 1 0 4z" />
    </svg>
  );
}

export function YoutubeIcon({ size = 16, ...props }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      {...props}
    >
      <path d="M22 8.6a2.8 2.8 0 0 0-1.9-2C18.4 6.2 12 6.2 12 6.2s-6.4 0-8.1.4A2.8 2.8 0 0 0 2 8.6 29 29 0 0 0 1.7 12 29 29 0 0 0 2 15.4a2.8 2.8 0 0 0 1.9 2c1.7.4 8.1.4 8.1.4s6.4 0 8.1-.4a2.8 2.8 0 0 0 1.9-2 29 29 0 0 0 .3-3.4 29 29 0 0 0-.3-3.4z" />
      <path d="m10 15 5-3-5-3z" fill="currentColor" />
    </svg>
  );
}

export function LinkedinIcon({ size = 16, ...props }: IconProps) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      {...props}
    >
      <path d="M16 8a6 6 0 0 1 6 6v7h-4v-7a2 2 0 0 0-2-2 2 2 0 0 0-2 2v7h-4v-7a6 6 0 0 1 6-6z" />
      <path d="M2 9h4v12H2z" />
      <path d="M4 2a2 2 0 1 0 0 4 2 2 0 0 0 0-4z" />
    </svg>
  );
}

/** Google's multi-color mark (fixed brand colors, not theme-driven). */
export function GoogleIcon({ size = 16, ...props }: IconProps) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" {...props}>
      <path fill="#4285F4" d="M22.5 12.2c0-.7-.1-1.4-.2-2H12v3.8h5.9a5 5 0 0 1-2.2 3.3v2.7h3.6c2-1.9 3.2-4.7 3.2-7.8z" />
      <path fill="#34A853" d="M12 23c2.9 0 5.4-1 7.2-2.6l-3.6-2.7c-1 .7-2.3 1-3.6 1-2.8 0-5.1-1.8-6-4.3H2.3v2.8A11 11 0 0 0 12 23z" />
      <path fill="#FBBC05" d="M6 14.4a6.6 6.6 0 0 1 0-4.2V7.4H2.3a11 11 0 0 0 0 9.8z" />
      <path fill="#EA4335" d="M12 5.5c1.6 0 3 .5 4.1 1.6l3.1-3.1A11 11 0 0 0 2.3 7.4L6 10.2c.9-2.6 3.2-4.7 6-4.7z" />
    </svg>
  );
}
