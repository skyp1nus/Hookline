"use client";

import { ChevronDown, Plus, TriangleAlert } from "lucide-react";

import { GoogleIcon, YoutubeIcon } from "@/components/brand-icons";
import { NotYet } from "@/components/not-yet";
import { PageHeading } from "@/components/page-heading";
import { usePlatform } from "@/components/platform-context";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Skeleton } from "@/components/ui/skeleton";
import {
  type GoogleProject,
  useDisconnectAccount,
  useGoogleAccounts,
  useGoogleProjects,
} from "@/features/connections/hooks";

import {
  ConnectCard,
  ConnectionCard,
  ConnectionGrid,
} from "../_components/connection-card";
import { ProjectsSection } from "../_components/projects-section";

const BACKEND = process.env.NEXT_PUBLIC_BACKEND_URL ?? "";

function connectHref(project: GoogleProject) {
  return `${BACKEND}/google/youtube-uploads/oauth/start?projectId=${project.id}`;
}

/**
 * Header "Connect account" affordance. Google OAuth is per-project (the consent screen authorizes against
 * one stored client), so we never auto-pick: with one active project it's a direct link, with several it's
 * a picker, and with none it's an honest disabled state pointing at the projects section.
 */
function ConnectAccountAction({
  activeProjects,
  anyProjects,
}: {
  activeProjects: GoogleProject[];
  anyProjects: boolean;
}) {
  if (activeProjects.length === 0) {
    return (
      <NotYet
        reason={
          anyProjects
            ? "Enable a Google Cloud project to connect accounts."
            : "Add a Google Cloud project (client id/secret) first."
        }
      >
        <Button size="sm" className="pointer-events-none" disabled>
          <Plus className="size-3.5" />
          Connect account
        </Button>
      </NotYet>
    );
  }

  if (activeProjects.length === 1) {
    return (
      <Button size="sm" asChild>
        <a href={connectHref(activeProjects[0])}>
          <Plus className="size-3.5" />
          Connect account
        </a>
      </Button>
    );
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button size="sm">
          <Plus className="size-3.5" />
          Connect account
          <ChevronDown className="size-3.5" />
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-56">
        {activeProjects.map((project) => (
          <DropdownMenuItem key={project.id} asChild>
            <a href={connectHref(project)}>
              <GoogleIcon size={14} />
              <span className="truncate">{project.label}</span>
            </a>
          </DropdownMenuItem>
        ))}
      </DropdownMenuContent>
    </DropdownMenu>
  );
}

export default function GoogleConnectionsPage() {
  const { platform } = usePlatform();
  const { data, isLoading } = useGoogleAccounts();
  const projectsQuery = useGoogleProjects();
  const disconnect = useDisconnectAccount();
  const accounts = data ?? [];

  const projects = projectsQuery.data ?? [];
  const activeProjects = projects.filter((p) => p.status?.toLowerCase() === "active");
  // The in-grid "connect new" card stays a single, unambiguous link only when there's exactly one place to
  // send the user. With several active projects the per-project rows / header picker are the path.
  const soleActive = activeProjects.length === 1 ? activeProjects[0] : null;

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title={platform.account}
        description="Google accounts authorized to upload videos to YouTube."
        actions={
          <ConnectAccountAction activeProjects={activeProjects} anyProjects={projects.length > 0} />
        }
      />

      <ProjectsSection projects={projects} isLoading={projectsQuery.isLoading} />

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
            {soleActive ? (
              <ConnectCard
                title="Connect account"
                subtitle="Authorize a new Google account"
                href={connectHref(soleActive)}
              />
            ) : activeProjects.length > 1 ? (
              <Card className="border-dashed bg-transparent p-0 ring-0">
                <div className="flex min-h-[150px] w-full flex-col items-center justify-center gap-2 p-5 text-center text-muted-foreground">
                  <div className="flex size-10 items-center justify-center rounded-[10px] border">
                    <Plus className="size-[19px]" />
                  </div>
                  <div className="text-sm font-[560] text-foreground">Connect account</div>
                  <div className="text-[12.5px]">
                    Use the &ldquo;Connect account&rdquo; action on a project above to choose which one
                    authorizes it.
                  </div>
                </div>
              </Card>
            ) : (
              <Card className="border-dashed bg-transparent p-0 ring-0">
                <div className="flex min-h-[150px] w-full flex-col items-center justify-center gap-2 p-5 text-center text-muted-foreground">
                  <div className="flex size-10 items-center justify-center rounded-[10px] border text-warn">
                    <TriangleAlert className="size-[19px]" />
                  </div>
                  <div className="text-sm font-[560] text-foreground">
                    {projects.length === 0 ? "No Google Cloud project yet" : "No active project"}
                  </div>
                  <div className="text-[12.5px]">
                    {projects.length === 0
                      ? "Add a project in the section above before connecting an account."
                      : "Enable a project in the section above to connect an account."}
                  </div>
                </div>
              </Card>
            )}
          </>
        )}
      </ConnectionGrid>

      <p className="flex items-center gap-2 text-[12.5px] text-muted-foreground">
        <GoogleIcon size={14} />
        Hookline requests the youtube.upload + youtube.force-ssl scopes on the connected account; YouTube
        Data API quota is governed per Google project.
      </p>
    </div>
  );
}
