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
import { useCreateApiKey } from "@/features/comments/hooks";

/**
 * Add a YouTube Data API key. The backend validates the key against YouTube before storing it — an invalid
 * key comes back 400 and is never persisted; the form surfaces that ProblemDetails as a toast.
 */
export function AddApiKeyDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const createKey = useCreateApiKey();
  const [name, setName] = useState("");
  const [apiKey, setApiKey] = useState("");

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (!name.trim() || !apiKey.trim()) return;
    try {
      await createKey.mutateAsync({ name: name.trim(), apiKey: apiKey.trim() });
      toast.success("API key added.");
      setName("");
      setApiKey("");
      onOpenChange(false);
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }

  return (
    <Dialog open={open} onOpenChange={(next) => !createKey.isPending && onOpenChange(next)}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Add API key</DialogTitle>
          <DialogDescription>
            A YouTube Data API key for comment polling. It&apos;s validated against YouTube before it&apos;s saved.
          </DialogDescription>
        </DialogHeader>

        <form id="add-key-form" onSubmit={onSubmit} className="flex flex-col gap-4">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="key-name">Name</Label>
            <Input
              id="key-name"
              autoFocus
              placeholder="e.g. Primary"
              value={name}
              onChange={(e) => setName(e.target.value)}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="key-value">API key</Label>
            <Input
              id="key-value"
              className="mono"
              placeholder="AIza…"
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
            />
          </div>
        </form>

        <DialogFooter>
          <Button
            type="button"
            variant="outline"
            onClick={() => onOpenChange(false)}
            disabled={createKey.isPending}
          >
            Cancel
          </Button>
          <Button
            type="submit"
            form="add-key-form"
            disabled={createKey.isPending || !name.trim() || !apiKey.trim()}
          >
            {createKey.isPending ? "Validating…" : "Add key"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
