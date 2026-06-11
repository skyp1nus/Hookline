"use client";

import { useQuery } from "@tanstack/react-query";

import { api } from "@/lib/api/client";
import { type LogEntry } from "@/lib/mock-data";

/** Backend module ids the shared audit trail tags rows with. */
export type LogModule = "youtube-comments" | "youtube-uploads";

/** `AuditLogRecord` — one row from `GET /api/system/logs`. Severity is folded into `detail` as a leading
 *  `[Level]` marker (there is no severity column); `module` is null for host-level events. */
interface AuditLogRecord {
  id: number;
  timestamp: string;
  actor: string;
  role: string | null;
  module: string | null;
  action: string;
  entityType: string | null;
  entityId: string | null;
  detail: string | null;
}

/** `PagedResult<AuditLogRecord>` — the System→Logs endpoint shape. */
interface PagedAudit {
  items: AuditLogRecord[];
  page: number;
  pageSize: number;
  total: number;
}

const LEVEL_BY_MARKER: Record<string, LogEntry["level"]> = {
  error: "error",
  warning: "warn",
  warn: "warn",
  information: "info",
  info: "info",
  success: "success",
};

/** Pull the folded `[Level]` marker off the detail, returning the parsed level + the human remainder. */
function splitLevel(detail: string | null): { level: LogEntry["level"]; text: string } {
  if (!detail) return { level: "info", text: "" };
  // Match only the leading "[Level] " marker; take the remainder via slice so we avoid the dotAll (`s`)
  // flag (which needs an es2018 target) while still keeping any newlines in the detail body.
  const m = detail.match(/^\[(\w+)\]\s*/);
  if (m) {
    const level = LEVEL_BY_MARKER[m[1].toLowerCase()] ?? "info";
    return { level, text: detail.slice(m[0].length) };
  }
  return { level: "info", text: detail };
}

function toolFor(module: string | null): LogEntry["tool"] {
  if (module === "youtube-comments") return "comments";
  if (module === "youtube-uploads") return "uploads";
  return "system";
}

function clock(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime())
    ? ""
    : d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", hour12: false });
}

function ago(iso: string): string {
  const then = new Date(iso).getTime();
  if (Number.isNaN(then)) return "";
  const s = Math.max(0, Math.round((Date.now() - then) / 1000));
  if (s < 60) return `${s}s`;
  const m = Math.round(s / 60);
  if (m < 60) return `${m}m`;
  const h = Math.round(m / 60);
  if (h < 48) return `${h}h`;
  return `${Math.round(h / 24)}d`;
}

function toLogEntry(r: AuditLogRecord): LogEntry {
  const { level, text } = splitLevel(r.detail);
  const target = r.entityId ?? r.entityType ?? r.actor;
  return {
    id: String(r.id),
    tool: toolFor(r.module),
    level,
    message: text || r.action,
    target,
    time: clock(r.timestamp),
    ago: ago(r.timestamp),
  };
}

/**
 * Real system audit trail (`GET /api/system/logs`), mapped from `PagedResult<AuditLogRecord>` into the flat
 * `LogEntry` rows the page renders. `module` filters server-side (per-tool); level + search stay client-side
 * over the returned page. Polls every 10s so the "Live" indicator is honest rather than a fake stream.
 */
export function useLogs(module?: LogModule, pageSize = 100) {
  return useQuery({
    queryKey: ["system", "logs", module ?? "all", pageSize],
    queryFn: async () => {
      const qs = new URLSearchParams({ page: "1", pageSize: String(pageSize) });
      if (module) qs.set("module", module);
      const res = await api.get<PagedAudit>(`/system/logs?${qs.toString()}`);
      return res.items.map(toLogEntry);
    },
    refetchInterval: 10_000,
  });
}
