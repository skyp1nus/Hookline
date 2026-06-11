"use client";

import { Plus } from "lucide-react";
import { type ComponentType, type ReactNode, useState } from "react";

import { ConfirmDialog } from "@/components/confirm-dialog";
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
  /** Whether the connection is live; drives the badge. Defaults to connected. */
  active?: boolean;
}

/** A connected account card: brand-tinted icon tile, name, handle, meta, actions. */
export function ConnectionCard({
  connection,
  icon: Icon,
  iconClassName,
  onDisconnect,
  disconnectTitle = "Disconnect?",
  disconnectDescription,
}: {
  connection: Connection;
  icon: GlyphIcon;
  /** Brand tint for the icon tile, e.g. "text-[#4A154B]" for Slack. */
  iconClassName?: string;
  /** Real disconnect mutation; runs inside a confirm dialog. Omit to honest-disable the button. */
  onDisconnect?: (id: string) => Promise<unknown>;
  disconnectTitle?: string;
  disconnectDescription?: string;
}) {
  const [confirmOpen, setConfirmOpen] = useState(false);
  const connected = connection.active !== false;

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
          <StatusBadge tone={connected ? "ok" : "warn"} dot>
            {connected ? "Connected" : "Inactive"}
          </StatusBadge>
        </div>
        <div className="mt-3.5">
          <div className="text-[14.5px] font-[580] tracking-[-0.01em]">{connection.name}</div>
          <div className="mono mt-0.5 text-xs text-muted-foreground">{connection.handle}</div>
        </div>
        <div className="mt-2.5 text-[12.5px] text-muted-foreground">{connection.meta}</div>
        {/* Disconnect is the only real action; a generic "Manage" had no distinct backend purpose and was
            removed rather than left disabled. The action renders only when a real disconnect handler is
            supplied (every current caller does) — never a faked/disabled button. */}
        {onDisconnect ? (
          <div className="mt-4 flex gap-2">
            <Button
              variant="outline"
              size="sm"
              className="w-full text-muted-foreground"
              onClick={() => setConfirmOpen(true)}
            >
              Disconnect
            </Button>
          </div>
        ) : null}
      </div>

      {onDisconnect ? (
        <ConfirmDialog
          open={confirmOpen}
          onOpenChange={setConfirmOpen}
          title={disconnectTitle}
          description={
            disconnectDescription ?? `Disconnect ${connection.name}? You can reconnect via OAuth later.`
          }
          confirmLabel="Disconnect"
          successMessage={`Disconnected ${connection.name}.`}
          onConfirm={() => onDisconnect(connection.id)}
        />
      ) : null}
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
