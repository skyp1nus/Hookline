"use client";

import {
  ChevronRight,
  CloudUpload,
  MessageSquare,
  RefreshCw,
  ScrollText,
} from "lucide-react";
import { useRouter } from "next/navigation";
import type { ReactNode } from "react";

import { PageHeading } from "@/components/page-heading";
import { StatusBadge } from "@/components/status";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import {
  type CommentsOverview,
  type UploadsOverview,
  useOverview,
} from "@/features/overview/hooks";
import { ROUTE_PATH, type RouteId } from "@/lib/nav";
import { cn } from "@/lib/utils";

const numberFmt = new Intl.NumberFormat("en-US");
const fmt = (n: number) => numberFmt.format(n);

export default function OverviewPage() {
  const router = useRouter();
  const { data, isLoading, isFetching, refetch } = useOverview();

  const go = (id: RouteId) => router.push(ROUTE_PATH[id] ?? "/");

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Overview"
        description="Live totals across both tools — comments forwarded and removed, uploads by outcome, and today's quota."
        actions={
          <>
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
        <CommentsPanel data={data?.comments} loading={isLoading} onMore={() => go("ytc-mappings")} />
        <UploadsPanel data={data?.uploads} loading={isLoading} onMore={() => go("ytu-history")} />
      </div>
    </div>
  );
}

// ── Comments panel ────────────────────────────────────────────────────────────────────────────────

function CommentsPanel({
  data,
  loading,
  onMore,
}: {
  data?: CommentsOverview;
  loading: boolean;
  onMore: () => void;
}) {
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
        {loading || !data ? (
          <PanelSkeleton />
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

            <WindowGrid
              rows={[
                { label: "Forwarded", v24: data.window24h.forwarded, v7: data.window7d.forwarded, v30: data.window30d.forwarded },
                { label: "Removed", v24: data.window24h.removed, v7: data.window7d.removed, v30: data.window30d.removed },
              ]}
            />

            <div>
              <SectionLabel>Top channels</SectionLabel>
              {data.perChannel.length === 0 ? (
                <EmptyRow>No forwarded comments yet.</EmptyRow>
              ) : (
                <div className="mt-1.5">
                  {data.perChannel.slice(0, 5).map((c, i) => (
                    <div
                      key={c.channelTitle}
                      className={cn("flex items-center justify-between py-2", i > 0 && "border-t")}
                    >
                      <span className="min-w-0 truncate text-[13px] font-medium">{c.channelTitle}</span>
                      <div className="flex shrink-0 items-center gap-2">
                        <span className="mono text-[12.5px]">{fmt(c.forwarded)}</span>
                        {c.removed30d > 0 && (
                          <StatusBadge tone="neutral">{fmt(c.removed30d)} removed · 30d</StatusBadge>
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

// ── Uploads panel ─────────────────────────────────────────────────────────────────────────────────

function UploadsPanel({
  data,
  loading,
  onMore,
}: {
  data?: UploadsOverview;
  loading: boolean;
  onMore: () => void;
}) {
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
        {loading || !data ? (
          <PanelSkeleton />
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

            <WindowGrid
              rows={[
                { label: "Done", v24: data.window24h.done, v7: data.window7d.done, v30: data.window30d.done },
                { label: "Failed", v24: data.window24h.failed, v7: data.window7d.failed, v30: data.window30d.failed },
                { label: "Canceled", v24: data.window24h.canceled, v7: data.window7d.canceled, v30: data.window30d.canceled },
              ]}
            />

            <div>
              <SectionLabel>Top accounts</SectionLabel>
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

interface WindowRow {
  label: string;
  v24: number;
  v7: number;
  v30: number;
}

function WindowGrid({ rows }: { rows: WindowRow[] }) {
  return (
    <div>
      <div className="grid grid-cols-[1fr_auto_auto_auto] items-center gap-x-5 border-b pb-1.5 text-[11px] font-medium uppercase tracking-wide text-muted-foreground">
        <span />
        <span className="text-right">24h</span>
        <span className="text-right">7d</span>
        <span className="text-right">30d</span>
      </div>
      {rows.map((r) => (
        <div
          key={r.label}
          className="grid grid-cols-[1fr_auto_auto_auto] items-center gap-x-5 border-b py-2 last:border-b-0"
        >
          <span className="text-[13px] font-medium">{r.label}</span>
          <span className="mono text-right text-[12.5px] tabular-nums">{fmt(r.v24)}</span>
          <span className="mono text-right text-[12.5px] tabular-nums">{fmt(r.v7)}</span>
          <span className="mono text-right text-[12.5px] tabular-nums">{fmt(r.v30)}</span>
        </div>
      ))}
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

function PanelSkeleton() {
  return (
    <div className="space-y-5">
      <div className="grid grid-cols-2 gap-3">
        <Skeleton className="h-[88px] w-full rounded-lg" />
        <Skeleton className="h-[88px] w-full rounded-lg" />
      </div>
      <div className="space-y-2.5">
        {[0, 1, 2].map((i) => (
          <Skeleton key={i} className="h-6 w-full" />
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
