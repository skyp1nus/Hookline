"use client";

import { Plus, TriangleAlert } from "lucide-react";

import { GoogleIcon, YoutubeIcon } from "@/components/brand-icons";
import { NotYet } from "@/components/not-yet";
import { PageHeading } from "@/components/page-heading";
import { usePlatform } from "@/components/platform-context";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import {
  useDisconnectAccount,
  useGoogleAccounts,
  useGoogleProjects,
} from "@/features/connections/hooks";

import {
  ConnectCard,
  ConnectionCard,
  ConnectionGrid,
} from "../_components/connection-card";

const BACKEND = process.env.NEXT_PUBLIC_BACKEND_URL ?? "";

export default function GoogleConnectionsPage() {
  const { platform } = usePlatform();
  const { data, isLoading } = useGoogleAccounts();
  const projectsQuery = useGoogleProjects();
  const disconnect = useDisconnectAccount();
  const accounts = data ?? [];

  // Google OAuth is per-project: the consent screen uses a stored Google Cloud client (id/secret). With no
  // project there is nothing to authorize against, so we surface an honest "add a project first" state
  // instead of letting the backend bounce to ?error=missing_client. (Project-creation UI is deferred — see
  // docs/backend-todo.md.)
  const projects = projectsQuery.data ?? [];
  const project = projects.find((p) => p.status?.toLowerCase() === "active") ?? projects[0];
  const connectHref = project
    ? `${BACKEND}/google/youtube-uploads/oauth/start?projectId=${project.id}`
    : null;

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title={platform.account}
        description="Google accounts authorized to upload videos to YouTube."
        actions={
          connectHref ? (
            <Button size="sm" asChild>
              <a href={connectHref}>
                <Plus className="size-3.5" />
                Connect account
              </a>
            </Button>
          ) : (
            <NotYet reason="Add a Google Cloud project (client id/secret) first.">
              <Button size="sm" className="pointer-events-none" disabled>
                <Plus className="size-3.5" />
                Connect account
              </Button>
            </NotYet>
          )
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
            {accounts.map((acct) => (
              <ConnectionCard
                key={acct.id}
                connection={acct}
                icon={YoutubeIcon}
                iconClassName="text-[#FF0033]"
                onDisconnect={(id) => disconnect.mutateAsync(id)}
                disconnectTitle="Disconnect account?"
                disconnectDescription={`Disconnect ${acct.name}? Any upload mapping that targets it is removed too.`}
              />
            ))}
            {connectHref ? (
              <ConnectCard
                title="Connect account"
                subtitle="Authorize a new Google account"
                href={connectHref}
              />
            ) : (
              <Card className="border-dashed bg-transparent p-0 ring-0">
                <div className="flex min-h-[150px] w-full flex-col items-center justify-center gap-2 p-5 text-center text-muted-foreground">
                  <div className="flex size-10 items-center justify-center rounded-[10px] border text-warn">
                    <TriangleAlert className="size-[19px]" />
                  </div>
                  <div className="text-sm font-[560] text-foreground">No Google Cloud project yet</div>
                  <div className="text-[12.5px]">
                    Add a project (client id/secret) before connecting an account. Project setup UI is coming
                    soon.
                  </div>
                </div>
              </Card>
            )}
          </>
        )}
      </ConnectionGrid>

      <p className="flex items-center gap-2 text-[12.5px] text-muted-foreground">
        <GoogleIcon size={14} />
        Hookline only requests the youtube.upload scope. Quota is governed per API key.
      </p>
    </div>
  );
}
