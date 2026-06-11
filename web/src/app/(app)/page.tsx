"use client";

import {
  ChevronRight,
  CircleAlert,
  CircleCheck,
  CloudUpload,
  Key,
  MessageSquare,
  RefreshCw,
  ScrollText,
  TriangleAlert,
} from "lucide-react";
import { useRouter } from "next/navigation";

import { iconByName } from "@/components/icon";
import { PageHeading } from "@/components/page-heading";
import { StatusBadge } from "@/components/status";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import {
  type ActivityItem,
  type HealthItem,
  type Metric,
  type NeedsAttentionItem,
} from "@/lib/mock-data";
import { ROUTE_PATH, type RouteId } from "@/lib/nav";
import { cn } from "@/lib/utils";
import { useOverview } from "@/features/overview/hooks";

const ACTIVITY_ICON = { upload: CloudUpload, comment: MessageSquare, key: Key };

export default function OverviewPage() {
  const router = useRouter();
  const { data, isLoading, isFetching, refetch } = useOverview();

  const go = (id: RouteId | string) => router.push(ROUTE_PATH[id as RouteId] ?? "/");
  const attn = data?.needsAttention ?? [];
  const metrics = data?.metrics ?? [];
  const activity = data?.activity ?? [];
  const health = data?.health ?? [];

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Overview"
        description="What your automations are doing right now — and what needs a hand."
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

      {/* Needs attention */}
      <Card
        className={cn(
          attn.length && "ring-[color-mix(in_oklch,var(--warn)_32%,var(--border))]",
        )}
      >
        <CardHeader className="flex flex-row items-center justify-between">
          <div className="flex items-center gap-2.5">
            <TriangleAlert className={cn("size-4", attn.length ? "text-warn" : "text-ok")} />
            <CardTitle>Needs attention</CardTitle>
            {attn.length > 0 && <StatusBadge tone="warn">{attn.length}</StatusBadge>}
          </div>
          <Button variant="ghost" size="sm" onClick={() => go("logs")}>
            All events
            <ChevronRight className="size-3.5" />
          </Button>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <div className="space-y-2.5">
              {[0, 1, 2].map((i) => (
                <Skeleton key={i} className="h-10 w-full" />
              ))}
            </div>
          ) : attn.length === 0 ? (
            <div className="flex items-center gap-3 px-2 py-6 text-muted-foreground">
              <CircleCheck className="size-[18px] text-ok" />
              All clear — no failed jobs, healthy quota, every connection valid.
            </div>
          ) : (
            <div>
              {attn.map((item, i) => (
                <AttentionRow key={item.id} item={item} onAct={go} last={i === attn.length - 1} />
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      {/* Useful metrics */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {isLoading
          ? [0, 1, 2, 3].map((i) => (
              <Card key={i}>
                <CardContent>
                  <Skeleton className="mb-4 h-3 w-28" />
                  <Skeleton className="mb-3.5 h-7 w-16" />
                  <Skeleton className="h-3 w-full" />
                </CardContent>
              </Card>
            ))
          : metrics.map((m) => <MetricCard key={m.id} metric={m} />)}
      </div>

      {/* Recent activity + connections health */}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[3fr_2fr]">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <div>
              <CardTitle>Recent activity</CardTitle>
              <CardDescription>Latest events across both tools</CardDescription>
            </div>
            <Button variant="ghost" size="sm" onClick={() => go("logs")}>
              View logs
              <ChevronRight className="size-3.5" />
            </Button>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <div>
                {[0, 1, 2, 3].map((i) => (
                  <div key={i} className="flex gap-3 py-3">
                    <Skeleton className="size-[30px] rounded-lg" />
                    <div className="flex-1 space-y-1.5">
                      <Skeleton className="h-3.5 w-[70%]" />
                      <Skeleton className="h-2.5 w-[40%]" />
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div>
                {activity.map((a, i) => (
                  <div key={a.id} className={cn(i > 0 && "border-t")}>
                    <ActivityRow item={a} />
                  </div>
                ))}
              </div>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>Connections health</CardTitle>
            <CardDescription>Slack · Google · API keys</CardDescription>
          </CardHeader>
          <CardContent>
            {isLoading ? (
              <div className="space-y-3">
                {[0, 1, 2].map((i) => (
                  <Skeleton key={i} className="h-8 w-full" />
                ))}
              </div>
            ) : (
              <>
                <div>
                  {health.map((h, i) => (
                    <div key={h.id} className={cn(i > 0 && "border-t")}>
                      <HealthRow
                        item={h}
                        onClick={() =>
                          go(
                            h.id === "slack"
                              ? "conn-slack"
                              : h.id === "google"
                                ? "conn-google"
                                : "conn-keys",
                          )
                        }
                      />
                    </div>
                  ))}
                </div>
                <button
                  type="button"
                  onClick={() => go("conn-keys")}
                  className="mt-3 flex w-full items-start gap-2.5 rounded-lg bg-warn-bg p-3 text-left outline-none focus-visible:ring-2 focus-visible:ring-ring"
                >
                  <CircleAlert className="mt-px size-4 shrink-0 text-warn" />
                  <div className="text-[12.5px] text-[color-mix(in_oklch,var(--warn)_70%,var(--foreground))]">
                    <span className="font-semibold">prod-yt-02</span> is at 87% of daily quota.
                    Rotate keys before the next batch.
                  </div>
                </button>
              </>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function AttentionRow({
  item,
  onAct,
  last,
}: {
  item: NeedsAttentionItem;
  onAct: (id: string) => void;
  last: boolean;
}) {
  const Icon = iconByName(item.icon);
  const box =
    item.severity === "danger"
      ? "bg-danger-bg text-danger"
      : item.severity === "warn"
        ? "bg-warn-bg text-warn"
        : "bg-muted text-muted-foreground";
  return (
    <div className={cn("flex items-center gap-3 py-3", !last && "border-b")}>
      <div className={cn("flex size-[34px] shrink-0 items-center justify-center rounded-[9px]", box)}>
        <Icon className="size-[17px]" />
      </div>
      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-[13.5px] font-[540] tracking-[-0.01em]">{item.title}</span>
          <span className="text-[11px] text-muted-foreground">{item.tool}</span>
        </div>
        <div className="mt-0.5 text-[12.5px] text-muted-foreground">{item.detail}</div>
      </div>
      <Button
        variant={item.severity === "danger" ? "default" : "outline"}
        size="sm"
        className="shrink-0"
        onClick={() => onAct(item.action.route)}
      >
        {item.action.label}
      </Button>
    </div>
  );
}

function MetricCard({ metric }: { metric: Metric }) {
  const Icon = iconByName(metric.icon);
  const valueColor =
    metric.tone === "warn" ? "text-warn" : metric.tone === "danger" ? "text-danger" : "text-foreground";
  return (
    <Card>
      <CardContent>
        <div className="flex items-center justify-between">
          <div className="text-[12.5px] font-medium text-muted-foreground">{metric.label}</div>
          <div className="flex size-7 items-center justify-center rounded-lg bg-muted text-muted-foreground">
            <Icon className="size-[15px]" />
          </div>
        </div>
        <div className={cn("mono mt-3 text-[30px] font-semibold leading-none tracking-[-0.02em]", valueColor)}>
          {metric.value}
        </div>
        <div className="mt-2.5 text-[12.5px] leading-snug text-foreground">{metric.context}</div>
        <div className="mt-1.5 flex items-center gap-1.5 text-[11.5px] text-muted-foreground">
          <span className="size-[5px] rounded-full bg-muted-foreground/60" />
          {metric.foot}
        </div>
      </CardContent>
    </Card>
  );
}

function ActivityRow({ item }: { item: ActivityItem }) {
  const Icon = ACTIVITY_ICON[item.kind] ?? CircleAlert;
  return (
    <div className="flex gap-3 py-3">
      <div className="flex size-[30px] shrink-0 items-center justify-center rounded-lg bg-muted text-muted-foreground">
        <Icon className="size-[15px]" />
      </div>
      <div className="min-w-0 flex-1">
        <div className="text-[13.5px] leading-snug">{item.text}</div>
        <div className="mt-0.5 text-xs text-muted-foreground">{item.meta}</div>
      </div>
      <div className="flex shrink-0 flex-col items-end gap-1.5">
        <span className="mono whitespace-nowrap text-[11.5px] text-muted-foreground">{item.time}</span>
        {item.status === "failed" && (
          <StatusBadge tone="danger" dot>
            Failed
          </StatusBadge>
        )}
      </div>
    </div>
  );
}

function HealthRow({ item, onClick }: { item: HealthItem; onClick: () => void }) {
  const Icon = iconByName(item.icon);
  const tone = item.status === "ok" ? "ok" : item.status === "warn" ? "warn" : "danger";
  const label = item.status === "ok" ? "Healthy" : item.status === "warn" ? "Attention" : "Down";
  return (
    <button
      type="button"
      onClick={onClick}
      className="flex w-full items-center gap-3 py-3 text-left outline-none focus-visible:ring-2 focus-visible:ring-ring"
    >
      <div className="flex size-8 items-center justify-center rounded-lg border bg-background">
        <Icon className="size-4" />
      </div>
      <div className="flex-1">
        <div className="text-[13.5px] font-medium">{item.label}</div>
        <div className="text-xs text-muted-foreground">{item.detail}</div>
      </div>
      <StatusBadge tone={tone} dot>
        {label}
      </StatusBadge>
    </button>
  );
}
