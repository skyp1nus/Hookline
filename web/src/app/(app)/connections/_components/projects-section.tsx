"use client";

import { MoreHorizontal, Pencil, Plus, Power, PowerOff, Trash2, UserPlus } from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";

import { ConfirmDialog } from "@/components/confirm-dialog";
import { StatusBadge } from "@/components/status";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Skeleton } from "@/components/ui/skeleton";
import { apiErrorMessage } from "@/lib/api/client";
import {
  type GoogleProject,
  useDeleteGoogleProject,
  useUpdateGoogleProject,
} from "@/features/connections/hooks";

import { ProjectFormDialog } from "./project-form-dialog";

const BACKEND = process.env.NEXT_PUBLIC_BACKEND_URL ?? "";

/** Authorize a new account through a specific project (OAuth start pins the issuing client by id). */
function connectHref(project: GoogleProject) {
  return `${BACKEND}/google/youtube-uploads/oauth/start?projectId=${project.id}`;
}

/**
 * Client id is not a secret (it appears in the consent URL) — just noisy. Every Google client id ends in
 * the SAME ".apps.googleusercontent.com", so a tail mask is identical across projects; show the
 * distinguishing leading identifier and drop the boilerplate domain. Non-Google ids fall back to a tail.
 */
const GOOGLE_CLIENT_SUFFIX = ".apps.googleusercontent.com";
function maskClientId(clientId: string) {
  if (clientId.endsWith(GOOGLE_CLIENT_SUFFIX)) {
    return `${clientId.slice(0, -GOOGLE_CLIENT_SUFFIX.length)}…`;
  }
  return clientId.length > 12 ? `${clientId.slice(0, 8)}…${clientId.slice(-4)}` : clientId;
}

function isActive(project: GoogleProject) {
  return project.status?.toLowerCase() === "active";
}

export function ProjectsSection({
  projects,
  isLoading,
}: {
  projects: GoogleProject[];
  isLoading: boolean;
}) {
  const updateProject = useUpdateGoogleProject();
  const deleteProject = useDeleteGoogleProject();

  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<GoogleProject | null>(null);
  const [deleting, setDeleting] = useState<GoogleProject | null>(null);

  async function toggleStatus(project: GoogleProject) {
    const next = isActive(project) ? "Disabled" : "Active";
    try {
      await updateProject.mutateAsync({ id: project.id, status: next });
      toast.success(next === "Active" ? "Project enabled." : "Project disabled.");
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }

  return (
    <section className="flex flex-col gap-3">
      <div className="flex items-center justify-between">
        <div>
          <h2 className="text-[14.5px] font-[580] tracking-[-0.01em]">Google Cloud projects</h2>
          <p className="text-[12.5px] text-muted-foreground">
            Each project is an OAuth client with its own YouTube Data API quota. Add more to raise the daily
            upload ceiling — uploads rotate across active projects.
          </p>
        </div>
        <Button size="sm" variant="outline" onClick={() => setCreating(true)}>
          <Plus className="size-3.5" />
          Add project
        </Button>
      </div>

      <Card className="overflow-hidden p-0">
        {isLoading ? (
          <div className="flex flex-col divide-y">
            {[0, 1].map((i) => (
              <div key={i} className="flex items-center justify-between gap-3 px-4 py-3.5">
                <div className="flex flex-col gap-1.5">
                  <Skeleton className="h-4 w-40" />
                  <Skeleton className="h-3 w-28" />
                </div>
                <Skeleton className="h-8 w-32" />
              </div>
            ))}
          </div>
        ) : projects.length === 0 ? (
          <div className="flex flex-col items-center justify-center gap-1.5 px-5 py-10 text-center">
            <div className="text-[13.5px] font-[540]">No Google Cloud projects yet</div>
            <div className="max-w-md text-[12.5px] text-muted-foreground">
              Add a project (OAuth client id + secret) before connecting an account — the consent screen
              authorizes against a stored client.
            </div>
            <Button size="sm" className="mt-1.5" onClick={() => setCreating(true)}>
              <Plus className="size-3.5" />
              Add project
            </Button>
          </div>
        ) : (
          <div className="flex flex-col divide-y">
            {projects.map((project) => {
              const active = isActive(project);
              return (
                <div
                  key={project.id}
                  className="flex flex-wrap items-center justify-between gap-3 px-4 py-3.5"
                >
                  <div className="min-w-0">
                    <div className="flex items-center gap-2">
                      <span className="truncate text-[13.5px] font-[540]">{project.label}</span>
                      <StatusBadge tone={active ? "ok" : "neutral"} dot>
                        {active ? "Active" : "Disabled"}
                      </StatusBadge>
                    </div>
                    <div className="mono mt-0.5 text-[11.5px] text-muted-foreground">
                      {maskClientId(project.clientId)}
                    </div>
                  </div>

                  <div className="flex items-center gap-4">
                    <div className="text-right text-[12px] text-muted-foreground">
                      <div>
                        {project.accountCount} {project.accountCount === 1 ? "account" : "accounts"}
                      </div>
                      <div className="mono">
                        {project.quota.usedUploads}/{project.quota.uploadLimit} uploads
                      </div>
                    </div>

                    {active && (
                      <Button variant="outline" size="sm" asChild>
                        <a href={connectHref(project)}>
                          <UserPlus className="size-3.5" />
                          Connect account
                        </a>
                      </Button>
                    )}

                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon">
                          <MoreHorizontal className="size-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end" className="w-48">
                        <DropdownMenuItem onSelect={() => setEditing(project)}>
                          <Pencil className="size-4" />
                          Rename
                        </DropdownMenuItem>
                        <DropdownMenuItem onSelect={() => toggleStatus(project)}>
                          {active ? <PowerOff className="size-4" /> : <Power className="size-4" />}
                          {active ? "Disable" : "Enable"}
                        </DropdownMenuItem>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem variant="destructive" onSelect={() => setDeleting(project)}>
                          <Trash2 className="size-4" />
                          Delete
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </Card>

      {/* Create */}
      <ProjectFormDialog open={creating} onOpenChange={setCreating} />
      {/* Rename (keyed so the form reseeds per project) */}
      <ProjectFormDialog
        key={editing?.id ?? "rename"}
        open={editing !== null}
        onOpenChange={(open) => !open && setEditing(null)}
        project={editing ?? undefined}
      />
      {/* Delete */}
      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title="Delete project?"
        description={
          deleting
            ? `Remove ${deleting.label}. ${
                deleting.accountCount > 0
                  ? "Disconnect this project's accounts first — the issuing client can't be removed while accounts are bound to it."
                  : "This can't be undone."
              }`
            : undefined
        }
        confirmLabel="Delete project"
        successMessage="Project deleted."
        onConfirm={() => deleteProject.mutateAsync(deleting!.id)}
      />
    </section>
  );
}
