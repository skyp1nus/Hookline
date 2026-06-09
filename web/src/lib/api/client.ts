/**
 * Same-origin client for the Next BFF (`/api/*`). The browser never sees the backend
 * admin token or the signed identity — the BFF injects those server-side. ProblemDetails
 * responses surface as ApiError with a human message.
 */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly problem?: unknown,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

function extractMessage(data: unknown, fallback: string): string {
  if (data && typeof data === "object") {
    const d = data as Record<string, unknown>;
    if (typeof d.detail === "string") return d.detail;
    if (typeof d.title === "string") return d.title;
    if (typeof d.error === "string") return d.error;
  }
  return fallback;
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(`/api${path}`, {
    method,
    credentials: "include",
    headers: body !== undefined ? { "content-type": "application/json" } : undefined,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });

  const text = await res.text();
  let data: unknown = null;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = text;
    }
  }

  if (!res.ok) {
    throw new ApiError(res.status, extractMessage(data, res.statusText || "Request failed"), data);
  }
  return data as T;
}

export const api = {
  get: <T>(path: string) => request<T>("GET", path),
  post: <T>(path: string, body?: unknown) => request<T>("POST", path, body),
  patch: <T>(path: string, body?: unknown) => request<T>("PATCH", path, body),
  del: <T>(path: string) => request<T>("DELETE", path),
};

export function apiErrorMessage(error: unknown): string {
  return error instanceof ApiError ? error.message : "Something went wrong.";
}
