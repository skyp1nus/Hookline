"use client";

import { Bell, Moon, Search, Sun } from "lucide-react";
import { usePathname } from "next/navigation";
import { useTheme } from "next-themes";
import { Fragment, useEffect, useState } from "react";

import { Kbd } from "@/components/kbd";
import { usePlatform } from "@/components/platform-context";
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb";
import { Separator } from "@/components/ui/separator";
import { SidebarTrigger } from "@/components/ui/sidebar";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { buildBreadcrumbs, pathToRouteId } from "@/lib/nav";

const iconBtn =
  "inline-flex size-[34px] shrink-0 items-center justify-center rounded-md text-foreground outline-none transition-colors hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring";

export function SiteHeader({ onOpenCommand }: { onOpenCommand: () => void }) {
  const pathname = usePathname();
  const { platform } = usePlatform();
  const { resolvedTheme, setTheme } = useTheme();
  const [mounted, setMounted] = useState(false);
  useEffect(() => setMounted(true), []);

  const routeId = pathToRouteId(pathname) ?? "overview";
  const crumbs = buildBreadcrumbs(routeId, platform);
  const isDark = resolvedTheme === "dark";

  return (
    <header className="sticky top-0 z-40 flex h-14 shrink-0 items-center gap-2.5 border-b bg-background/80 px-4 backdrop-blur-md">
      <SidebarTrigger className="size-[34px]" />
      <Separator orientation="vertical" className="h-5" />

      <Breadcrumb className="min-w-0 overflow-hidden">
        <BreadcrumbList className="gap-1.5 sm:gap-1.5">
          {crumbs.map((crumb, i) => {
            const last = i === crumbs.length - 1;
            return (
              <Fragment key={`${crumb}-${i}`}>
                {i > 0 && <BreadcrumbSeparator />}
                <BreadcrumbItem className={last ? undefined : "hidden sm:flex"}>
                  {last ? (
                    <BreadcrumbPage className="text-[13.5px] font-[540]">{crumb}</BreadcrumbPage>
                  ) : (
                    <span className="text-[13.5px] text-muted-foreground">{crumb}</span>
                  )}
                </BreadcrumbItem>
              </Fragment>
            );
          })}
        </BreadcrumbList>
      </Breadcrumb>

      <div className="flex-1" />

      <button
        type="button"
        onClick={onOpenCommand}
        className="flex h-[34px] items-center gap-2 rounded-md border bg-background px-2.5 text-[13px] text-muted-foreground outline-none transition-colors hover:bg-accent focus-visible:ring-2 focus-visible:ring-ring"
      >
        <Search className="size-[15px]" />
        <span className="hidden pr-4 sm:inline">Search&hellip;</span>
        <Kbd>⌘K</Kbd>
      </button>

      <Tooltip>
        <TooltipTrigger asChild>
          <button type="button" className={`relative ${iconBtn}`} aria-label="Notifications">
            <Bell className="size-[17px]" />
            <span className="absolute right-2 top-2 size-1.5 rounded-full bg-primary" />
          </button>
        </TooltipTrigger>
        <TooltipContent>Notifications</TooltipContent>
      </Tooltip>

      <Tooltip>
        <TooltipTrigger asChild>
          <button
            type="button"
            onClick={() => setTheme(isDark ? "light" : "dark")}
            className={iconBtn}
            aria-label="Toggle theme"
          >
            {mounted && isDark ? <Sun className="size-[17px]" /> : <Moon className="size-[17px]" />}
          </button>
        </TooltipTrigger>
        <TooltipContent>{isDark ? "Light mode" : "Dark mode"}</TooltipContent>
      </Tooltip>
    </header>
  );
}
