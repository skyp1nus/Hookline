"use client";

import { Check, ChevronsUpDown, Plus } from "lucide-react";

import { usePlatform } from "@/components/platform-context";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { PLATFORMS } from "@/lib/platforms";
import { cn } from "@/lib/utils";

export function PlatformSwitcher() {
  const { platform, setPlatformId } = usePlatform();
  const Icon = platform.icon;

  return (
    <DropdownMenu>
      <DropdownMenuTrigger className="flex h-[38px] w-full items-center gap-2 rounded-md border bg-sidebar-accent px-2.5 outline-none transition-colors hover:bg-sidebar-accent/70 focus-visible:ring-2 focus-visible:ring-sidebar-ring">
        <span
          className="flex size-[22px] shrink-0 items-center justify-center rounded-md text-white"
          style={{ background: platform.color }}
        >
          <Icon className="size-3.5" />
        </span>
        <span className="flex-1 truncate text-left text-[13px] font-[560]">{platform.name}</span>
        <ChevronsUpDown className="size-3.5 shrink-0 text-muted-foreground" />
      </DropdownMenuTrigger>
      <DropdownMenuContent
        align="start"
        className="w-(--radix-dropdown-menu-trigger-width) min-w-56"
      >
        <DropdownMenuLabel className="text-[11px] uppercase tracking-[0.04em] text-muted-foreground">
          Platforms
        </DropdownMenuLabel>
        {PLATFORMS.map((pf) => {
          const PfIcon = pf.icon;
          const active = pf.id === platform.id;
          return (
            <DropdownMenuItem
              key={pf.id}
              disabled={pf.soon}
              onSelect={() => {
                if (!pf.soon) setPlatformId(pf.id);
              }}
              className="gap-2"
            >
              <span
                className={cn(
                  "flex size-[22px] shrink-0 items-center justify-center rounded-md",
                  pf.soon ? "bg-muted text-muted-foreground" : "text-white",
                )}
                style={pf.soon ? undefined : { background: pf.color }}
              >
                <PfIcon className="size-3.5" />
              </span>
              <div className="flex-1 overflow-hidden leading-tight">
                <div className={cn("text-[13px]", active && "font-[560]")}>{pf.name}</div>
                {pf.handle && (
                  <div className="truncate text-[11px] text-muted-foreground">{pf.handle}</div>
                )}
              </div>
              {pf.soon ? (
                <span className="rounded-full bg-muted px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-[0.03em] text-muted-foreground">
                  Soon
                </span>
              ) : (
                active && <Check className="size-3.5 text-primary" />
              )}
            </DropdownMenuItem>
          );
        })}
        <DropdownMenuSeparator />
        <DropdownMenuItem className="gap-2 text-muted-foreground">
          <Plus className="size-3.5" />
          Connect platform
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
