"use client";

import { type FormEvent, useState } from "react";
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
import { useCreateChannel } from "@/features/comments/hooks";

/**
 * Add a tracked YouTube channel by raw id, `@handle`, or youtube.com URL. The backend resolves it against
 * the YouTube Data API (consuming a leased key's quota); an unresolvable input or no key with quota comes
 * back as a ProblemDetails the form surfaces as a toast.
 */
export function AddChannelDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const createChannel = useCreateChannel();
  const [input, setInput] = useState("");

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    const value = input.trim();
    if (!value) return;
    try {
      const channel = await createChannel.mutateAsync({ input: value });
      toast.success(`Added ${channel.title}.`);
      setInput("");
      onOpenChange(false);
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !createChannel.isPending && onOpenChange(next)}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Add channel</DialogTitle>
          <DialogDescription>
            Paste a channel URL, an <span className="mono">@handle</span>, or a raw channel id.
          </DialogDescription>
        </DialogHeader>

        <form id="add-channel-form" onSubmit={onSubmit} className="flex flex-col gap-1.5">
          <Label htmlFor="channel-input">Channel</Label>
          <Input
            id="channel-input"
            autoFocus
            placeholder="https://youtube.com/@handle"
            value={input}
            onChange={(e) => setInput(e.target.value)}
          />
        </form>

        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={createChannel.isPending}
          >
            Cancel
          </Button>
          <Button type="submit" form="add-channel-form" disabled={createChannel.isPending || !input.trim()}>
            {createChannel.isPending ? "Resolving…" : "Add channel"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
