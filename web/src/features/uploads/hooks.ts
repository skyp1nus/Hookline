"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/lib/api/client";
import {
  type Job,
  type UploadHistoryItem,
  type UploadMapping,
} from "@/lib/mock-data";

export function useJobs() {
  return useQuery({
    queryKey: ["uploads", "jobs"],
    queryFn: () => api.get<Job[]>("/youtube-uploads/jobs"),
    // Re-sync the queue with real backend state; the card's 1s tick smooths between polls.
    refetchInterval: 4000,
  });
}

export function useUploadHistory() {
  return useQuery({
    queryKey: ["uploads", "history"],
    queryFn: () => api.get<UploadHistoryItem[]>("/youtube-uploads/upload-history"),
  });
}

export function useUploadMappings() {
  return useQuery({
    queryKey: ["uploads", "mappings"],
    queryFn: () => api.get<UploadMapping[]>("/youtube-uploads/upload-mappings"),
  });
}

/** Cancel a queued/downloading job (no-op past the point of no return — the backend enforces it). */
export function useCancelJob() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.post(`/youtube-uploads/jobs/${id}/cancel`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["uploads", "jobs"] }),
  });
}

// ── upload mappings: create / update (active toggle) / delete ──

/** A Slack channel pickable for a new upload route. Carries both ids the create endpoint needs. */
export interface UploadSlackChannelOption {
  slackChannelId: string;
  name: string;
  workspaceId: string;
  workspaceName: string;
  isPrivate: boolean;
}

/** A Google account pickable for a new upload route. */
export interface UploadGoogleAccountOption {
  id: string;
  label: string;
}

/** `CreateMappingDto` — link a Slack channel to a Google account. */
export interface CreateUploadMappingInput {
  slackWorkspaceId: string;
  slackChannelId: string;
  googleAccountId: string;
}

interface SlackChannelRaw {
  id: string;
  slackChannelId: string;
  name: string;
  isPrivate: boolean;
  workspaceId: string;
  workspaceName: string;
}
interface GoogleAccountRaw {
  id: string;
  label: string;
  youTubeChannelTitle: string | null;
}

/** Pickers for the Add-route dialog: member Slack channels + connected Google accounts. */
export function useUploadMappingOptions(enabled = true) {
  return useQuery({
    queryKey: ["uploads", "mapping-options"],
    enabled,
    queryFn: async () => {
      const [channels, accounts] = await Promise.all([
        api.get<SlackChannelRaw[]>("/youtube-uploads/slack/channels"),
        api.get<GoogleAccountRaw[]>("/youtube-uploads/google/accounts"),
      ]);
      return {
        slackChannels: channels.map(
          (c): UploadSlackChannelOption => ({
            slackChannelId: c.slackChannelId,
            name: c.name,
            workspaceId: c.workspaceId,
            workspaceName: c.workspaceName,
            isPrivate: c.isPrivate,
          }),
        ),
        googleAccounts: accounts.map(
          (a): UploadGoogleAccountOption => ({ id: a.id, label: a.youTubeChannelTitle ?? a.label }),
        ),
      };
    },
  });
}

/**
 * Re-sync the Slack channel caches for all active workspaces. The picker reads a module-local cache that is
 * otherwise only synced on Slack OAuth connect, so the Add-route dialog fires this on open to surface channels
 * created/joined since. Best-effort on the backend; the options query reads the freshened cache afterwards.
 */
export function useRefreshUploadSlackChannels() {
  return useMutation({
    mutationFn: () => api.post("/youtube-uploads/slack/refresh-channels"),
  });
}

export function useCreateUploadMapping() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateUploadMappingInput) => api.post("/youtube-uploads/mappings", body),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["uploads", "mappings"] }),
  });
}

/** Toggle a route active/paused (P0). Paused routes are skipped at ingest — the pipeline stops triggering. */
export function useUpdateUploadMapping() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, active }: { id: string; active: boolean }) =>
      api.patch(`/youtube-uploads/mappings/${id}`, { active }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["uploads", "mappings"] }),
  });
}

export function useDeleteUploadMapping() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.del(`/youtube-uploads/mappings/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["uploads", "mappings"] }),
  });
}
