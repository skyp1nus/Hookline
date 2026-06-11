"use client";

import type { ReactNode } from "react";

import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { cn } from "@/lib/utils";

/**
 * Honest "not yet" affordance for a control whose backend does not exist yet. Wraps a DISABLED control so
 * hovering still surfaces the reason (a disabled element swallows pointer events, so the span trigger
 * carries the tooltip). Never fakes success — the wrapped control must be `disabled`. The matching backend
 * work is tracked in docs/backend-todo.md.
 */
export function NotYet({
  children,
  reason = "Not wired yet — backend pending.",
  className,
}: {
  children: ReactNode;
  reason?: string;
  className?: string;
}) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span className={cn("inline-flex cursor-not-allowed", className)}>{children}</span>
      </TooltipTrigger>
      <TooltipContent>{reason}</TooltipContent>
    </Tooltip>
  );
}

/** A small muted "Not yet" pill to sit beside a disabled control where a label reads clearer than a tooltip. */
export function NotYetBadge({ className }: { className?: string }) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full border border-dashed px-1.5 py-px text-[10.5px] font-medium uppercase tracking-wide text-muted-foreground",
        className,
      )}
    >
      Not yet
    </span>
  );
}
