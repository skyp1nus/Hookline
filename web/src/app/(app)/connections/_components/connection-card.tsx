"use client";

import { Plus } from "lucide-react";
import type { ComponentType, ReactNode } from "react";

import { StatusBadge } from "@/components/status";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { cn } from "@/lib/utils";

/** A glyph that accepts `className` / `size`, e.g. SlackIcon, YoutubeIcon. */
export type GlyphIcon = ComponentType<{ className?: string; size?: number }>;

export interface Connection {
  id: string;
  /** Account / workspace / channel name. */
  name: string;
  /** Domain, handle, or email — rendered mono. */
  handle: string;
  /** A short status line, e.g. "5 channels mapped · OAuth valid". */
  meta: ReactNode;
}

/** A connected account card: brand-tinted icon tile, name, handle, meta, actions. */
export function ConnectionCard({
  connection,
  icon: Icon,
  iconClassName,
  onDisconnect,
  onManage,
}: {
  connection: Connection;
  icon: GlyphIcon;
  /** Brand tint for the icon tile, e.g. "text-[#4A154B]" for Slack. */
  iconClassName?: string;
  onDisconnect?: () => void;
  onManage?: () => void;
}) {
  return (
    <Card className="p-0">
      <div className="flex flex-col p-[18px]">
        <div className="flex items-start justify-between">
          <div
            className={cn(
              "flex size-10 items-center justify-center rounded-[10px] border bg-background",
              iconClassName,
            )}
          >
            <Icon className="size-5" />
          </div>
          <StatusBadge tone="ok" dot>
            Connected
          </StatusBadge>
        </div>
        <div className="mt-3.5">
          <div className="text-[14.5px] font-[580] tracking-[-0.01em]">{connection.name}</div>
          <div className="mono mt-0.5 text-xs text-muted-foreground">{connection.handle}</div>
        </div>
        <div className="mt-2.5 text-[12.5px] text-muted-foreground">{connection.meta}</div>
        <div className="mt-4 flex gap-2">
          <Button variant="outline" size="sm" className="flex-1" onClick={onManage}>
            Manage
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className="text-muted-foreground"
            onClick={onDisconnect}
          >
            Disconnect
          </Button>
        </div>
      </div>
    </Card>
  );
}

/** The dashed "connect a new one" card. Renders as a link when `href` is set. */
export function ConnectCard({
  title,
  subtitle,
  href,
  onClick,
}: {
  title: string;
  subtitle: string;
  href?: string;
  onClick?: () => void;
}) {
  const inner = (
    <>
      <div className="flex size-10 items-center justify-center rounded-[10px] border">
        <Plus className="size-[19px]" />
      </div>
      <div className="text-sm font-[560] text-foreground">{title}</div>
      <div className="text-[12.5px]">{subtitle}</div>
    </>
  );
  const className =
    "flex min-h-[150px] w-full flex-col items-center justify-center gap-2 p-5 text-center text-muted-foreground outline-none transition-colors hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring";

  return (
    <Card className="border-dashed bg-transparent p-0 ring-0">
      {href ? (
        <a href={href} className={className}>
          {inner}
        </a>
      ) : (
        <button type="button" onClick={onClick} className={className}>
          {inner}
        </button>
      )}
    </Card>
  );
}

/** Shared responsive grid for the connection card pages. */
export function ConnectionGrid({ children }: { children: ReactNode }) {
  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">{children}</div>
  );
}
