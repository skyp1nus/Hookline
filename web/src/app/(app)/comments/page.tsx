"use client";

import { type ReactNode, useState } from "react";

import { AreaChart } from "@/components/charts";
import { PageHeading } from "@/components/page-heading";
import { ProgressBar } from "@/components/progress-bar";
import { StatusBadge, StatusDot } from "@/components/status";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { ToggleGroup, ToggleGroupItem } from "@/components/ui/toggle-group";
import {
  useChannels,
  useCommentsActivity,
  useCommentsEngagement,
  useCommentsHealth,
  useCommentsHistory,
  useCommentsModeration,
  useCommentStats,
} from "@/features/comments/hooks";
import {
  type ActivityPoint,
  type DashboardStats,
  type DeliveryHealth,
  type Engagement,
  type History,
  type MappingHealth,
  type ModerationStats,
  type ModeratorStat,
  type YouTubeChannelDto,
} from "@/features/comments/types";
import { formatDuration, formatNumber } from "@/lib/format";
import { cn } from "@/lib/utils";

type RangeKey = "24h" | "7d" | "30d";
type HistoryDays = 30 | 90 | 365;

const HISTORY_OPTIONS: { key: HistoryDays; label: string }[] = [
  { key: 30, label: "30d" },
  { key: 90, label: "90d" },
  { key: 365, label: "1y" },
];

const RANGES: { key: RangeKey; label: string; long: string }[] = [
  { key: "24h", label: "24h", long: "last 24 hours" },
  { key: "7d", label: "7d", long: "last 7 days" },
  { key: "30d", label: "30d", long: "last 30 days" },
];

interface DashStat {
  id: string;
  label: string;
  value: string;
  sub: string;
}

function initials(name: string) {
  return name
    .split(" ")
    .map((w) => w[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
}

/** "5 min ago" / "2 hr ago" / "3 d ago" from an ISO instant, or "never". */
function timeAgo(iso: string | null): string {
  if (!iso) return "never";
  const secs = Math.max(0, (Date.now() - new Date(iso).getTime()) / 1000);
  if (secs < 60) return "just now";
  if (secs < 3600) return `${Math.floor(secs / 60)} min ago`;
  if (secs < 86400) return `${Math.floor(secs / 3600)} hr ago`;
  return `${Math.floor(secs / 86400)} d ago`;
}

/** x-axis label for a bucket: local hour for 24h, "Mon D" for daily ranges. */
function bucketLabel(iso: string, range: RangeKey): string {
  const d = new Date(iso);
  if (range === "24h") return `${String(d.getHours()).padStart(2, "0")}:00`;
  return d.toLocaleDateString(undefined, { month: "short", day: "numeric" });
}

function toStatCards(s: DashboardStats): DashStat[] {
  return [
    { id: "mappings", label: "Active mappings", value: `${s.activeMappings}`, sub: `${s.totalMappings} total` },
    {
      id: "comments",
      label: "Comments · 24h",
      value: formatNumber(s.commentsLast24h),
      sub: `${formatNumber(s.commentsToday)} today (PT)`,
    },
    {
      id: "quota",
      label: "Quota · today",
      value: `≈ ${s.estimatedPercent}%`,
      sub: `est. ${formatNumber(s.estimatedDailyUnits)} / ${formatNumber(s.quotaCeiling)} units`,
    },
    {
      id: "errors",
      label: "Errors · 24h",
      value: `${s.errorsLast24h}`,
      sub: s.errorsLast24h > 0 ? "needs attention" : "all clear",
    },
  ];
}

export default function CommentsDashboardPage() {
  const { data: stats, isLoading: statsLoading } = useCommentStats();
  const { data: channelsData, isLoading: channelsLoading } = useChannels();
  const [range, setRange] = useState<RangeKey>("24h");
  const { data: activity, isLoading: activityLoading } = useCommentsActivity(range);
  const { data: moderation, isLoading: moderationLoading } = useCommentsModeration();
  const { data: health, isLoading: healthLoading } = useCommentsHealth();
  const { data: engagement, isLoading: engagementLoading } = useCommentsEngagement();
  const [historyDays, setHistoryDays] = useState<HistoryDays>(90);
  const { data: history, isLoading: historyLoading } = useCommentsHistory(historyDays);

  const statCards = stats ? toStatCards(stats) : [];
  const channels = [...(channelsData ?? [])].sort((a, b) => b.mappingCount - a.mappingCount);
  const maxMappings = Math.max(...channels.map((ch) => ch.mappingCount), 1);

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Dashboard"
        description="Live snapshot of YouTube → Slack comment forwarding."
      />

      {/* Stat cards */}
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {statsLoading
          ? [0, 1, 2, 3].map((i) => (
              <Card key={i}>
                <CardContent>
                  <Skeleton className="mb-3 h-3 w-28" />
                  <Skeleton className="mb-3 h-6 w-16" />
                  <Skeleton className="h-3 w-full" />
                </CardContent>
              </Card>
            ))
          : statCards.map((s) => <StatCard key={s.id} stat={s} />)}
      </div>

      {/* Activity timeline (range-toggled) */}
      <ActivityCard
        range={range}
        onRange={setRange}
        points={activity?.points ?? []}
        loading={activityLoading}
      />

      {/* Moderation breakdown + delivery/mapping health */}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <ModerationCard data={moderation} loading={moderationLoading} />
        <HealthCard
          delivery={health?.delivery}
          mappings={health?.mappings ?? []}
          loading={healthLoading}
        />
      </div>

      {/* Engagement analytics */}
      <EngagementCard data={engagement} loading={engagementLoading} />

      {/* Long-term history (durable nightly rollup) */}
      <HistoryCard data={history} loading={historyLoading} days={historyDays} onDays={setHistoryDays} />

      {/* Channels by mapping count */}
      <Card>
        <CardHeader>
          <CardTitle>Channels</CardTitle>
          <CardDescription>Tracked channels by mapping count</CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          {channelsLoading ? (
            [0, 1, 2].map((i) => <Skeleton key={i} className="h-8 w-full" />)
          ) : channels.length === 0 ? (
            <p className="py-4 text-center text-[13px] text-muted-foreground">No channels tracked yet.</p>
          ) : (
            channels.map((ch) => <ChannelBar key={ch.id} channel={ch} max={maxMappings} />)
          )}
        </CardContent>
      </Card>
    </div>
  );
}

// ── Activity timeline ───────────────────────────────────────────────────────────────────────────────

function ActivityCard({
  range,
  onRange,
  points,
  loading,
}: {
  range: RangeKey;
  onRange: (r: RangeKey) => void;
  points: ActivityPoint[];
  loading: boolean;
}) {
  const forwarded = points.map((p) => p.forwarded);
  const removed = points.map((p) => p.removed);
  const labels = points.map((p) => bucketLabel(p.bucket, range));
  const totalForwarded = forwarded.reduce((a, b) => a + b, 0);
  const totalRemoved = removed.reduce((a, b) => a + b, 0);
  const totalReplies = points.reduce((a, p) => a + p.replies, 0);
  const rangeLong = RANGES.find((r) => r.key === range)!.long;

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between">
        <div>
          <CardTitle>Activity</CardTitle>
          <CardDescription>Forwarded vs removed · {rangeLong}</CardDescription>
        </div>
        <ToggleGroup
          type="single"
          value={range}
          onValueChange={(v) => v && onRange(v as RangeKey)}
          variant="outline"
          size="sm"
          spacing={0}
          aria-label="Time range"
          className="bg-background"
        >
          {RANGES.map((r) => (
            <ToggleGroupItem
              key={r.key}
              value={r.key}
              aria-label={r.long}
              className="px-3 text-[12px] text-muted-foreground data-[state=on]:text-foreground"
            >
              {r.label}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {loading ? (
          <Skeleton className="h-[220px] w-full rounded-xl" />
        ) : (
          <>
            <div className="flex flex-wrap items-baseline gap-x-6 gap-y-1">
              <Legend tone="primary" label="Forwarded" value={totalForwarded} sub={`${formatNumber(totalReplies)} replies`} />
              <Legend tone="danger" label="Removed" value={totalRemoved} />
            </div>
            <AreaChart
              forwarded={forwarded.length ? forwarded : [0]}
              removed={removed.length ? removed : [0]}
              labels={labels.length ? labels : [""]}
            />
          </>
        )}
      </CardContent>
    </Card>
  );
}

function Legend({
  tone,
  label,
  value,
  sub,
}: {
  tone: "primary" | "danger";
  label: string;
  value: number;
  sub?: string;
}) {
  return (
    <div className="flex items-baseline gap-2">
      <span
        className={cn("size-2.5 self-center rounded-full", tone === "primary" ? "bg-primary" : "bg-danger")}
      />
      <span className="mono text-[22px] font-semibold leading-none tracking-[-0.02em]">{formatNumber(value)}</span>
      <span className="text-[12px] text-muted-foreground">{label}</span>
      {sub && <span className="text-[11.5px] text-muted-foreground">· {sub}</span>}
    </div>
  );
}

// ── Moderation breakdown ────────────────────────────────────────────────────────────────────────────

function ModerationCard({
  data,
  loading,
}: {
  data?: ModerationStats;
  loading: boolean;
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Moderation</CardTitle>
        <CardDescription>Removed on YouTube · by outcome &amp; moderator</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        {loading || !data ? (
          <>
            <div className="grid grid-cols-3 gap-2.5">
              {[0, 1, 2].map((i) => (
                <Skeleton key={i} className="h-[68px] w-full rounded-lg" />
              ))}
            </div>
            <Skeleton className="h-24 w-full" />
          </>
        ) : data.totalRemoved === 0 ? (
          <p className="py-6 text-center text-[13px] text-muted-foreground">No comments removed yet.</p>
        ) : (
          <>
            <div className="grid grid-cols-3 gap-2.5">
              <MetricTile label="Removed · all time" value={data.totalRemoved} tone="neutral" />
              <MetricTile label="Rejected" value={data.rejected} tone="danger" />
              <MetricTile label="Already gone" value={data.alreadyGone} tone="neutral" />
            </div>

            <div>
              <SectionLabel>Top moderators · all time</SectionLabel>
              {data.perModerator.length === 0 ? (
                <p className="py-3 text-[12.5px] text-muted-foreground">No moderators recorded.</p>
              ) : (
                <div className="mt-1.5">
                  {data.perModerator.slice(0, 6).map((m, i) => (
                    <ModeratorRow key={m.slackUserId ?? m.name} mod={m} top={i > 0} max={data.perModerator[0].removedAllTime} />
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

function ModeratorRow({ mod, top, max }: { mod: ModeratorStat; top: boolean; max: number }) {
  return (
    <div className={cn("py-2", top && "border-t")}>
      <div className="mb-[7px] flex items-center gap-2.5">
        <Avatar size="sm" className="shrink-0">
          <AvatarFallback className="bg-danger/15 text-danger text-[10px]">{initials(mod.name)}</AvatarFallback>
        </Avatar>
        <div className="min-w-0 flex-1 truncate text-[13px] font-[540]">{mod.name}</div>
        <span className="mono text-[13px] font-semibold">{formatNumber(mod.removedAllTime)}</span>
        {mod.removed7d > 0 && <StatusBadge tone="neutral">{formatNumber(mod.removed7d)} · 7d</StatusBadge>}
      </div>
      <ProgressBar value={(mod.removedAllTime / Math.max(max, 1)) * 100} tone="danger" height={6} />
    </div>
  );
}

// ── Delivery / mapping health ─────────────────────────────────────────────────────────────────────────

function HealthCard({
  delivery,
  mappings,
  loading,
}: {
  delivery?: DeliveryHealth;
  mappings: MappingHealth[];
  loading: boolean;
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Health</CardTitle>
        <CardDescription>Delivery queue &amp; per-mapping freshness</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        {loading || !delivery ? (
          <>
            <div className="grid grid-cols-3 gap-2.5">
              {[0, 1, 2].map((i) => (
                <Skeleton key={i} className="h-[68px] w-full rounded-lg" />
              ))}
            </div>
            <Skeleton className="h-24 w-full" />
          </>
        ) : (
          <>
            <div className="grid grid-cols-3 gap-2.5">
              <MetricTile label="Pending" value={delivery.pending} tone={delivery.pending > 0 ? "neutral" : "ok"} />
              <MetricTile label="Retrying" value={delivery.failing} tone={delivery.failing > 0 ? "warn" : "ok"} />
              <MetricTile label="Near give-up" value={delivery.nearGiveUp} tone={delivery.nearGiveUp > 0 ? "danger" : "ok"} />
            </div>
            {delivery.oldestPendingAt && (
              <p className="-mt-2 text-[11.5px] text-muted-foreground">
                Oldest queued item · {timeAgo(delivery.oldestPendingAt)}
              </p>
            )}

            <div>
              <SectionLabel>Mappings</SectionLabel>
              {mappings.length === 0 ? (
                <p className="py-3 text-[12.5px] text-muted-foreground">No mappings configured.</p>
              ) : (
                <div className="mt-1.5">
                  {mappings.slice(0, 8).map((m, i) => (
                    <MappingRow key={m.mappingId} m={m} top={i > 0} />
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

function MappingRow({ m, top }: { m: MappingHealth; top: boolean }) {
  const tone = !m.isActive ? "neutral" : m.lastError ? "danger" : "ok";
  return (
    <div className={cn("flex items-center gap-2.5 py-2", top && "border-t")}>
      <StatusDot tone={tone} />
      <div className="min-w-0 flex-1">
        <div className="truncate text-[13px] font-[540]">{m.channelTitle}</div>
        <div className="truncate text-[11.5px] text-muted-foreground">
          {m.lastError ? m.lastError : `#${m.slackChannelName} · polled ${timeAgo(m.lastPolledAt)}`}
        </div>
      </div>
      <div className="flex shrink-0 items-center gap-2">
        <span className="mono text-[12.5px]" title="forwarded in last 24h">
          {formatNumber(m.forwarded24h)}
        </span>
        {!m.isActive && <StatusBadge tone="neutral">paused</StatusBadge>}
      </div>
    </div>
  );
}

// ── Engagement analytics ──────────────────────────────────────────────────────────────────────────────

function EngagementCard({ data, loading }: { data?: Engagement; loading: boolean }) {
  const latency = data?.summary.avgForwardLatencySeconds;
  return (
    <Card>
      <CardHeader>
        <CardTitle>Engagement</CardTitle>
        <CardDescription>
          {data && data.summary.captured > 0
            ? `Based on ${formatNumber(data.summary.captured)} enriched comments`
            : "Likes, authors & latency on forwarded comments"}
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        {loading || !data ? (
          <>
            <div className="grid grid-cols-2 gap-2.5 sm:grid-cols-4">
              {[0, 1, 2, 3].map((i) => (
                <Skeleton key={i} className="h-[68px] w-full rounded-lg" />
              ))}
            </div>
            <Skeleton className="h-28 w-full" />
          </>
        ) : data.summary.captured === 0 ? (
          <p className="py-6 text-center text-[13px] text-muted-foreground">
            No engagement data captured yet — it accrues on comments forwarded from now on.
          </p>
        ) : (
          <>
            <div className="grid grid-cols-2 gap-2.5 sm:grid-cols-4">
              <SummaryTile label="Avg likes" value={formatNumber(data.summary.avgLikes)} />
              <SummaryTile label="Avg length" value={`${formatNumber(data.summary.avgCommentLength)} ch`} />
              <SummaryTile
                label="Avg forward lag"
                value={latency == null ? "—" : formatDuration(Math.round(latency))}
              />
              <SummaryTile label="Enriched" value={formatNumber(data.summary.captured)} />
            </div>

            <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
              <RankList
                title="Busiest videos"
                empty="No videos yet."
                rows={data.perVideo.map((v) => ({
                  key: v.videoId,
                  label: v.videoTitle ?? v.videoId,
                  value: v.forwarded,
                  badge: v.likes > 0 ? `${formatNumber(v.likes)} likes` : undefined,
                }))}
              />
              <RankList
                title="Top authors"
                empty="No authors captured yet."
                rows={data.topAuthors.map((a) => ({
                  key: a.authorChannelUrl ?? a.name,
                  label: a.name,
                  value: a.forwarded,
                  badge: a.likes > 0 ? `${formatNumber(a.likes)} likes` : undefined,
                }))}
              />
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

function SummaryTile({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border bg-background p-3">
      <div className="text-[11px] font-medium text-muted-foreground">{label}</div>
      <div className="mono mt-1.5 text-[22px] font-semibold leading-none tracking-[-0.02em]">{value}</div>
    </div>
  );
}

function RankList({
  title,
  empty,
  rows,
}: {
  title: string;
  empty: string;
  rows: { key: string; label: string; value: number; badge?: string }[];
}) {
  const max = Math.max(...rows.map((r) => r.value), 1);
  return (
    <div>
      <SectionLabel>{title}</SectionLabel>
      {rows.length === 0 ? (
        <p className="py-3 text-[12.5px] text-muted-foreground">{empty}</p>
      ) : (
        <div className="mt-1.5 flex flex-col gap-2.5">
          {rows.map((r) => (
            <div key={r.key}>
              <div className="mb-[6px] flex items-center gap-2.5">
                <div className="min-w-0 flex-1 truncate text-[13px] font-[540]">{r.label}</div>
                {r.badge && <StatusBadge tone="neutral">{r.badge}</StatusBadge>}
                <span className="mono text-[12.5px] font-semibold">{formatNumber(r.value)}</span>
              </div>
              <ProgressBar value={(r.value / max) * 100} tone="primary" height={6} />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

// ── Long-term history (durable rollup) ──────────────────────────────────────────────────────────────

function HistoryCard({
  data,
  loading,
  days,
  onDays,
}: {
  data?: History;
  loading: boolean;
  days: HistoryDays;
  onDays: (d: HistoryDays) => void;
}) {
  const points = data?.points ?? [];
  const forwarded = points.map((p) => p.forwarded);
  const removed = points.map((p) => p.removed);
  const labels = points.map((p) =>
    new Date(p.date).toLocaleDateString(undefined, { month: "short", day: "numeric" }),
  );
  const totalForwarded = forwarded.reduce((a, b) => a + b, 0);
  const totalRemoved = removed.reduce((a, b) => a + b, 0);

  return (
    <Card>
      <CardHeader className="flex flex-row items-start justify-between">
        <div>
          <CardTitle>History</CardTitle>
          <CardDescription>Daily totals · archived nightly (survives retention)</CardDescription>
        </div>
        <ToggleGroup
          type="single"
          value={String(days)}
          onValueChange={(v) => v && onDays(Number(v) as HistoryDays)}
          variant="outline"
          size="sm"
          spacing={0}
          aria-label="History span"
          className="bg-background"
        >
          {HISTORY_OPTIONS.map((o) => (
            <ToggleGroupItem
              key={o.key}
              value={String(o.key)}
              className="px-3 text-[12px] text-muted-foreground data-[state=on]:text-foreground"
            >
              {o.label}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        {loading ? (
          <Skeleton className="h-[220px] w-full rounded-xl" />
        ) : totalForwarded === 0 && totalRemoved === 0 ? (
          <p className="py-10 text-center text-[13px] text-muted-foreground">
            No archived history yet — the nightly rollup fills this in from tomorrow.
          </p>
        ) : (
          <>
            <div className="flex flex-wrap items-baseline gap-x-6 gap-y-1">
              <Legend tone="primary" label="Forwarded" value={totalForwarded} />
              <Legend tone="danger" label="Removed" value={totalRemoved} />
            </div>
            <AreaChart
              forwarded={forwarded.length ? forwarded : [0]}
              removed={removed.length ? removed : [0]}
              labels={labels.length ? labels : [""]}
            />
          </>
        )}
      </CardContent>
    </Card>
  );
}

// ── Shared building blocks ──────────────────────────────────────────────────────────────────────────

function StatCard({ stat }: { stat: DashStat }) {
  return (
    <Card>
      <CardContent>
        <div className="text-[12.5px] font-medium text-muted-foreground">{stat.label}</div>
        <div className="mt-2.5 flex items-baseline gap-2">
          <div className="mono text-[28px] font-semibold leading-none tracking-[-0.02em]">{stat.value}</div>
        </div>
        <div className="mt-3 text-[11.5px] text-muted-foreground">{stat.sub}</div>
      </CardContent>
    </Card>
  );
}

function MetricTile({
  label,
  value,
  tone = "neutral",
}: {
  label: string;
  value: number;
  tone?: "ok" | "warn" | "danger" | "neutral";
}) {
  const valueColor =
    tone === "ok"
      ? "text-ok"
      : tone === "warn"
        ? "text-warn"
        : tone === "danger"
          ? "text-danger"
          : "text-foreground";
  return (
    <div className="rounded-lg border bg-background p-3">
      <div className="flex items-center gap-1.5">
        <StatusDot tone={tone} />
        <span className="text-[11px] font-medium text-muted-foreground">{label}</span>
      </div>
      <div className={cn("mono mt-1.5 text-[22px] font-semibold leading-none tracking-[-0.02em]", valueColor)}>
        {formatNumber(value)}
      </div>
    </div>
  );
}

function SectionLabel({ children }: { children: ReactNode }) {
  return (
    <div className="text-[11px] font-medium uppercase tracking-wide text-muted-foreground">{children}</div>
  );
}

function ChannelBar({ channel, max }: { channel: YouTubeChannelDto; max: number }) {
  return (
    <div>
      <div className="mb-[7px] flex items-center gap-2.5">
        <Avatar size="sm" className="shrink-0">
          <AvatarFallback className="bg-primary/15 text-primary text-[10px]">
            {initials(channel.title)}
          </AvatarFallback>
        </Avatar>
        <div className="min-w-0 flex-1 truncate text-[13px] font-[540]">{channel.title}</div>
        <span
          className={cn(
            "mono text-[13px] font-semibold",
            channel.mappingCount ? "text-foreground" : "text-muted-foreground",
          )}
        >
          {formatNumber(channel.mappingCount)}
        </span>
      </div>
      <ProgressBar
        value={(channel.mappingCount / max) * 100}
        tone={channel.mappingCount ? "primary" : "ok"}
        height={6}
      />
    </div>
  );
}
