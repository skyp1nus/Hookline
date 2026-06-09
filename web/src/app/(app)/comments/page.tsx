"use client";

import { ArrowDown, ArrowUp, ChevronRight, MessageSquare } from "lucide-react";
import { useRouter } from "next/navigation";

import { Sparkline } from "@/components/charts";
import { PageHeading } from "@/components/page-heading";
import { ProgressBar } from "@/components/progress-bar";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { type StatItem, type YtChannel } from "@/lib/mock-data";
import { formatNumber } from "@/lib/format";
import { ROUTE_PATH } from "@/lib/nav";
import { cn } from "@/lib/utils";
import { useChannels, useCommentStats, useCommentsFeed } from "@/features/comments/hooks";

import { CommentCard } from "./_components/comment-card";

function initials(name: string) {
  return name
    .split(" ")
    .map((w) => w[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
}

export default function CommentsDashboardPage() {
  const router = useRouter();
  const { data: stats, isLoading: statsLoading } = useCommentStats();
  const { data: channelsData, isLoading: channelsLoading } = useChannels();
  const { data: feed, isLoading: feedLoading } = useCommentsFeed();

  const commentStats = stats ?? [];
  const channels = channelsData ?? [];
  const maxFwd = Math.max(...channels.map((ch) => ch.fwd24), 1);
  const latest = (feed ?? []).filter((f) => !f.held).slice(0, 3);

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
          : commentStats.map((s) => <StatCard key={s.id} stat={s} />)}
      </div>

      {/* Forwarding by channel + latest forwards */}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[2fr_3fr]">
        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <div>
              <CardTitle>Forwarding by channel</CardTitle>
              <CardDescription>Comments forwarded · 24h</CardDescription>
            </div>
            <Button variant="ghost" size="sm" onClick={() => router.push(ROUTE_PATH["ytc-channels"])}>
              Channels
              <ChevronRight className="size-3.5" />
            </Button>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {channelsLoading
              ? [0, 1, 2].map((i) => <Skeleton key={i} className="h-8 w-full" />)
              : channels.map((ch) => (
                  <ChannelBar key={ch.id} channel={ch} max={maxFwd} />
                ))}
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="flex flex-row items-center justify-between">
            <div>
              <CardTitle>Latest forwards</CardTitle>
              <CardDescription>Most recent comments delivered to Slack</CardDescription>
            </div>
            <Button variant="ghost" size="sm" onClick={() => router.push(ROUTE_PATH["ytc-feed"])}>
              Feed
              <ChevronRight className="size-3.5" />
            </Button>
          </CardHeader>
          <CardContent className="flex flex-col gap-2.5">
            {feedLoading
              ? [0, 1, 2].map((i) => <Skeleton key={i} className="h-[88px] w-full rounded-xl" />)
              : latest.map((f) => <CommentCard key={f.id} item={f} compact />)}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

function StatCard({ stat }: { stat: StatItem }) {
  const up = stat.trend === "up";
  const down = stat.trend === "down";
  const deltaColor = up ? "text-ok" : down ? "text-danger" : "text-muted-foreground";
  const sparkColor = up ? "var(--primary)" : down ? "var(--danger)" : "var(--muted-foreground)";
  return (
    <Card>
      <CardContent>
        <div className="text-[12.5px] font-medium text-muted-foreground">{stat.label}</div>
        <div className="mt-2.5 flex items-baseline gap-2">
          <div className="mono text-[28px] font-semibold leading-none tracking-[-0.02em]">{stat.value}</div>
          {stat.trend !== "flat" && (
            <span className={cn("inline-flex items-center gap-0.5 text-[12.5px] font-medium", deltaColor)}>
              {up ? <ArrowUp className="size-[13px]" /> : <ArrowDown className="size-[13px]" />}
              {stat.sub.replace(/^[+-]/, "")}
            </span>
          )}
        </div>
        <div className="mt-3 flex items-end justify-between">
          <div className="text-[11.5px] text-muted-foreground">
            {stat.trend === "flat" ? stat.sub : "vs. prior 24h"}
          </div>
          {stat.spark && <Sparkline data={stat.spark} color={sparkColor} />}
        </div>
      </CardContent>
    </Card>
  );
}

function ChannelBar({ channel, max }: { channel: YtChannel; max: number }) {
  return (
    <div>
      <div className="mb-[7px] flex items-center gap-2.5">
        <Avatar size="sm" className="shrink-0">
          <AvatarFallback className="bg-primary/15 text-primary text-[10px]">
            {initials(channel.name)}
          </AvatarFallback>
        </Avatar>
        <div className="min-w-0 flex-1 truncate text-[13px] font-[540]">{channel.name}</div>
        <span
          className={cn(
            "mono text-[13px] font-semibold",
            channel.fwd24 ? "text-foreground" : "text-muted-foreground",
          )}
        >
          {formatNumber(channel.fwd24)}
        </span>
      </div>
      <ProgressBar value={(channel.fwd24 / max) * 100} tone={channel.fwd24 ? "primary" : "ok"} height={6} />
    </div>
  );
}
