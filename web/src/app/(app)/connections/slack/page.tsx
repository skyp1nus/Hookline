"use client";

import { Plus } from "lucide-react";
import { useEffect, useState } from "react";

import { SlackIcon } from "@/components/brand-icons";
import { PageHeading } from "@/components/page-heading";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { DATA } from "@/lib/mock-data";

import {
  ConnectCard,
  ConnectionCard,
  ConnectionGrid,
  type Connection,
} from "../_components/connection-card";

const SLACK_OAUTH_START = `${process.env.NEXT_PUBLIC_BACKEND_URL ?? ""}/slack/oauth/start`;

/** Slack-specific detail beyond DATA.health (which only knows the 2/2 count). */
const SLACK_WORKSPACES: Connection[] = [
  {
    id: "ws-daniels-team",
    name: "Daniel's Team",
    handle: "daniels-team.slack.com",
    meta: "5 channels mapped · OAuth valid",
  },
  {
    id: "ws-side-project",
    name: "Side Project Co.",
    handle: "sideproject.slack.com",
    meta: "2 channels mapped · OAuth valid",
  },
];

export default function SlackConnectionsPage() {
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    const t = setTimeout(() => setLoading(false), 600);
    return () => clearTimeout(t);
  }, []);

  const slackHealth = DATA.health.find((h) => h.id === "slack");

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Slack workspaces"
        description="Workspaces that receive forwarded comments and upload reports."
        actions={
          <Button size="sm" asChild>
            <a href={SLACK_OAUTH_START}>
              <Plus className="size-3.5" />
              Add workspace
            </a>
          </Button>
        }
      />

      <ConnectionGrid>
        {loading ? (
          [0, 1, 2].map((i) => (
            <Card key={i} className="p-0">
              <div className="flex flex-col gap-3.5 p-[18px]">
                <div className="flex items-start justify-between">
                  <Skeleton className="size-10 rounded-[10px]" />
                  <Skeleton className="h-[22px] w-24 rounded-full" />
                </div>
                <Skeleton className="h-4 w-32" />
                <Skeleton className="h-3 w-40" />
                <Skeleton className="h-8 w-full" />
              </div>
            </Card>
          ))
        ) : (
          <>
            {SLACK_WORKSPACES.map((ws) => (
              <ConnectionCard
                key={ws.id}
                connection={ws}
                icon={SlackIcon}
                iconClassName="text-[#4A154B] dark:text-[#E01E5A]"
              />
            ))}
            <ConnectCard
              title="Connect a workspace"
              subtitle={
                slackHealth
                  ? `${slackHealth.ok} connected · authorize another Slack workspace`
                  : "Authorize a new Slack workspace"
              }
              href={SLACK_OAUTH_START}
            />
          </>
        )}
      </ConnectionGrid>
    </div>
  );
}
