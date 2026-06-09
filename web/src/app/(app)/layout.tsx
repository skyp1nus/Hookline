"use client";

import type { CSSProperties } from "react";
import { useEffect, useState } from "react";

import { CommandPalette } from "@/components/command-palette";
import { PlatformProvider } from "@/components/platform-context";
import { AppSidebar } from "@/components/shell/app-sidebar";
import { SiteHeader } from "@/components/shell/site-header";
import { SidebarInset, SidebarProvider } from "@/components/ui/sidebar";

export default function AppLayout({ children }: { children: React.ReactNode }) {
  const [cmdOpen, setCmdOpen] = useState(false);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setCmdOpen((o) => !o);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  return (
    <PlatformProvider>
      <SidebarProvider style={{ "--sidebar-width-icon": "4rem" } as CSSProperties}>
        <AppSidebar />
        <SidebarInset>
          <SiteHeader onOpenCommand={() => setCmdOpen(true)} />
          <div className="mx-auto w-full max-w-[1200px] px-7 pb-[60px] pt-[26px]">{children}</div>
        </SidebarInset>
        <CommandPalette open={cmdOpen} onOpenChange={setCmdOpen} />
      </SidebarProvider>
    </PlatformProvider>
  );
}
