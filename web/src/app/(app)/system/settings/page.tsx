"use client";

import { Bell, TriangleAlert } from "lucide-react";
import { type ComponentType, type ReactNode } from "react";

import { NotYet, NotYetBadge } from "@/components/not-yet";
import { PageHeading } from "@/components/page-heading";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Switch } from "@/components/ui/switch";
import { cn } from "@/lib/utils";

// Profile + Team & Access were removed in the control-panel scope-cut. Alerts and the Danger Zone are kept
// as wanted features but have NO backend yet — every control here is honestly DISABLED ("not yet"), never
// faked. The missing endpoints + the domain columns they need are specced in docs/backend-todo.md.

const ALERTS: { id: string; title: string; desc: string; on: boolean }[] = [
  { id: "uploads", title: "Upload failures", desc: "Email me when an upload job fails.", on: true },
  { id: "quota", title: "Quota warnings", desc: "Alert when any API key passes 80% of daily quota.", on: true },
  { id: "oauth", title: "OAuth expiry", desc: "Warn 7 days before a connection token expires.", on: true },
  { id: "digest", title: "Weekly digest", desc: "A Monday summary of forwards and uploads.", on: false },
];

const DANGER_ACTIONS: { title: string; desc: string; label: string; destructive: boolean }[] = [
  {
    title: "Pause all automations",
    desc: "Stop every comment mapping and upload route at once.",
    label: "Pause all",
    destructive: false,
  },
  {
    title: "Reset workspace data",
    desc: "Clear all logs, history, and mappings for this workspace.",
    label: "Reset data",
    destructive: true,
  },
  {
    title: "Delete workspace",
    desc: "Permanently remove this workspace and all connections.",
    label: "Delete",
    destructive: true,
  },
];

export default function SettingsPage() {
  return (
    <div className="flex max-w-[760px] flex-col gap-[22px]">
      <PageHeading title="Settings" description="Workspace alerts and controls." />

      {/* Alerts */}
      <SettingsSection
        icon={Bell}
        title="Alerts"
        desc="When Hookline should ping you."
        aside={<NotYetBadge />}
      >
        {ALERTS.map((a, i) => (
          <div
            key={a.id}
            className={cn(
              "flex items-center justify-between gap-4 py-3",
              i !== ALERTS.length - 1 && "border-b",
            )}
          >
            <div>
              <div className="text-[13.5px] font-[540]">{a.title}</div>
              <div className="mt-0.5 text-[12.5px] text-muted-foreground">{a.desc}</div>
            </div>
            {/* No persistence endpoint yet — disabled, not faked. Reflects the intended default only. */}
            <NotYet reason="Alert preferences aren't persisted yet — backend pending.">
              <Switch checked={a.on} disabled className="pointer-events-none" />
            </NotYet>
          </div>
        ))}
      </SettingsSection>

      {/* Danger zone */}
      <Card className="p-0 ring-[color-mix(in_oklch,var(--danger)_35%,var(--border))]">
        <div className="p-5">
          <div className="mb-4 flex items-center gap-2.5">
            <div className="flex size-[30px] items-center justify-center rounded-lg bg-danger-bg text-danger">
              <TriangleAlert className="size-4" />
            </div>
            <div className="flex-1">
              <div className="flex items-center gap-2">
                <div className="text-[15px] font-semibold tracking-[-0.01em]">Danger zone</div>
                <NotYetBadge />
              </div>
              <div className="text-[13px] text-muted-foreground">
                Irreversible actions. Disabled until the backend exists — never simulated.
              </div>
            </div>
          </div>
          {DANGER_ACTIONS.map((a, i) => (
            <div
              key={a.title}
              className={cn(
                "flex items-center justify-between gap-4 py-[13px]",
                i !== DANGER_ACTIONS.length - 1 && "border-b",
              )}
            >
              <div>
                <div className="text-[13.5px] font-[540]">{a.title}</div>
                <div className="mt-0.5 text-[12.5px] text-muted-foreground">{a.desc}</div>
              </div>
              <NotYet reason="No backend action yet — disabled so it can't be faked.">
                <Button
                  variant={a.destructive ? "destructive" : "outline"}
                  size="sm"
                  className="pointer-events-none shrink-0"
                  disabled
                >
                  {a.label}
                </Button>
              </NotYet>
            </div>
          ))}
        </div>
      </Card>
    </div>
  );
}

function SectionIcon({ icon: Icon }: { icon: ComponentType<{ className?: string }> }) {
  return (
    <div className="flex size-[30px] items-center justify-center rounded-lg bg-muted text-muted-foreground">
      <Icon className="size-4" />
    </div>
  );
}

function SettingsSection({
  icon,
  title,
  desc,
  aside,
  children,
}: {
  icon: ComponentType<{ className?: string }>;
  title: string;
  desc?: string;
  aside?: ReactNode;
  children: ReactNode;
}) {
  return (
    <Card className="p-0">
      <div className="p-5">
        <div className="mb-4 flex items-center gap-2.5">
          <SectionIcon icon={icon} />
          <div className="flex-1">
            <div className="flex items-center gap-2">
              <div className="text-[15px] font-semibold tracking-[-0.01em]">{title}</div>
              {aside}
            </div>
            {desc && <div className="text-[13px] text-muted-foreground">{desc}</div>}
          </div>
        </div>
        {children}
      </div>
    </Card>
  );
}
