"use client";

import { Plus } from "lucide-react";
import { useRouter } from "next/navigation";

import { PageHeading } from "@/components/page-heading";
import { StatusBadge } from "@/components/status";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { DATA, type YtChannel } from "@/lib/mock-data";
import { formatNumber } from "@/lib/format";
import { ROUTE_PATH } from "@/lib/nav";
import { cn } from "@/lib/utils";

function initials(name: string) {
  return name
    .split(" ")
    .map((w) => w[0])
    .slice(0, 2)
    .join("")
    .toUpperCase();
}

export default function ChannelsPage() {
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

      <div className="grid grid-cols-[repeat(auto-fill,minmax(320px,1fr))] gap-4">
        {DATA.ytChannels.map((ch) => (
          <ChannelCard key={ch.id} channel={ch} />
        ))}
      </div>
    </div>
  );
}

function ChannelCard({ channel }: { channel: YtChannel }) {
  const router = useRouter();
  const idle = channel.status === "idle";
  return (
    <Card className="p-[18px]">
      <div className="flex items-start gap-3">
        <Avatar className="size-11 shrink-0">
          <AvatarFallback className="bg-primary/15 text-primary">{initials(channel.name)}</AvatarFallback>
        </Avatar>
        <div className="min-w-0 flex-1">
          <div className="truncate text-[14.5px] font-[580]">{channel.name}</div>
          <div className="mono mt-px text-xs text-muted-foreground">{channel.handle}</div>
        </div>
        <StatusBadge tone={idle ? "neutral" : "ok"} dot>
          {idle ? "Idle" : "Active"}
        </StatusBadge>
      </div>

      <div className="mt-4 flex gap-[18px]">
        <Stat label="Subscribers" value={channel.subs} />
        <Stat label="Videos" value={formatNumber(channel.videos)} />
        <Stat label="Mappings" value={formatNumber(channel.mappings)} />
      </div>

      <div className="mt-4 flex items-center justify-between rounded-[9px] bg-muted px-[13px] py-[11px]">
        <div>
          <div
            className={cn(
              "mono text-base font-semibold",
              channel.fwd24 ? "text-foreground" : "text-muted-foreground",
            )}
          >
            {formatNumber(channel.fwd24)}
          </div>
          <div className="text-[11px] text-muted-foreground">forwarded · 24h</div>
        </div>
        <div className="text-right">
          <div className="mono text-base font-semibold">{formatNumber(channel.fwd7)}</div>
          <div className="text-[11px] text-muted-foreground">· 7d</div>
        </div>
      </div>

      <div className="mt-3.5 flex items-center justify-between">
        <span className="text-[11.5px] text-muted-foreground">Last comment {channel.lastComment}</span>
        <Button variant="outline" size="sm" onClick={() => router.push(ROUTE_PATH["ytc-mappings"])}>
          Manage
        </Button>
      </div>
    </Card>
  );
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="mono text-[15px] font-semibold">{value}</div>
      <div className="mt-px text-[11px] text-muted-foreground">{label}</div>
    </div>
  );
}
