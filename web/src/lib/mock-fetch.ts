/**
 * Phase 0 seam: `use*` hooks resolve their data through this. Today it returns the
 * mock value after a short delay (so skeletons render). In Phases 1–2 each hook's
 * query function is swapped to call the BFF (`api.get('/youtube-comments/…')`) — and only
 * the hook bodies change, never the components.
 */
export function mockFetch<T>(value: T, delayMs = 400): Promise<T> {
  return new Promise((resolve) => setTimeout(() => resolve(value), delayMs));
}
