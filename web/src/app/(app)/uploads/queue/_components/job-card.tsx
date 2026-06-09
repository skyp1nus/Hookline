"use client";

import {
  Ban,
  ChevronRight,
  CircleAlert,
  CircleCheck,
  CircleX,
  Clock,
  CloudUpload,
  Download,
  ExternalLink,
  FileVideo,
  Lock,
  MoreHorizontal,
  RefreshCw,
  ScrollText,
  X,
} from "lucide-react";
import type { ComponentType } from "react";

import { SlackIcon, YoutubeIcon } from "@/components/brand-icons";
import { ProgressBar } from "@/components/progress-bar";
import { JOB_STATUS, StatusBadge } from "@/components/status";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { formatNumber } from "@/lib/format";
import type { Job } from "@/lib/mock-data";
import { cn } from "@/lib/utils";

/** mm:ss readout used for elapsed / ETA. */
function fmtTime(s: number | null): string {
  if (s == null) return "—";
  const m = Math.floor(s / 60);
  const sec = s % 60;
  return `${m}:${String(sec).padStart(2, "0")}`;
}

const STATUS_ICON: Record<Job["status"], ComponentType<{ className?: string }>> = {
  queued: Clock,
  downloading: Download,
  uploading: CloudUpload,
  processing: CloudUpload,
  done: CircleCheck,
  failed: CircleX,
  canceled: Ban,
};

export function JobCard({
  job,
  onCancel,
  onRetry,
  onDismiss,
}: {
  job: Job;
  onCancel: (id: string) => void;
  onRetry: (id: string) => void;
  onDismiss: (id: string) => void;
}) {
  const st = JOB_STATUS[job.status];
  const active = job.status === "downloading" || job.status === "uploading";
  const StatusIcon = STATUS_ICON[job.status];
  const [sourceLabel, sourceValue] = job.source.split(" · ");

  return (
    <Card
      className={cn(
        "gap-0 p-[18px] transition-[box-shadow] duration-200",
        active &&
          "shadow-[0_0_0_1px_color-mix(in_oklch,var(--primary)_14%,transparent)] ring-[color-mix(in_oklch,var(--primary)_30%,var(--border))]",
      )}
    >
      <div className="flex flex-col gap-[14px]">
        {/* top row: icon + title + status + menu */}
        <div className="flex items-start gap-[13px]">
          <div
            className={cn(
              "flex size-10 shrink-0 items-center justify-center rounded-[9px]",
              active ? "bg-primary/15 text-primary" : "bg-muted text-muted-foreground",
            )}
          >
            <FileVideo className="size-[19px]" />
          </div>
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-[9px]">
              <span className="text-[14.5px] font-[580] tracking-[-0.01em]">{job.title}</span>
              <StatusBadge tone={st.tone} dot pulse={active}>
                {st.label}
              </StatusBadge>
              {job.status === "done" && (
                <StatusBadge tone="neutral">
                  <Lock className="size-3" />
                  Private
                </StatusBadge>
              )}
            </div>
            <div className="mt-[5px] flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
              <span className="mono">{job.id}</span>
              <span>·</span>
              <span>{(job.sizeMB / 1024).toFixed(2)} GB</span>
              <span>·</span>
              <span>by {job.by}</span>
            </div>
          </div>
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="size-[30px] shrink-0">
                <MoreHorizontal className="size-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end" className="w-44">
              <DropdownMenuItem>
                <ExternalLink className="size-4" />
                Open source file
              </DropdownMenuItem>
              <DropdownMenuItem>
                <ScrollText className="size-4" />
                View job log
              </DropdownMenuItem>
              {active && (
                <DropdownMenuItem variant="destructive" onSelect={() => onCancel(job.id)}>
                  <Ban className="size-4" />
                  Cancel job
                </DropdownMenuItem>
              )}
              {job.status === "failed" && (
                <DropdownMenuItem onSelect={() => onRetry(job.id)}>
                  <RefreshCw className="size-4" />
                  Retry
                </DropdownMenuItem>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        </div>

        {/* route: source -> target account -> slack channel */}
        <div className="flex flex-wrap items-center gap-2.5 rounded-[9px] bg-muted px-3 py-2.5 text-[12.5px]">
          <RouteChip icon={Download} label={sourceLabel} value={sourceValue ?? job.source} />
          <ChevronRight className="size-[15px] shrink-0 text-muted-foreground" />
          <RouteChip icon={YoutubeIcon} label={job.target} value={job.account} mono />
          <ChevronRight className="size-[15px] shrink-0 text-muted-foreground" />
          <RouteChip icon={SlackIcon} label="Reporting to" value={job.channel} mono />
        </div>

        {/* per-status footer */}
        {job.status === "failed" ? (
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex items-center gap-2 text-[12.5px] text-danger">
              <CircleAlert className="size-[15px] shrink-0" />
              <span>{job.error}</span>
            </div>
            <div className="flex gap-2">
              <Button variant="outline" size="sm" onClick={() => onDismiss(job.id)}>
                Dismiss
              </Button>
              <Button size="sm" onClick={() => onRetry(job.id)}>
                <RefreshCw className="size-3.5" />
                Retry
              </Button>
            </div>
          </div>
        ) : job.status === "done" ? (
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div className="flex flex-wrap items-center gap-2 text-[12.5px] text-muted-foreground">
              <CircleCheck className="size-[15px] shrink-0 text-ok" />
              <span>Uploaded as private ·</span>
              <a
                href="#"
                className="mono text-primary no-underline"
                onClick={(e) => e.preventDefault()}
              >
                {job.videoUrl}
              </a>
              <span>· finished in {fmtTime(job.elapsed)}</span>
            </div>
            <Button variant="outline" size="sm">
              Open on YouTube
              <ExternalLink className="size-3.5" />
            </Button>
          </div>
        ) : job.status === "queued" || job.status === "canceled" ? (
          <div className="flex items-center justify-between gap-3">
            <div className="flex items-center gap-[7px] text-[12.5px] text-muted-foreground">
              <Clock className="size-[15px]" />
              {job.status === "queued" ? "Waiting for an available worker…" : "Job canceled"}
            </div>
            {job.status === "queued" && (
              <Button variant="ghost" size="sm" onClick={() => onCancel(job.id)}>
                Cancel
              </Button>
            )}
          </div>
        ) : (
          <div>
            <div className="mb-2 flex items-center justify-between">
              <div className="flex items-center gap-[7px] text-[12.5px] font-medium">
                <StatusIcon className="size-3.5 text-primary" />
                <span>
                  {job.status === "downloading" ? "Downloading from Drive" : "Uploading to YouTube"}
                </span>
                <span className="mono font-normal text-muted-foreground">
                  · {fmtTime(job.elapsed)} elapsed
                </span>
              </div>
              <div className="flex items-center gap-3">
                <span className="mono text-[13px] font-semibold">{Math.round(job.progress)}%</span>
                <Button
                  variant="ghost"
                  size="sm"
                  className="text-muted-foreground"
                  onClick={() => onCancel(job.id)}
                >
                  <X className="size-3.5" />
                  Cancel
                </Button>
              </div>
            </div>
            <ProgressBar value={job.progress} animated height={8} />
            <div className="mt-[7px] flex justify-between text-[11.5px] text-muted-foreground">
              <span className="mono">
                {formatNumber(Math.round((job.progress / 100) * job.sizeMB))} /{" "}
                {formatNumber(job.sizeMB)} MB
              </span>
              <span>{job.eta != null ? `~${fmtTime(job.eta)} remaining` : "estimating…"}</span>
            </div>
          </div>
        )}
      </div>
    </Card>
  );
}

function RouteChip({
  icon: Icon,
  label,
  value,
  mono = false,
}: {
  icon: ComponentType<{ className?: string }>;
  label: string;
  value: string;
  mono?: boolean;
}) {
  return (
    <div className="flex min-w-0 items-center gap-[7px]">
      <Icon className="size-[15px] shrink-0 text-muted-foreground" />
      <div className="min-w-0 leading-[1.25]">
        <div className="text-[10.5px] uppercase tracking-[0.03em] text-muted-foreground">
          {label}
        </div>
        <div className={cn("truncate text-[12.5px] font-medium", mono && "mono")}>{value}</div>
      </div>
    </div>
  );
}
