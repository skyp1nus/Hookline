"use client";

import { useMemo } from "react";

import { PageHeading } from "@/components/page-heading";
import { StatusDot } from "@/components/status";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { formatNumber } from "@/lib/format";
import { useCommentsTimeline } from "@/features/comments/hooks";

function hourLabel(iso: string) {
  return new Date(iso).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

export default function FeedPage() {
  const { data, isLoading } = useCommentsTimeline();
  const points = useMemo(() => data ?? [], [data]);

  const total = points.reduce((sum, p) => sum + p.count, 0);
  const max = Math.max(...points.map((p) => p.count), 1);

  return (
    <div className="flex flex-col gap-[18px]">
      <PageHeading
        title="Feed"
        description="Comments processed and forwarded to Slack over the last 24 hours."
        actions={
          <div className="flex items-center gap-[7px] text-[12.5px] text-muted-foreground">
            <StatusDot tone="ok" pulse />
            Live
          </div>
        }
      />

      <Card>
        <CardHeader>
          <CardTitle>Comments · last 24h</CardTitle>
          <CardDescription>
            {isLoading ? "Loading…" : `${formatNumber(total)} comments processed, by hour`}
          </CardDescription>
        </CardHeader>
        <CardContent>
          {isLoading ? (
            <Skeleton className="h-44 w-full rounded-xl" />
          ) : total === 0 ? (
            <p className="py-12 text-center text-[13.5px] text-muted-foreground">
              No comments processed in the last 24 hours.
            </p>
          ) : (
            <div className="flex h-44 items-end gap-1">
              {points.map((p) => (
                <div
                  key={p.bucket}
                  className="group flex flex-1 flex-col items-center justify-end gap-1"
                  title={`${hourLabel(p.bucket)} · ${formatNumber(p.count)}`}
                >
                  <span className="mono text-[10px] text-muted-foreground opacity-0 transition-opacity group-hover:opacity-100">
                    {p.count}
                  </span>
                  <div
                    className="w-full rounded-t-[3px] bg-primary/80 transition-colors group-hover:bg-primary"
                    style={{ height: `${Math.max((p.count / max) * 100, p.count > 0 ? 4 : 1)}%` }}
                  />
                </div>
              ))}
            </div>
          )}
          {!isLoading && total > 0 && (
            <div className="mt-2 flex justify-between text-[10.5px] text-muted-foreground">
              <span>{hourLabel(points[0].bucket)}</span>
              <span>{hourLabel(points[points.length - 1].bucket)}</span>
            </div>
          )}
        </CardContent>
      </Card>

      <p className="text-center text-[12px] text-muted-foreground">
        A per-comment feed is not available yet — this view shows the hourly processing timeline.
      </p>
    </div>
  );
}
