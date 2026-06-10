"use client";

import { ChevronRight, MessageSquare } from "lucide-react";
import { useRouter } from "next/navigation";

import { Sparkline } from "@/components/charts";
import { PageHeading } from "@/components/page-heading";
import { ProgressBar } from "@/components/progress-bar";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { formatNumber } from "@/lib/format";
import { ROUTE_PATH } from "@/lib/nav";
import { cn } from "@/lib/utils";
import { useChannels, useCommentStats, useCommentsTimeline } from "@/features/comments/hooks";
import { type DashboardStats, type YouTubeChannelDto } from "@/features/comments/types";

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
      label: "Quota used · today",
      value: `${s.quotaUsedPercent}%`,
      sub: `${formatNumber(s.totalQuotaUsedToday)} / ${formatNumber(s.totalQuotaLimit)} units`,
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
  const router = useRouter();
  const { data: stats, isLoading: statsLoading } = useCommentStats();
  const { data: channelsData, isLoading: channelsLoading } = useChannels();
  const { data: timeline, isLoading: timelineLoading } = useCommentsTimeline();

  const statCards = stats ? toStatCards(stats) : [];
  const channels = [...(channelsData ?? [])].sort((a, b) => b.mappingCount - a.mappingCount);
  const maxMappings = Math.max(...channels.map((ch) => ch.mappingCount), 1);
  const series = (timeline ?? []).map((p) => p.count);
  const total24h = series.reduce((sum, n) => sum + n, 0);

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Dashboard"
        description="Live snapshot of YouTube → Slack comment forwarding."
        actions={
          <Button size="sm" onClick={() => router.push(ROUTE_PATH["ytc-feed"])}>
            <MessageSquare className="size-3.5" />
            Open feed
          </Button>
        }
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

      {/* Channels by mappings + 24h comments timeline */}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[2fr_3fr]">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <div>
              <CardTitle>Channels</CardTitle>
              <CardDescription>Tracked channels by mapping count</CardDescription>
            </div>
            <Button variant="ghost" size="sm" onClick={() => router.push(ROUTE_PATH["ytc-channels"])}>
              Channels
              <ChevronRight className="size-3.5" />
            </Button>
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

        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <div>
              <CardTitle>Comments · last 24h</CardTitle>
              <CardDescription>Comments processed per hour</CardDescription>
            </div>
            <Button variant="ghost" size="sm" onClick={() => router.push(ROUTE_PATH["ytc-feed"])}>
              Feed
              <ChevronRight className="size-3.5" />
            </Button>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {timelineLoading ? (
              <Skeleton className="h-[88px] w-full rounded-xl" />
            ) : (
              <>
                <div className="flex items-baseline gap-2">
                  <span className="mono text-[28px] font-semibold leading-none tracking-[-0.02em]">
                    {formatNumber(total24h)}
                  </span>
                  <span className="text-[12.5px] text-muted-foreground">processed in the last 24 hours</span>
                </div>
                <Sparkline data={series.length ? series : [0]} color="var(--primary)" />
              </>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

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
