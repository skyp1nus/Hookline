import { ApiError } from "@/lib/api/client";

/**
 * Fetches a CSV export from the BFF (`/api/*`) and saves it as a named file. The endpoint streams
 * `text/csv`; the filename is set here because the BFF proxy forwards content-type but not
 * content-disposition. Throws `ApiError` on a non-OK response so callers can toast the message.
 */
export async function downloadCsv(path: string, filename: string): Promise<void> {
  const res = await fetch(`/api${path}`, { credentials: "include" });
  const text = await res.text();
  if (!res.ok) {
    throw new ApiError(res.status, res.statusText || "Export failed");
  }

  const url = URL.createObjectURL(new Blob([text], { type: "text/csv;charset=utf-8" }));
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

/** `hookline-<slug>-YYYYMMDD.csv` — a stable, sortable export filename. */
export function csvFilename(slug: string): string {
  const d = new Date();
  const stamp = `${d.getFullYear()}${String(d.getMonth() + 1).padStart(2, "0")}${String(d.getDate()).padStart(2, "0")}`;
  return `hookline-${slug}-${stamp}.csv`;
}
