"use client";

import Link from "next/link";
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
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { apiErrorMessage } from "@/lib/api/client";
import {
  useAvailableChannels,
  useCreateChannel,
  useCreateMapping,
  useMappingOptions,
  useRefreshSlackChannels,
  useUpdateMapping,
} from "@/features/comments/hooks";
import {
  POLLING_FREQUENCY_OPTIONS,
  REPLY_SCAN_FREQUENCY_OPTIONS,
  type MappingDto,
  type PollingFrequency,
  type ReplyScanFrequency,
} from "@/features/comments/types";

/**
 * Create or edit a mapping. In create mode the YouTube + Slack endpoints are picked from
 * `GET /mappings/options`; in edit mode they're fixed (the backend's PATCH only touches the polling
 * config) and shown read-only. Submitting calls the create/update mutation, which keeps the dynamic
 * scheduler in sync server-side and invalidates the mappings query so the table refetches.
 */
export function MappingFormDialog({
  open,
  onOpenChange,
  mapping,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  mapping?: MappingDto;
}) {
  const isEdit = Boolean(mapping);
  const refreshSlack = useRefreshSlackChannels();
  // Only read the picker options once the on-open Slack refresh settles, so it never shows the stale
  // pre-refresh cache (the shared Connections install doesn't fill this module's channel cache by itself).
  const optionsQuery = useMappingOptions(open && !isEdit && (refreshSlack.isSuccess || refreshSlack.isError));
  // Monitoring is OAuth-only: you can only track channels a connected Google account owns (force-ssl).
  // The picker lists those; an empty list is the honest "connect Google to enable monitoring" gate.
  const availableQuery = useAvailableChannels(open && !isEdit);
  const createMapping = useCreateMapping();
  const updateMapping = useUpdateMapping();
  const createChannel = useCreateChannel();

  const [youTubeChannelId, setYouTubeChannelId] = useState("");
  const [slackChannelId, setSlackChannelId] = useState("");
  const [addChannelId, setAddChannelId] = useState("");
  const [frequency, setFrequency] = useState<PollingFrequency>(15);
  const [includeReplies, setIncludeReplies] = useState(false);
  const [replySweepFrequency, setReplySweepFrequency] = useState<ReplyScanFrequency>(0);
  const [replyWindowDays, setReplyWindowDays] = useState(30);

  useEffect(() => {
    if (!open) return;
    if (mapping) {
      setFrequency(mapping.frequency);
      setIncludeReplies(mapping.includeReplies);
      setReplySweepFrequency(mapping.replySweepFrequency);
      setReplyWindowDays(mapping.replyWindowDays);
    } else {
      setYouTubeChannelId("");
      setSlackChannelId("");
      setAddChannelId("");
      setFrequency(15);
      setIncludeReplies(false);
      setReplySweepFrequency(0);
      setReplyWindowDays(30);
      // Sync every active workspace's channels before loading the picker; `reset` re-arms it on each reopen.
      refreshSlack.reset();
      refreshSlack.mutate();
    }
    // `refreshSlack` is a stable react-query handle — re-run only when the dialog (re)opens or the mode flips.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, mapping]);

  const busy = createMapping.isPending || updateMapping.isPending;
  // The picker can't render until the on-open refresh settles and the options query has fetched.
  const loadingOptions = refreshSlack.isPending || optionsQuery.isFetching;
  // The operator's connected channels that can be monitored, and the ones not yet tracked.
  const available = availableQuery.data ?? [];
  const addable = available.filter((c) => !c.alreadyTracked);
  // No connected, comment-capable Google account ⇒ honest gated state (connect Google to enable monitoring).
  const noConnectedChannels = availableQuery.isSuccess && available.length === 0;

  // Track one of the operator's own connected channels inline so a mapping can be created end-to-end
  // without a separate Channels page. No YouTube lookup — the backend validates it against the connected
  // accounts. On success the options query refetches and we auto-select the freshly tracked channel.
  async function onAddChannel() {
    if (!addChannelId) return;
    try {
      const channel = await createChannel.mutateAsync({ youTubeChannelId: addChannelId });
      setAddChannelId("");
      setYouTubeChannelId(channel.id);
      toast.success(`Now tracking ${channel.title}.`);
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }
  const channels = optionsQuery.data?.youTubeChannels ?? [];
  const slackChannels = optionsQuery.data?.slackChannels ?? [];

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    // A sweep cadence only matters when replies are forwarded; pin it Off otherwise.
    const sweep: ReplyScanFrequency = includeReplies ? replySweepFrequency : 0;
    try {
      if (mapping) {
        await updateMapping.mutateAsync({
          id: mapping.id,
          body: { frequency, includeReplies, replySweepFrequency: sweep, replyWindowDays },
        });
        toast.success("Mapping updated.");
      } else {
        if (!youTubeChannelId || !slackChannelId) {
          toast.error("Pick a YouTube channel and a Slack channel.");
          return;
        }
        await createMapping.mutateAsync({
          youTubeChannelId,
          slackChannelId,
          frequency,
          includeReplies,
          replySweepFrequency: sweep,
          replyWindowDays,
        });
        toast.success("Mapping created.");
      }
      onOpenChange(false);
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !busy && onOpenChange(next)}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{isEdit ? "Edit mapping" : "Add mapping"}</DialogTitle>
          <DialogDescription>
            Forward new comments from a YouTube channel into a Slack channel.
          </DialogDescription>
        </DialogHeader>

        <form id="mapping-form" onSubmit={onSubmit} className="flex flex-col gap-4">
          {isEdit ? (
            <div className="grid grid-cols-2 gap-3 rounded-lg bg-muted px-3 py-2.5 text-[13px]">
              <div>
                <div className="text-[11px] text-muted-foreground">YouTube channel</div>
                <div className="truncate font-[540]">{mapping!.youTubeChannelTitle}</div>
              </div>
              <div>
                <div className="text-[11px] text-muted-foreground">Slack channel</div>
                <div className="mono truncate">
                  {mapping!.slackChannelName}
                  <span className="text-muted-foreground"> · {mapping!.slackWorkspaceName}</span>
                </div>
              </div>
            </div>
          ) : (
            <>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="yt-channel">YouTube channel</Label>
                <Select value={youTubeChannelId} onValueChange={setYouTubeChannelId} disabled={loadingOptions}>
                  <SelectTrigger id="yt-channel">
                    <SelectValue
                      placeholder={
                        loadingOptions
                          ? "Loading channels…"
                          : channels.length
                            ? "Select a channel…"
                            : "No channels yet — add one below"
                      }
                    />
                  </SelectTrigger>
                  <SelectContent>
                    {channels.map((c) => (
                      <SelectItem key={c.id} value={c.id}>
                        {c.title}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {addable.length > 0 && (
                  <div className="flex gap-1.5">
                    <Select value={addChannelId} onValueChange={setAddChannelId} disabled={createChannel.isPending}>
                      <SelectTrigger id="add-channel" className="flex-1">
                        <SelectValue placeholder="Track one of your channels…" />
                      </SelectTrigger>
                      <SelectContent>
                        {addable.map((c) => (
                          <SelectItem key={c.youTubeChannelId} value={c.youTubeChannelId}>
                            {c.title}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                    <Button
                      type="button"
                      variant="outline"
                      onClick={onAddChannel}
                      disabled={createChannel.isPending || !addChannelId}
                    >
                      {createChannel.isPending ? "Adding…" : "Add"}
                    </Button>
                  </div>
                )}
                {noConnectedChannels ? (
                  <p className="text-[11px] text-amber-600 dark:text-amber-500">
                    No connected YouTube channel can be monitored. Connect a Google account (and grant the
                    comment-management permission) in{" "}
                    <Link href="/connections/google" className="underline underline-offset-2">
                      Connections → Google
                    </Link>{" "}
                    to enable monitoring.
                  </p>
                ) : (
                  <p className="text-[11px] text-muted-foreground">
                    Monitoring runs on the channel owner&apos;s Google account — pick one of your connected
                    channels. It&apos;s selected automatically once tracked.
                  </p>
                )}
              </div>

              <div className="flex flex-col gap-1.5">
                <Label htmlFor="slack-channel">Slack channel</Label>
                <Select value={slackChannelId} onValueChange={setSlackChannelId} disabled={loadingOptions}>
                  <SelectTrigger id="slack-channel">
                    <SelectValue
                      placeholder={
                        loadingOptions
                          ? "Refreshing channels…"
                          : slackChannels.length
                            ? "Select a channel…"
                            : "No channels — connect Slack first"
                      }
                    />
                  </SelectTrigger>
                  <SelectContent>
                    {slackChannels.map((c) => (
                      <SelectItem key={c.id} value={c.id}>
                        {c.name} · {c.workspaceName}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {!loadingOptions && slackChannels.length === 0 && (
                  <p className="text-[11px] text-amber-600 dark:text-amber-500">
                    No Slack channels found. Connect a workspace in{" "}
                    <Link href="/connections/slack" className="underline underline-offset-2">
                      Connections → Slack
                    </Link>{" "}
                    (and invite the bot to a channel), then reopen.
                  </p>
                )}
              </div>
            </>
          )}

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="frequency">Polling cadence</Label>
            <Select value={String(frequency)} onValueChange={(v) => setFrequency(Number(v) as PollingFrequency)}>
              <SelectTrigger id="frequency">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {POLLING_FREQUENCY_OPTIONS.map((o) => (
                  <SelectItem key={o.value} value={String(o.value)}>
                    {o.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="flex items-center justify-between rounded-lg border px-3 py-2.5">
            <div>
              <Label htmlFor="include-replies" className="cursor-pointer">
                Forward replies
              </Label>
              <p className="text-[11.5px] text-muted-foreground">Also post replies, threaded under their comment.</p>
            </div>
            <Switch id="include-replies" checked={includeReplies} onCheckedChange={setIncludeReplies} />
          </div>

          {includeReplies && (
            <div className="grid grid-cols-2 gap-3">
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="sweep">Reply sweep</Label>
                <Select
                  value={String(replySweepFrequency)}
                  onValueChange={(v) => setReplySweepFrequency(Number(v) as ReplyScanFrequency)}
                >
                  <SelectTrigger id="sweep">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {REPLY_SCAN_FREQUENCY_OPTIONS.map((o) => (
                      <SelectItem key={o.value} value={String(o.value)}>
                        {o.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="window">Look-back (days)</Label>
                <Input
                  id="window"
                  type="number"
                  min={1}
                  max={90}
                  value={replyWindowDays}
                  onChange={(e) => setReplyWindowDays(Number(e.target.value))}
                />
              </div>
            </div>
          )}
        </form>

        <DialogFooter>
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={busy}>
            Cancel
          </Button>
          <Button type="submit" form="mapping-form" disabled={busy}>
            {busy ? "Saving…" : isEdit ? "Save changes" : "Create mapping"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
