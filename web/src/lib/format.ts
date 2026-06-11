/** en-US number formatting (7,300 not 7 300). */
export function formatNumber(n: number): string {
  return n.toLocaleString("en-US");
}

/** Seconds → compact duration, e.g. 184 → "3m 04s", 47 → "47s". */
export function formatDuration(seconds: number): string {
  if (seconds < 60) return `${seconds}s`;
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}m ${String(s).padStart(2, "0")}s`;
}

/** Megabytes → "842 MB" / "1.21 GB". */
export function formatMB(mb: number): string {
  if (mb >= 1000) return `${(mb / 1000).toFixed(2)} GB`;
  return `${mb} MB`;
}

export function clampPercent(value: number): number {
  return Math.max(0, Math.min(100, value));
}
