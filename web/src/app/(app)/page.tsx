"use client";

import {
  ChevronRight,
  CloudUpload,
  MessageSquare,
  RefreshCw,
  ScrollText,
} from "lucide-react";
import { useRouter } from "next/navigation";
import { type ReactNode, useState } from "react";

import { PageHeading } from "@/components/page-heading";
import { StatusBadge, StatusDot } from "@/components/status";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import {
  type CommentsChannelStat,
  type CommentsOverview,
  type UploadsOverview,
  useOverview,
} from "@/features/overview/hooks";
import { ROUTE_PATH, type RouteId } from "@/lib/nav";
import { cn } from "@/lib/utils";

const numberFmt = new Intl.NumberFormat("en-US");
const fmt = (n: number) => numberFmt.format(n);

// ── Time-range selector shared by both panels ───────────────────────────────────────────────────────

const PERIODS = [
  { key: "24h", short: "24h", long: "last 24 hours" },
  { key: "7d", short: "7d", long: "last 7 days" },
  { key: "30d", short: "30d", long: "last 30 days" },
] as const;

type PeriodKey = (typeof PERIODS)[number]["key"];

const periodLong = (p: PeriodKey) => PERIODS.find((x) => x.key === p)!.long;

export default function OverviewPage() {
  const router = useRouter();
  const { data, isLoading, isFetching, refetch } = useOverview();
  const [period, setPeriod] = useState<PeriodKey>("7d");

  const go = (id: RouteId) => router.push(ROUTE_PATH[id] ?? "/");

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Overview"
        description="Live totals across both tools — comments forwarded and removed, uploads by outcome, and today's quota."
        actions={
          <>
            <PeriodToggle value={period} onChange={setPeriod} />
            <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching}>
              <RefreshCw className={cn("size-3.5", isFetching && "animate-spin")} />
              Refresh
            </Button>
            <Button size="sm" onClick={() => go("logs")}>
              <ScrollText className="size-3.5" />
              Open logs
            </Button>
          </>
        }
      />

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-2">
        <CommentsPanel
          data={data?.comments}
          loading={isLoading}
          period={period}
          onMore={() => go("ytc-mappings")}
        />
        <UploadsPanel
          data={data?.uploads}
          loading={isLoading}
          period={period}
          onMore={() => go("ytu-history")}
        />
      </div>
    </div>
  );
}

function PeriodToggle({
  value,
  onChange,
}: {
  value: PeriodKey;
  onChange: (v: PeriodKey) => void;
}) {
  return (
    <ToggleGroup
      type="single"
      value={value}
      onValueChange={(v) => v && onChange(v as PeriodKey)}
      variant="outline"
      size="sm"
      spacing={0}
      aria-label="Time range"
      className="bg-background"
    >
      {PERIODS.map((p) => (
        <ToggleGroupItem
          key={p.key}
          value={p.key}
          aria-label={p.long}
          className="px-3 text-[12px] text-muted-foreground data-[state=on]:text-foreground"
        >
          {p.short}
        </ToggleGroupItem>
      ))}
    </ToggleGroup>
  );
}

// ── Comments panel ────────────────────────────────────────────────────────────────────────────────

function CommentsPanel({
  data,
  loading,
  period,
  onMore,
}: {
  data?: CommentsOverview;
  loading: boolean;
  period: PeriodKey;
  onMore: () => void;
}) {
  const win = data
    ? period === "24h"
      ? data.window24h
      : period === "7d"
        ? data.window7d
        : data.window30d
    : undefined;

  const channels = data ? topChannels(data.perChannel, period) : [];

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <div className="flex items-center gap-2.5">
          <MessageSquare className="size-4 text-info" />
          <div>
            <CardTitle>YouTube Comments</CardTitle>
            <CardDescription>Forwarded to Slack · removed on YouTube</CardDescription>
          </div>
        </div>
        <Button variant="ghost" size="sm" onClick={onMore}>
          Mappings
          <ChevronRight className="size-3.5" />
        </Button>
      </CardHeader>
      <CardContent className="space-y-5">
        {loading || !data || !win ? (
          <PanelSkeleton tiles={2} />
        ) : (
          <>
            <div className="grid grid-cols-2 gap-3">
              <Stat label="Forwarded · all time" value={fmt(data.totalForwarded)} />
              <Stat
                label="Quota · today"
                value={`${data.quota.percent}%`}
                tone={data.quota.percent >= 90 ? "danger" : data.quota.percent >= 70 ? "warn" : undefined}
                sub={`${fmt(data.quota.used)} / ${fmt(data.quota.ceiling)} units`}
              />
            </div>

            <ActivitySection period={period} cols={2}>
              <MetricTile label="Forwarded" value={win.forwarded} tone="ok" />
              <MetricTile label="Removed" value={win.removed} tone="danger" />
            </ActivitySection>

            <div>
              <SectionLabel>Top channels · {periodLong(period)}</SectionLabel>
              {channels.length === 0 ? (
                <EmptyRow>No forwarded comments in the {periodLong(period)}.</EmptyRow>
              ) : (
                <div className="mt-1.5">
                  {channels.map((c, i) => (
                    <div
                      key={c.channelTitle}
                      className={cn("flex items-center justify-between py-2", i > 0 && "border-t")}
                    >
                      <span className="min-w-0 truncate text-[13px] font-medium">{c.channelTitle}</span>
                      <div className="flex shrink-0 items-center gap-2">
                        <span className="mono text-[12.5px]">{fmt(c.forwarded)}</span>
                        {c.removed > 0 && (
                          <StatusBadge tone="neutral">{fmt(c.removed)} removed</StatusBadge>
                        )}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

/** Channels ranked by forwarded count within the selected window (rows with no activity dropped). */
function topChannels(stats: CommentsChannelStat[], period: PeriodKey) {
  const pick = (c: CommentsChannelStat) =>
    period === "24h"
      ? { forwarded: c.forwarded24h, removed: c.removed24h }
      : period === "7d"
        ? { forwarded: c.forwarded7d, removed: c.removed7d }
        : { forwarded: c.forwarded30d, removed: c.removed30d };

  return stats
    .map((c) => ({ channelTitle: c.channelTitle, ...pick(c) }))
    .filter((c) => c.forwarded > 0 || c.removed > 0)
    .sort((a, b) => b.forwarded - a.forwarded)
    .slice(0, 5);
}

// ── Uploads panel ─────────────────────────────────────────────────────────────────────────────────

function UploadsPanel({
  data,
  loading,
  period,
  onMore,
}: {
  data?: UploadsOverview;
  loading: boolean;
  period: PeriodKey;
  onMore: () => void;
}) {
  const win = data
    ? period === "24h"
      ? data.window24h
      : period === "7d"
        ? data.window7d
        : data.window30d
    : undefined;

  return (
    <Card>
      <CardHeader className="flex flex-row items-center justify-between">
        <div className="flex items-center gap-2.5">
          <CloudUpload className="size-4 text-info" />
          <div>
            <CardTitle>YouTube Uploads</CardTitle>
            <CardDescription>Uploads by outcome · today&apos;s upload bucket</CardDescription>
          </div>
        </div>
        <Button variant="ghost" size="sm" onClick={onMore}>
          History
          <ChevronRight className="size-3.5" />
        </Button>
      </CardHeader>
      <CardContent className="space-y-5">
        {loading || !data || !win ? (
          <PanelSkeleton tiles={3} />
        ) : (
          <>
            <div className="grid grid-cols-2 gap-3">
              <Stat label="Uploaded · all time" value={fmt(data.totalUploads)} />
              <Stat
                label="videos.insert · today"
                value={`${fmt(data.bucket.used)} / ${fmt(data.bucket.limit)}`}
                tone={
                  data.bucket.limit > 0 && data.bucket.used / data.bucket.limit >= 0.9
                    ? "danger"
                    : data.bucket.limit > 0 && data.bucket.used / data.bucket.limit >= 0.7
                      ? "warn"
                      : undefined
                }
                sub="upload calls"
              />
            </div>

            <ActivitySection period={period} cols={3}>
              <MetricTile label="Done" value={win.done} tone="ok" />
              <MetricTile label="Failed" value={win.failed} tone="danger" />
              <MetricTile label="Canceled" value={win.canceled} tone="neutral" />
            </ActivitySection>

            <div>
              <SectionLabel>Top accounts · all time</SectionLabel>
              {data.perAccount.length === 0 ? (
                <EmptyRow>No uploads yet.</EmptyRow>
              ) : (
                <div className="mt-1.5">
                  {data.perAccount.slice(0, 5).map((a, i) => (
                    <div
                      key={a.accountTitle}
                      className={cn("flex items-center justify-between py-2", i > 0 && "border-t")}
                    >
                      <span className="min-w-0 truncate text-[13px] font-medium">{a.accountTitle}</span>
                      <div className="flex shrink-0 items-center gap-2">
                        <span className="mono text-[12.5px]">{fmt(a.done)}</span>
                        {a.failed > 0 && <StatusBadge tone="danger">{fmt(a.failed)} failed</StatusBadge>}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

// ── Shared building blocks ──────────────────────────────────────────────────────────────────────────

function Stat({
  label,
  value,
  sub,
  tone,
}: {
  label: string;
  value: string;
  sub?: string;
  tone?: "warn" | "danger";
}) {
  const valueColor =
    tone === "danger" ? "text-danger" : tone === "warn" ? "text-warn" : "text-foreground";
  return (
    <div className="rounded-lg border bg-background p-3.5">
      <div className="text-[12px] font-medium text-muted-foreground">{label}</div>
      <div className={cn("mono mt-2 text-[26px] font-semibold leading-none tracking-[-0.02em]", valueColor)}>
        {value}
      </div>
      {sub && <div className="mt-2 text-[11.5px] text-muted-foreground">{sub}</div>}
    </div>
  );
}

/** Windowed activity block — header reflects the selected range, tiles laid out in `cols` columns. */
function ActivitySection({
  period,
  cols,
  children,
}: {
  period: PeriodKey;
  cols: 2 | 3;
  children: ReactNode;
}) {
  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <SectionLabel>Activity</SectionLabel>
        <span className="text-[11px] font-medium text-muted-foreground">{periodLong(period)}</span>
      </div>
      <div className={cn("grid gap-2.5", cols === 2 ? "grid-cols-2" : "grid-cols-3")}>{children}</div>
    </div>
  );
}

function MetricTile({
  label,
  value,
  tone = "neutral",
}: {
  label: string;
  value: number;
  tone?: "ok" | "danger" | "neutral";
}) {
  const valueColor =
    tone === "ok" ? "text-ok" : tone === "danger" ? "text-danger" : "text-foreground";
  return (
    <div className="rounded-lg border bg-background p-3">
      <div className="flex items-center gap-1.5">
        <StatusDot tone={tone} />
        <span className="text-[11.5px] font-medium text-muted-foreground">{label}</span>
      </div>
      <div className={cn("mono mt-1.5 text-[22px] font-semibold leading-none tracking-[-0.02em]", valueColor)}>
        {fmt(value)}
      </div>
    </div>
  );
}

function SectionLabel({ children }: { children: ReactNode }) {
  return (
    <div className="text-[11px] font-medium uppercase tracking-wide text-muted-foreground">{children}</div>
  );
}

function EmptyRow({ children }: { children: ReactNode }) {
  return <div className="py-3 text-[12.5px] text-muted-foreground">{children}</div>;
}

function PanelSkeleton({ tiles }: { tiles: 2 | 3 }) {
  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3">
        <Skeleton className="h-[88px] w-full rounded-lg" />
        <Skeleton className="h-[88px] w-full rounded-lg" />
      </div>
      <div className={cn("grid gap-2.5", tiles === 2 ? "grid-cols-2" : "grid-cols-3")}>
        {Array.from({ length: tiles }).map((_, i) => (
          <Skeleton key={i} className="h-[68px] w-full rounded-lg" />
        ))}
      </div>
      <div className="space-y-2.5">
        {[0, 1, 2].map((i) => (
          <Skeleton key={i} className="h-7 w-full" />
        ))}
      </div>
    </div>
  );
}
