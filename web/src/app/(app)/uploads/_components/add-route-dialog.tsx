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
import { Label } from "@/components/ui/label";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { apiErrorMessage } from "@/lib/api/client";
import { useCreateUploadMapping, useUploadMappingOptions } from "@/features/uploads/hooks";

/**
 * Create an upload route (Slack channel → Google account) via the real `POST /youtube-uploads/mappings`.
 * Pickers come from the live Slack-channels + Google-accounts endpoints; the Slack option carries both the
 * channel id and its workspace id, which is what the create endpoint needs. Submitting invalidates the
 * mappings query so the table refetches.
 */
export function AddRouteDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const optionsQuery = useUploadMappingOptions(open);
  const createMapping = useCreateUploadMapping();

  const [slackChannelId, setSlackChannelId] = useState("");
  const [googleAccountId, setGoogleAccountId] = useState("");

  useEffect(() => {
    if (open) {
      setSlackChannelId("");
      setGoogleAccountId("");
    }
  }, [open]);

  const slackChannels = optionsQuery.data?.slackChannels ?? [];
  const googleAccounts = optionsQuery.data?.googleAccounts ?? [];
  const busy = createMapping.isPending;

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    const channel = slackChannels.find((c) => c.slackChannelId === slackChannelId);
    if (!channel || !googleAccountId) {
      toast.error("Pick a Slack channel and a YouTube account.");
      return;
    }
    try {
      await createMapping.mutateAsync({
        slackWorkspaceId: channel.workspaceId,
        slackChannelId: channel.slackChannelId,
        googleAccountId,
      });
      toast.success("Route created.");
      onOpenChange(false);
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !busy && onOpenChange(next)}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Add route</DialogTitle>
          <DialogDescription>
            Uploads dropped in a Slack channel land on the chosen YouTube account.
          </DialogDescription>
        </DialogHeader>

        <form id="add-route-form" onSubmit={onSubmit} className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="slack-channel">Slack channel</Label>
            <Select value={slackChannelId} onValueChange={setSlackChannelId}>
              <SelectTrigger id="slack-channel">
                <SelectValue
                  placeholder={
                    slackChannels.length ? "Select a channel…" : "No channels — connect Slack first"
                  }
                />
              </SelectTrigger>
              <SelectContent>
                {slackChannels.map((c) => (
                  <SelectItem key={c.slackChannelId} value={c.slackChannelId}>
                    {c.name} · {c.workspaceName}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="yt-account">YouTube account</Label>
            <Select value={googleAccountId} onValueChange={setGoogleAccountId}>
              <SelectTrigger id="yt-account">
                <SelectValue
                  placeholder={
                    googleAccounts.length ? "Select an account…" : "No accounts — connect Google first"
                  }
                />
              </SelectTrigger>
              <SelectContent>
                {googleAccounts.map((a) => (
                  <SelectItem key={a.id} value={a.id}>
                    {a.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </form>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </Button>
          <Button type="submit" form="add-route-form" disabled={busy}>
            {busy ? "Creating…" : "Create route"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
