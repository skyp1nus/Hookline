"use client";

import { Plus } from "lucide-react";

import { SlackIcon } from "@/components/brand-icons";
import { PageHeading } from "@/components/page-heading";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { useSlackWorkspaces } from "@/features/connections/hooks";

import {
  ConnectCard,
  ConnectionCard,
  ConnectionGrid,
} from "../_components/connection-card";

const SLACK_OAUTH_START = `${process.env.NEXT_PUBLIC_BACKEND_URL ?? ""}/slack/oauth/start`;

export default function SlackConnectionsPage() {
  const { data, isLoading } = useSlackWorkspaces();
  const workspaces = data ?? [];

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
        {isLoading ? (
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
            {workspaces.map((ws) => (
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
                workspaces.length
                  ? `${workspaces.length} connected · authorize another Slack workspace`
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
