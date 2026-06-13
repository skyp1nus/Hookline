"use client";

import { Check, CircleAlert, Globe, Link2, Lock } from "lucide-react";
import type { ComponentType, ReactNode } from "react";
import { useEffect, useState } from "react";
import { toast } from "sonner";

import { PageHeading } from "@/components/page-heading";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  useUpdateUploadSettings,
  useUploadSettings,
  type UploadVisibility,
} from "@/features/uploads/hooks";
import { YOUTUBE_CATEGORIES, YOUTUBE_LANGUAGES } from "@/features/uploads/youtube-meta";
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
  // categoryId / language use "" for None (field left unset on the upload).
  const [category, setCategory] = useState("");
  const [language, setLanguage] = useState("");
  const [publicStats, setPublicStats] = useState(true);

  useEffect(() => {
    if (!data) return;
    setVisibility(data.defaultVisibility);
    setKids(data.madeForKids);
    setAi(data.containsSyntheticMedia);
    setCategory(data.categoryId);
    setLanguage(data.language);
    setPublicStats(data.publicStatsViewable);
  }, [data]);

  const dirty =
    !!data &&
    (visibility !== data.defaultVisibility ||
      kids !== data.madeForKids ||
      ai !== data.containsSyntheticMedia ||
      category !== data.categoryId ||
      language !== data.language ||
      publicStats !== data.publicStatsViewable);

  async function save() {
    try {
      await update.mutateAsync({
        defaultVisibility: visibility,
        madeForKids: kids,
        containsSyntheticMedia: ai,
        categoryId: category,
        language,
        publicStatsViewable: publicStats,
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
            control={
              <Segmented value={ai} onChange={setAi} disabled={!data} options={YES_NO} />
            }
          />
          <SettingRow
            title="Category"
            desc="The YouTube category assigned to every upload (snippet.categoryId). None leaves it unset."
            control={
              <MetaSelect
                value={category}
                onChange={setCategory}
                disabled={!data}
                placeholder="Select category"
                options={YOUTUBE_CATEGORIES.map((c) => ({ value: c.id, label: c.label }))}
              />
            }
          />
          <SettingRow
            title="Video language"
            desc="Sets both the metadata and audio language (snippet.defaultLanguage + defaultAudioLanguage). None leaves both unset."
            control={
              <MetaSelect
                value={language}
                onChange={setLanguage}
                disabled={!data}
                placeholder="Select language"
                options={YOUTUBE_LANGUAGES.map((l) => ({ value: l.code, label: l.label }))}
              />
            }
          />
          <SettingRow
            title="Show like counts"
            desc="Shows the public like count on the watch page (status.publicStatsViewable)."
            last
            control={
              <Segmented
                value={publicStats}
                onChange={setPublicStats}
                disabled={!data}
                options={YES_NO}
              />
            }
          />
        </div>
      </Card>
    </div>
  );
}

/**
 * shadcn Select over a None-able list. Radix forbids an empty-string item value, so the "None" item uses the
 * "none" sentinel and we map "none" ↔ "" at the value/onChange boundary (the form + server use "" for None).
 */
function MetaSelect({
  value,
  onChange,
  options,
  placeholder,
  disabled,
}: {
  value: string;
  onChange: (v: string) => void;
  options: { value: string; label: string }[];
  placeholder?: string;
  disabled?: boolean;
}) {
  return (
    <Select
      value={value === "" ? "none" : value}
      onValueChange={(v) => onChange(v === "none" ? "" : v)}
      disabled={disabled}
    >
      <SelectTrigger className="w-[230px]">
        <SelectValue placeholder={placeholder} />
      </SelectTrigger>
      <SelectContent>
        <SelectItem value="none">None</SelectItem>
        {options.map((o) => (
          <SelectItem key={o.value} value={o.value}>
            {o.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
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
