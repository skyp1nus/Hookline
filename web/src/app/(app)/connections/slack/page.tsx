"use client";

import { Plus } from "lucide-react";

import { SlackIcon } from "@/components/brand-icons";
import { PageHeading } from "@/components/page-heading";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import {
  type SlackWorkspace,
  useCommentsSlackWorkspaces,
  useDisconnectCommentsWorkspace,
  useDisconnectWorkspace,
  useSlackWorkspaces,
} from "@/features/connections/hooks";

import {
  ConnectCard,
  ConnectionCard,
  ConnectionGrid,
} from "../_components/connection-card";

// Backend-direct OAuth (bypasses the BFF); the install client is env-configured. Each TOOL is its own
// Slack app with its own bot, so each must be connected separately: a card posts as the bot of the app
// that owns it, and Slack routes that card's button interactivity back to the SAME app. Connecting the
// Comments app here is what makes "Reject on YouTube" work.
const BACKEND = process.env.NEXT_PUBLIC_BACKEND_URL ?? "";
const UPLOADS_OAUTH_START = `${BACKEND}/slack/youtube-uploads/oauth/start`;
const COMMENTS_OAUTH_START = `${BACKEND}/slack/youtube-comments/oauth/start`;

export default function SlackConnectionsPage() {
  return (
    <div className="flex flex-col gap-[26px]">
      <PageHeading
        title="Slack workspaces"
        description="Each tool is its own Slack app with its own bot — connect a workspace to the tool that should post there."
      />

      <SlackAppSection
        heading="YouTube Uploads bot"
        blurb="Posts upload reports + the cancel/confirm buttons."
        oauthStart={UPLOADS_OAUTH_START}
        query={useSlackWorkspaces()}
        disconnect={useDisconnectWorkspace()}
      />

      <SlackAppSection
        heading="YouTube Comments bot"
        blurb="Posts comment cards + the “Reject on YouTube” button. Connect this so rejects work."
        oauthStart={COMMENTS_OAUTH_START}
        query={useCommentsSlackWorkspaces()}
        disconnect={useDisconnectCommentsWorkspace()}
      />
    </div>
  );
}

function SlackAppSection({
  heading,
  blurb,
  oauthStart,
  query,
  disconnect,
}: {
  heading: string;
  blurb: string;
  oauthStart: string;
  query: { data: SlackWorkspace[] | undefined; isLoading: boolean };
  disconnect: { mutateAsync: (id: string) => Promise<unknown> };
}) {
  const workspaces = query.data ?? [];

  return (
    <section className="flex flex-col gap-3.5">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-[15px] font-[560] tracking-[-0.01em]">{heading}</h2>
          <p className="text-[12.5px] text-muted-foreground">{blurb}</p>
        </div>
        <Button size="sm" asChild>
          <a href={oauthStart}>
            <Plus className="size-3.5" />
            Add workspace
          </a>
        </Button>
      </div>

      <ConnectionGrid>
        {query.isLoading ? (
          [0, 1].map((i) => (
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
                onDisconnect={(id) => disconnect.mutateAsync(id)}
                disconnectTitle="Disconnect workspace?"
                disconnectDescription={`Disconnect ${ws.name}? Mappings that post here will stop until you reconnect.`}
              />
            ))}
            <ConnectCard
              title="Connect a workspace"
              subtitle={
                workspaces.length
                  ? `${workspaces.length} connected · authorize another Slack workspace`
                  : "Authorize a Slack workspace"
              }
              href={oauthStart}
            />
          </>
        )}
      </ConnectionGrid>
    </section>
  );
}
