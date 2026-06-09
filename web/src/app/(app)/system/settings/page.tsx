"use client";

import {
  Bell,
  Mail,
  MoreHorizontal,
  Plus,
  SlidersHorizontal,
  Trash2,
  TriangleAlert,
  User,
  Users,
} from "lucide-react";
import { type ComponentType, type ReactNode, useState } from "react";

import { PageHeading } from "@/components/page-heading";
import { StatusBadge } from "@/components/status";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { type StatusTone, type TeamMember } from "@/lib/mock-data";
import { cn } from "@/lib/utils";
import { useTeam } from "@/features/system/hooks";

const ROLE_TONE: Record<TeamMember["role"], StatusTone> = {
  Owner: "info",
  Admin: "ok",
  Editor: "neutral",
  Viewer: "neutral",
};

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
    desc: "Permanently remove Daniel’s Channel and all connections.",
    label: "Delete",
    destructive: true,
  },
];

function initials(name: string) {
  return name
    .split(" ")
    .map((w) => w[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
}

export default function SettingsPage() {
  const { data } = useTeam();
  const [name, setName] = useState("Daniel Cole");
  const [alerts, setAlerts] = useState<Record<string, boolean>>(
    Object.fromEntries(ALERTS.map((a) => [a.id, a.on])),
  );

  const team = data ?? [];

  return (
    <div className="flex max-w-[760px] flex-col gap-[22px]">
      <PageHeading title="Settings" description="Profile, team access, and workspace controls." />

      {/* Profile */}
      <SettingsSection icon={User} title="Profile" desc="How you appear across Hookline.">
        <div className="mb-[18px] flex items-center gap-4">
          <Avatar size="lg" className="size-14">
            <AvatarFallback className="bg-primary/15 text-primary">{initials(name)}</AvatarFallback>
          </Avatar>
          <div className="flex gap-2">
            <Button variant="outline" size="sm">
              Upload photo
            </Button>
            <Button variant="ghost" size="sm" className="text-muted-foreground">
              Remove
            </Button>
          </div>
        </div>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <Field label="Full name">
            <Input value={name} onChange={(e) => setName(e.target.value)} />
          </Field>
          <Field label="Email" hint="Used for sign-in and alert notifications.">
            <Input value="daniel@hookline.io" disabled className="opacity-70" />
          </Field>
        </div>
        <div className="flex justify-end gap-2">
          <Button size="sm">Save changes</Button>
        </div>
      </SettingsSection>

      {/* Team & access */}
      <Card className="overflow-hidden p-0">
        <div className="flex items-start justify-between gap-3 px-5 pb-3.5 pt-5">
          <div className="flex items-center gap-2.5">
            <SectionIcon icon={Users} />
            <div>
              <div className="text-[15px] font-semibold tracking-[-0.01em]">Team &amp; access</div>
              <div className="text-[13px] text-muted-foreground">
                People who can manage these automations.
              </div>
            </div>
          </div>
          <Button variant="outline" size="sm">
            <Plus className="size-3.5" />
            Invite
          </Button>
        </div>
        <div className="border-t">
          {team.map((m, i) => (
            <div
              key={m.id}
              className={cn(
                "flex items-center gap-3 px-5 py-3",
                i !== team.length - 1 && "border-b",
              )}
            >
              <Avatar className="size-[34px]">
                <AvatarFallback className="bg-primary/15 text-primary text-xs">
                  {initials(m.name)}
                </AvatarFallback>
              </Avatar>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-[7px] text-[13.5px] font-[540]">
                  {m.name}
                  {m.you && (
                    <span className="text-[11px] font-normal text-muted-foreground">(you)</span>
                  )}
                </div>
                <div className="mono text-[11.5px] text-muted-foreground">{m.email}</div>
              </div>
              <StatusBadge tone={ROLE_TONE[m.role]}>{m.role}</StatusBadge>
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button variant="ghost" size="icon">
                    <MoreHorizontal className="size-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem>
                    <SlidersHorizontal className="size-4" />
                    Change role
                  </DropdownMenuItem>
                  <DropdownMenuItem>
                    <Mail className="size-4" />
                    Resend invite
                  </DropdownMenuItem>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem variant="destructive">
                    <Trash2 className="size-4" />
                    Remove
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
          ))}
        </div>
      </Card>

      {/* Alerts */}
      <SettingsSection icon={Bell} title="Alerts" desc="When Hookline should ping you.">
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
            <Switch
              checked={alerts[a.id]}
              onCheckedChange={(v) => setAlerts((prev) => ({ ...prev, [a.id]: v }))}
            />
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
            <div>
              <div className="text-[15px] font-semibold tracking-[-0.01em]">Danger zone</div>
              <div className="text-[13px] text-muted-foreground">
                Irreversible actions. Proceed with care.
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
              <Button
                variant={a.destructive ? "destructive" : "outline"}
                size="sm"
                className="shrink-0"
              >
                {a.label}
              </Button>
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
  children,
}: {
  icon: ComponentType<{ className?: string }>;
  title: string;
  desc?: string;
  children: ReactNode;
}) {
  return (
    <Card className="p-0">
      <div className="p-5">
        <div className="mb-4 flex items-center gap-2.5">
          <SectionIcon icon={icon} />
          <div>
            <div className="text-[15px] font-semibold tracking-[-0.01em]">{title}</div>
            {desc && <div className="text-[13px] text-muted-foreground">{desc}</div>}
          </div>
        </div>
        {children}
      </div>
    </Card>
  );
}

function Field({
  label,
  hint,
  children,
}: {
  label: string;
  hint?: string;
  children: ReactNode;
}) {
  return (
    <div className="mb-4">
      <label className="mb-1.5 block text-[12.5px] font-[540]">{label}</label>
      {children}
      {hint && <div className="mt-[5px] text-[11.5px] text-muted-foreground">{hint}</div>}
    </div>
  );
}
