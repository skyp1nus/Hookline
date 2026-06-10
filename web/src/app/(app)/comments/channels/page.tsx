"use client";

import { Plus } from "lucide-react";
import { useRouter } from "next/navigation";

import { PageHeading } from "@/components/page-heading";
import { StatusBadge } from "@/components/status";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { formatNumber } from "@/lib/format";
import { ROUTE_PATH } from "@/lib/nav";
import { useChannels } from "@/features/comments/hooks";
import { type YouTubeChannelDto } from "@/features/comments/types";

function initials(name: string) {
  return name
    .split(" ")
    .map((w) => w[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
}

export default function ChannelsPage() {
  const { data } = useChannels();
  const channels = data ?? [];
  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Channels"
        description="YouTube channels being watched for new comments."
        actions={
          <Button size="sm">
            <Plus className="size-3.5" />
            Add channel
          </Button>
        }
      />

      {channels.length === 0 ? (
        <Card className="p-10 text-center text-[13.5px] text-muted-foreground">
          No channels tracked yet. Add one to start forwarding its comments.
        </Card>
      ) : (
        <div className="grid grid-cols-[repeat(auto-fill,minmax(320px,1fr))] gap-4">
          {channels.map((ch) => (
            <ChannelCard key={ch.id} channel={ch} />
          ))}
        </div>
      )}
    </div>
  );
}

function ChannelCard({ channel }: { channel: YouTubeChannelDto }) {
  const router = useRouter();
  const idle = channel.mappingCount === 0;
  return (
    <Card className="p-[18px]">
      <div className="flex items-start gap-3">
        <Avatar className="size-11 shrink-0">
          <AvatarFallback className="bg-primary/15 text-primary">{initials(channel.title)}</AvatarFallback>
        </Avatar>
        <div className="min-w-0 flex-1">
          <div className="truncate text-[14.5px] font-[580]">{channel.title}</div>
          <div className="mono mt-px text-xs text-muted-foreground">{channel.handle ?? "—"}</div>
        </div>
        <StatusBadge tone={idle ? "neutral" : "ok"} dot>
          {idle ? "Idle" : "Active"}
        </StatusBadge>
      </div>

      <div className="mt-4 flex items-center justify-between rounded-[9px] bg-muted px-[13px] py-[11px]">
        <div>
          <div
            className={
              channel.mappingCount
                ? "mono text-base font-semibold text-foreground"
                : "mono text-base font-semibold text-muted-foreground"
            }
          >
            {formatNumber(channel.mappingCount)}
          </div>
          <div className="text-[11px] text-muted-foreground">Slack mappings</div>
        </div>
        <div className="text-right">
          <div className="text-[12px] font-medium">{new Date(channel.addedAt).toLocaleDateString()}</div>
          <div className="text-[11px] text-muted-foreground">added</div>
        </div>
      </div>

      <div className="mt-3.5 flex items-center justify-end">
        <Button variant="outline" size="sm" onClick={() => router.push(ROUTE_PATH["ytc-mappings"])}>
          Manage
        </Button>
      </div>
    </Card>
  );
}
