"use client";

import { ChevronRight, ExternalLink, Reply, ShieldAlert, ThumbsUp, Video } from "lucide-react";

import { SlackIcon, YoutubeIcon } from "@/components/brand-icons";
import { StatusBadge } from "@/components/status";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import type { FeedComment, StatusTone } from "@/lib/mock-data";
import { cn } from "@/lib/utils";

/** flag → label + tone + icon, mirroring the prototype's FLAG_META. */
const FLAG_META: Record<string, { label: string; tone: StatusTone }> = {
  question: { label: "Question", tone: "info" },
  "top-fan": { label: "Top fan", tone: "ok" },
  spam: { label: "Spam", tone: "danger" },
};

function FlagBadge({ flag }: { flag: string }) {
  const meta = FLAG_META[flag];
  if (!meta) return null;
  return <StatusBadge tone={meta.tone}>{meta.label}</StatusBadge>;
}

function initials(name: string) {
  return name
    .split(" ")
    .map((w) => w[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
}

export function CommentCard({ item, compact = false }: { item: FeedComment; compact?: boolean }) {
  const held = item.held ?? false;
  return (
    <Card
      className={cn(
        "p-0",
        compact ? "shadow-none" : "shadow-[var(--shadow-sm)]",
        held && "ring-[color-mix(in_oklch,var(--danger)_26%,var(--border))]",
      )}
    >
      <div className={cn("flex gap-[13px]", compact ? "p-[14px]" : "p-[17px]")}>
        <Avatar size={compact ? "default" : "lg"} className="shrink-0">
          <AvatarFallback className="bg-primary/15 text-primary">{initials(item.author)}</AvatarFallback>
        </Avatar>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-[13.5px] font-[580]">{item.author}</span>
            <span className="mono text-[11.5px] text-muted-foreground">{item.handle}</span>
            {item.flags.map((f) => (
              <FlagBadge key={f} flag={f} />
            ))}
          </div>
          <p
            className={cn(
              "mt-1.5 text-[13.5px] leading-normal text-pretty",
              held ? "text-muted-foreground" : "text-foreground",
            )}
          >
            {item.text}
          </p>
          <div className="mt-[11px] flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
            <span className="inline-flex items-center gap-1.5">
              <Video className="size-[13px]" />
              {item.video}
            </span>
            <span className="opacity-50">·</span>
            <span className="inline-flex items-center gap-1.5">
              <YoutubeIcon size={13} />
              {item.ytChannel}
            </span>
            <ChevronRight className="size-3 opacity-60" />
            {held ? (
              <span className="inline-flex items-center gap-1.5 font-medium text-danger">
                <ShieldAlert className="size-[13px]" />
                Held — not forwarded
              </span>
            ) : (
              <span className="inline-flex items-center gap-1.5 font-medium text-foreground">
                <SlackIcon size={13} />
                <span className="mono">{item.slack}</span>
              </span>
            )}
          </div>
        </div>
        <div className="flex shrink-0 flex-col items-end gap-2">
          <span className="mono whitespace-nowrap text-[11.5px] text-muted-foreground">{item.time}</span>
          <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
            <ThumbsUp className="size-[13px]" />
            {item.likes}
          </span>
        </div>
      </div>
      {!compact && (
        <div className="flex justify-end gap-2 px-[17px] pb-[15px]">
          {held ? (
            <>
              <Button variant="ghost" size="sm" className="text-muted-foreground">
                Mark as spam
              </Button>
              <Button variant="outline" size="sm">
                <Reply className="size-3.5" />
                Forward anyway
              </Button>
            </>
          ) : (
            <>
              <Button variant="ghost" size="sm" className="text-muted-foreground">
                <ExternalLink className="size-3.5" />
                Open on YouTube
              </Button>
              <Button variant="outline" size="sm">
                <SlackIcon size={14} />
                View in Slack
              </Button>
            </>
          )}
        </div>
      )}
    </Card>
  );
}
