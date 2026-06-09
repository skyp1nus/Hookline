/** Hookline brand mark — a hook on a line (webhook motif), violet→indigo. */
export function BrandMark({
  size = 28,
  radius = 8,
  className,
}: {
  size?: number;
  radius?: number;
  className?: string;
}) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 32 32"
      fill="none"
      className={className}
      aria-hidden
    >
      <defs>
        <linearGradient id="hl-grad" x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="oklch(0.62 0.21 286)" />
          <stop offset="1" stopColor="oklch(0.5 0.23 264)" />
        </linearGradient>
      </defs>
      <rect x="0.5" y="0.5" width="31" height="31" rx={radius} fill="url(#hl-grad)" />
      <circle cx="19.5" cy="8.5" r="2" fill="white" />
      <path d="M19.5 8.5 V16 A4.6 4.6 0 1 1 14.9 20.6" stroke="white" strokeWidth="3" strokeLinecap="round" fill="none" />
      <path d="M14.9 20.6 16.4 18.7" stroke="white" strokeWidth="3" strokeLinecap="round" fill="none" />
    </svg>
  );
}
