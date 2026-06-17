"use client";

import { type FormEvent, useEffect, useState } from "react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { apiErrorMessage } from "@/lib/api/client";
import {
  type GoogleProject,
  useCreateGoogleProject,
  useUpdateGoogleProject,
} from "@/features/connections/hooks";

/**
 * Create a Google Cloud project (label + client id + write-only secret) or rename an existing one. In edit
 * mode only the label is editable — the client id is fixed (it issued the existing accounts' refresh
 * tokens) and shown read-only, and there is no secret field. The client secret lives only in local state
 * and is cleared on submit/close; it is never read back or persisted client-side.
 */
export function ProjectFormDialog({
  open,
  onOpenChange,
  project,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  project?: GoogleProject;
}) {
  const isEdit = Boolean(project);
  const createProject = useCreateGoogleProject();
  const updateProject = useUpdateGoogleProject();

  const [label, setLabel] = useState("");
  const [clientId, setClientId] = useState("");
  const [clientSecret, setClientSecret] = useState("");

  useEffect(() => {
    if (!open) return;
    if (project) {
      setLabel(project.label);
      setClientId(project.clientId);
      setClientSecret("");
    } else {
      setLabel("");
      setClientId("");
      setClientSecret("");
    }
  }, [open, project]);

  const busy = createProject.isPending || updateProject.isPending;

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    try {
      if (project) {
        const trimmed = label.trim();
        if (!trimmed) {
          toast.error("Enter a project name.");
          return;
        }
        await updateProject.mutateAsync({ id: project.id, label: trimmed });
        toast.success("Project renamed.");
      } else {
        if (!clientId.trim() || !clientSecret.trim()) {
          toast.error("Enter the client ID and client secret.");
          return;
        }
        await createProject.mutateAsync({
          label: label.trim(),
          clientId: clientId.trim(),
          clientSecret,
        });
        toast.success("Project added.");
      }
      // Never let the secret linger in state after a submit.
      setClientSecret("");
      onOpenChange(false);
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (busy) return;
        // Drop the secret the moment the dialog closes for any reason.
        if (!next) setClientSecret("");
        onOpenChange(next);
      }}
    >
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{isEdit ? "Rename project" : "Add Google Cloud project"}</DialogTitle>
          <DialogDescription>
            {isEdit
              ? "Update the display name. The client id and secret can't be changed here."
              : "Paste the OAuth client id and secret from the Google Cloud console. The secret is encrypted at rest and never shown again."}
          </DialogDescription>
        </DialogHeader>

        <form id="project-form" onSubmit={onSubmit} className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="project-label">Label</Label>
            <Input
              id="project-label"
              placeholder="e.g. Project A"
              value={label}
              onChange={(e) => setLabel(e.target.value)}
              autoFocus
            />
            {!isEdit && (
              <p className="text-[11px] text-muted-foreground">Optional — defaults to the client id.</p>
            )}
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="project-client-id">Client ID</Label>
            <Input
              id="project-client-id"
              className="mono"
              placeholder="xxxxxxxx.apps.googleusercontent.com"
              value={clientId}
              onChange={(e) => setClientId(e.target.value)}
              readOnly={isEdit}
              disabled={isEdit}
            />
          </div>

          {!isEdit && (
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="project-client-secret">Client secret</Label>
              <Input
                id="project-client-secret"
                type="password"
                autoComplete="off"
                placeholder="GOCSPX-…"
                value={clientSecret}
                onChange={(e) => setClientSecret(e.target.value)}
              />
              <p className="text-[11px] text-muted-foreground">
                Stored encrypted; write-only — it can&apos;t be viewed after saving.
              </p>
            </div>
          )}
        </form>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </Button>
          <Button type="submit" form="project-form" disabled={busy}>
            {busy ? "Saving…" : isEdit ? "Save changes" : "Add project"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
