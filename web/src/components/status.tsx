import type { ReactNode } from "react";

import type { StatusTone } from "@/lib/mock-data";
import { cn } from "@/lib/utils";

/** Tone → token-driven classes. Mirrors the design's status pill system. */
const TONE: Record<StatusTone, { text: string; bg: string; dot: string; border: string }> = {
  neutral: { text: "text-muted-foreground", bg: "bg-muted", dot: "bg-muted-foreground", border: "border-muted-foreground/35" },
  info: { text: "text-info", bg: "bg-info-bg", dot: "bg-info", border: "border-info/35" },
  ok: { text: "text-ok", bg: "bg-ok-bg", dot: "bg-ok", border: "border-ok/35" },
  warn: { text: "text-warn", bg: "bg-warn-bg", dot: "bg-warn", border: "border-warn/35" },
  danger: { text: "text-danger", bg: "bg-danger-bg", dot: "bg-danger", border: "border-danger/35" },
};

export function StatusDot({
  tone = "neutral",
  pulse = false,
  className,
}: {
  tone?: StatusTone;
  pulse?: boolean;
  className?: string;
}) {
  return (
    <span
      className={cn(
        "inline-block size-1.5 shrink-0 rounded-full",
        TONE[tone].dot,
        pulse && "animate-pulse-dot",
        className,
      )}
    />
  );
}

export function StatusBadge({
  tone = "neutral",
  dot = false,
  pulse = false,
  outline = false,
  className,
  children,
}: {
  tone?: StatusTone;
  dot?: boolean;
  pulse?: boolean;
  outline?: boolean;
  className?: string;
  children: ReactNode;
}) {
  return (
    <span
      className={cn(
        "inline-flex h-[22px] items-center gap-1.5 whitespace-nowrap rounded-full px-[9px] text-xs font-medium leading-none",
        outline
          ? cn("border bg-transparent", TONE[tone].text, TONE[tone].border)
          : cn(TONE[tone].bg, TONE[tone].text),
        className,
      )}
    >
      {dot && <StatusDot tone={tone} pulse={pulse} />}
      {children}
    </span>
  );
}

/** Maps a job/feed status string to a tone + label, like the prototype. */
export const JOB_STATUS: Record<string, { tone: StatusTone; label: string }> = {
  queued: { tone: "neutral", label: "Queued" },
  downloading: { tone: "info", label: "Downloading" },
  uploading: { tone: "info", label: "Uploading" },
  processing: { tone: "info", label: "Processing" },
  done: { tone: "ok", label: "Done" },
  failed: { tone: "danger", label: "Failed" },
  canceled: { tone: "neutral", label: "Canceled" },
};
