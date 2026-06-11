"use client";

import { Bell, TriangleAlert } from "lucide-react";
import { type ComponentType, type ReactNode, useState } from "react";
import { toast } from "sonner";

import { ConfirmDialog } from "@/components/confirm-dialog";
import { PageHeading } from "@/components/page-heading";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { apiErrorMessage } from "@/lib/api/client";
import { cn } from "@/lib/utils";
import {
  type AlertSettings,
  useAlerts,
  usePauseAll,
  useResetData,
  useUpdateAlerts,
} from "@/features/system/hooks";

// Alerts persist server-side (shared settings store); the Danger Zone actions each run a real, audited
// backend cascade. Profile / Team & Access and the "delete workspace" action were cut — single-tenant has
// no account to delete (tracked in docs/backend-todo.md, blocked on a multi-tenant design).

const ALERTS: { key: keyof AlertSettings; title: string; desc: string }[] = [
  { key: "uploadFailures", title: "Upload failures", desc: "Email me when an upload job fails." },
  { key: "quotaWarnings", title: "Quota warnings", desc: "Alert when any API key passes 80% of daily quota." },
  { key: "oauthExpiry", title: "OAuth expiry", desc: "Warn 7 days before a connection token expires." },
  { key: "weeklyDigest", title: "Weekly digest", desc: "A Monday summary of forwards and uploads." },
];

const ALERT_DEFAULTS: AlertSettings = {
  uploadFailures: true,
  quotaWarnings: true,
  oauthExpiry: true,
  weeklyDigest: false,
};

export default function SettingsPage() {
  const { data } = useAlerts();
  const updateAlerts = useUpdateAlerts();
  const pauseAll = usePauseAll();
  const alerts = data ?? ALERT_DEFAULTS;

  const [pauseOpen, setPauseOpen] = useState(false);
  const [resetOpen, setResetOpen] = useState(false);

  async function toggleAlert(key: keyof AlertSettings, value: boolean) {
    try {
      await updateAlerts.mutateAsync({ [key]: value });
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }

  // Surface a partial fan-out honestly: if one module failed while the other paused, don't claim "all paused".
  async function handlePauseAll() {
    const res = await pauseAll.mutateAsync();
    if (res.partial) {
      toast.warning(
        `Paused ${res.paused} automation(s), but ${res.failed?.length ?? 0} module(s) failed — check the logs.`,
      );
    } else {
      toast.success("All automations paused.");
    }
  }

  return (
    <div className="flex max-w-[760px] flex-col gap-[22px]">
      <PageHeading title="Settings" description="Workspace alerts and controls." />

      {/* Alerts */}
      <SettingsSection
        icon={Bell}
        title="Alerts"
        desc="When Hookline should ping you. Preferences are saved; delivery is rolling out."
      >
        {ALERTS.map((alert, i) => (
          <div
            key={alert.key}
            className={cn(
              "flex items-center justify-between gap-4 py-3",
              i !== ALERTS.length - 1 && "border-b",
            )}
          >
            <div>
              <div className="text-[13.5px] font-[540]">{alert.title}</div>
              <div className="mt-0.5 text-[12.5px] text-muted-foreground">{alert.desc}</div>
            </div>
            <Switch
              checked={alerts[alert.key]}
              disabled={!data || updateAlerts.isPending}
              onCheckedChange={(v) => toggleAlert(alert.key, v)}
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
            <div className="flex-1">
              <div className="text-[15px] font-semibold tracking-[-0.01em]">Danger zone</div>
              <div className="text-[13px] text-muted-foreground">
                Irreversible actions. Each runs a real backend cascade and is written to the audit log.
              </div>
            </div>
          </div>

          <div className="flex items-center justify-between gap-4 border-b py-[13px]">
            <div>
              <div className="text-[13.5px] font-[540]">Pause all automations</div>
              <div className="mt-0.5 text-[12.5px] text-muted-foreground">
                Stop every comment mapping and upload route at once.
              </div>
            </div>
            <Button variant="outline" size="sm" className="shrink-0" onClick={() => setPauseOpen(true)}>
              Pause all
            </Button>
          </div>

          <div className="flex items-center justify-between gap-4 py-[13px]">
            <div>
              <div className="text-[13.5px] font-[540]">Reset workspace data</div>
              <div className="mt-0.5 text-[12.5px] text-muted-foreground">
                Clear jobs, history, dedup and quota state for both tools. Keeps mappings, connections and the
                audit log.
              </div>
            </div>
            <Button variant="destructive" size="sm" className="shrink-0" onClick={() => setResetOpen(true)}>
              Reset data
            </Button>
          </div>
        </div>
      </Card>

      <ConfirmDialog
        open={pauseOpen}
        onOpenChange={setPauseOpen}
        title="Pause all automations?"
        description="Every comment mapping and upload route is set to paused, and the comment polling jobs are torn down. You can resume each one individually afterwards."
        confirmLabel="Pause all"
        onConfirm={handlePauseAll}
      />

      <ResetDialog open={resetOpen} onOpenChange={setResetOpen} />
    </div>
  );
}

const RESET_PHRASE = "RESET";

function ResetDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  const reset = useResetData();
  const [phrase, setPhrase] = useState("");
  const [busy, setBusy] = useState(false);
  const armed = phrase.trim() === RESET_PHRASE;

  function close(next: boolean) {
    if (busy) return;
    onOpenChange(next);
    if (!next) setPhrase("");
  }

  async function handleConfirm() {
    if (!armed) return;
    setBusy(true);
    try {
      const res = await reset.mutateAsync();
      if (res.partial) {
        toast.warning(
          `Reset partial — cleared ${res.cleared} record(s), but ${res.failed?.length ?? 0} module(s) failed. Check the logs.`,
        );
      } else {
        toast.success(`Reset complete — cleared ${res.cleared} record(s).`);
      }
      close(false);
    } catch (error) {
      toast.error(apiErrorMessage(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open={open} onOpenChange={close}>
      <DialogContent showCloseButton={false}>
        <DialogHeader>
          <DialogTitle>Reset workspace data?</DialogTitle>
          <DialogDescription>
            Permanently clears upload jobs + history, the comment dedup ledger, the retry queue and quota
            counters for both tools. Mappings, connections, secrets and the audit log are kept. This can&apos;t
            be undone.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-1.5">
          <label htmlFor="reset-confirm" className="text-[12.5px] text-muted-foreground">
            Type <span className="mono font-medium text-foreground">{RESET_PHRASE}</span> to confirm
          </label>
          <Input
            id="reset-confirm"
            value={phrase}
            onChange={(e) => setPhrase(e.target.value)}
            placeholder={RESET_PHRASE}
            autoComplete="off"
            autoFocus
          />
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={() => close(false)} disabled={busy}>
            Cancel
          </Button>
          <Button variant="destructive" onClick={handleConfirm} disabled={!armed || busy}>
            {busy ? "Resetting…" : "Reset data"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
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
          <div className="flex-1">
            <div className="text-[15px] font-semibold tracking-[-0.01em]">{title}</div>
            {desc && <div className="text-[13px] text-muted-foreground">{desc}</div>}
          </div>
        </div>
        {children}
      </div>
    </Card>
  );
}
