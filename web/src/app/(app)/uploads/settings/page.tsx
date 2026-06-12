"use client";

import { Check, CircleAlert, Globe, Link2, Lock } from "lucide-react";
import type { ComponentType, ReactNode } from "react";
import { useEffect, useState } from "react";
import { toast } from "sonner";

import { PageHeading } from "@/components/page-heading";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  useUpdateUploadSettings,
  useUploadSettings,
  type UploadVisibility,
} from "@/features/uploads/hooks";
import { apiErrorMessage } from "@/lib/api/client";
import { cn } from "@/lib/utils";

const YES_NO: { value: boolean; label: string }[] = [
  { value: true, label: "Yes" },
  { value: false, label: "No" },
];

export default function UploadSettingsPage() {
  const { data } = useUploadSettings();
  const update = useUpdateUploadSettings();

  // Local form mirrors server state; explicit Save persists the diff (the design's Save / Saved button).
  const [visibility, setVisibility] = useState<UploadVisibility>("private");
  const [kids, setKids] = useState(false);
  const [ai, setAi] = useState(false);

  useEffect(() => {
    if (!data) return;
    setVisibility(data.defaultVisibility);
    setKids(data.madeForKids);
    setAi(data.containsSyntheticMedia);
  }, [data]);

  const dirty =
    !!data &&
    (visibility !== data.defaultVisibility ||
      kids !== data.madeForKids ||
      ai !== data.containsSyntheticMedia);

  async function save() {
    try {
      await update.mutateAsync({
        defaultVisibility: visibility,
        madeForKids: kids,
        containsSyntheticMedia: ai,
      });
      toast.success("Upload settings saved.");
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Settings"
        description="Default studio metadata applied to every video uploaded to YouTube. A route can override these in Mappings."
        actions={
          <Button size="sm" disabled={!dirty || update.isPending} onClick={save}>
            <Check className="size-3.5" />
            {dirty ? "Save changes" : "Saved"}
          </Button>
        }
      />

      <Card className="gap-0 p-0">
        <div className="px-5 pt-[18px]">
          <div className="text-[12.5px] font-semibold uppercase tracking-[0.04em] text-muted-foreground">
            Default video settings
          </div>
        </div>
        <div className="px-5">
          <SettingRow
            title="Privacy"
            desc="How each uploaded video is published on YouTube."
            control={
              <Segmented
                value={visibility}
                onChange={setVisibility}
                disabled={!data}
                options={[
                  { value: "public", label: "Public", icon: Globe },
                  { value: "unlisted", label: "Unlisted", icon: Link2 },
                  { value: "private", label: "Private", icon: Lock },
                ]}
              />
            }
          />
          <SettingRow
            title="Made for kids"
            desc={"Sets the audience declaration. Default: “No, it’s not made for kids.”"}
            note="Maps to the YouTube API selfDeclaredMadeForKids field."
            control={
              <Segmented value={kids} onChange={setKids} disabled={!data} options={YES_NO} />
            }
          />
          <SettingRow
            title="Altered or AI content"
            desc="Discloses realistic content that’s been meaningfully altered or generated with AI. Default: No."
            note="Applied only where the YouTube API exposes the disclosure field — otherwise ignored."
            last
            control={
              <Segmented value={ai} onChange={setAi} disabled={!data} options={YES_NO} />
            }
          />
        </div>
      </Card>
    </div>
  );
}

function SettingRow({
  title,
  desc,
  note,
  control,
  last,
}: {
  title: string;
  desc: string;
  note?: string;
  control: ReactNode;
  last?: boolean;
}) {
  return (
    <div
      className={cn(
        "flex flex-wrap items-start justify-between gap-6 py-[18px]",
        !last && "border-b",
      )}
    >
      <div className="min-w-0 max-w-[470px]">
        <div className="text-sm font-[560]">{title}</div>
        <p className="m-0 mt-[5px] text-[13px] leading-normal text-muted-foreground">{desc}</p>
        {note && (
          <div className="mt-2 flex items-center gap-1.5 text-[11.5px] text-muted-foreground">
            <CircleAlert className="size-[13px] shrink-0" />
            {note}
          </div>
        )}
      </div>
      <div className="shrink-0">{control}</div>
    </div>
  );
}

function Segmented<T extends string | boolean>({
  value,
  onChange,
  options,
  disabled,
}: {
  value: T;
  onChange: (v: T) => void;
  options: { value: T; label: string; icon?: ComponentType<{ className?: string }> }[];
  disabled?: boolean;
}) {
  return (
    <div
      className={cn(
        "inline-flex gap-1 rounded-[calc(var(--radius)-1px)] bg-muted p-1",
        disabled && "pointer-events-none opacity-50",
      )}
    >
      {options.map((o) => {
        const active = value === o.value;
        const Icon = o.icon;
        return (
          <button
            key={String(o.value)}
            type="button"
            onClick={() => onChange(o.value)}
            className={cn(
              "flex h-8 items-center gap-1.5 rounded-[calc(var(--radius)-4px)] px-[13px] text-[13px] font-medium transition-colors",
              active
                ? "bg-background text-foreground shadow-sm"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            {Icon && <Icon className="size-[15px]" />}
            {o.label}
          </button>
        );
      })}
    </div>
  );
}
