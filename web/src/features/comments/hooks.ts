"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/lib/api/client";

import {
  type AddChannelInput,
  type ApiKeyDto,
  type CommentsTimelinePoint,
  type CreateApiKeyInput,
  type CreateMappingInput,
  type DashboardStats,
  type MappingDto,
  type MappingOptions,
  type UpdateMappingInput,
  type YouTubeChannelDto,
} from "./types";

// All reads + writes go through the BFF (`/api/*` → backend `/api/youtube-comments/*`) and are typed to
// the real backend DTOs (see ./types). Query keys stay `["comments", …]`; mutations invalidate the keys
// whose data they change so the UI refetches. ProblemDetails surface as ApiError — components turn those
// into sonner toasts via `apiErrorMessage` after `mutateAsync`.

// ── reads ──

export function useCommentStats() {
  return useQuery({
    queryKey: ["comments", "stats"],
    queryFn: () => api.get<DashboardStats>("/youtube-comments/dashboard/stats"),
    refetchInterval: 30_000,
  });
}

// The backend has no per-comment feed endpoint; the "feed" is the 24h comments-processed timeline
// (24 hourly buckets). A true per-comment feed is a deferred ticket — out of scope for Phase 2.
export function useCommentsTimeline() {
  return useQuery({
    queryKey: ["comments", "timeline"],
    queryFn: () => api.get<CommentsTimelinePoint[]>("/youtube-comments/dashboard/comments-timeline"),
  });
}

export function useChannels() {
  return useQuery({
    queryKey: ["comments", "channels"],
    queryFn: () => api.get<YouTubeChannelDto[]>("/youtube-comments/youtube/channels"),
  });
}

export function useCommentMappings() {
  return useQuery({
    queryKey: ["comments", "mappings"],
    queryFn: () => api.get<MappingDto[]>("/youtube-comments/mappings"),
  });
}

/** The selectable YouTube + Slack endpoints for the mapping form. */
export function useMappingOptions(enabled = true) {
  return useQuery({
    queryKey: ["comments", "mapping-options"],
    queryFn: () => api.get<MappingOptions>("/youtube-comments/mappings/options"),
    enabled,
  });
}

/** Stored YouTube Data API keys with today's quota usage. */
export function useApiKeys(enabled = true) {
  return useQuery({
    queryKey: ["comments", "keys"],
    queryFn: () => api.get<ApiKeyDto[]>("/youtube-comments/keys"),
    enabled,
  });
}

/**
 * Re-sync the Slack channel caches for all active workspaces. The mapping picker reads a module-local cache
 * that is otherwise only filled on the module's own Slack OAuth connect — but Slack is connected through the
 * shared Connections area, which never touches it. The Add-mapping dialog fires this on open; the options
 * query reads the freshened cache once this settles. Best-effort on the backend.
 */
export function useRefreshSlackChannels() {
  return useMutation({
    mutationFn: () => api.post("/youtube-comments/slack/refresh-channels"),
  });
}

// ── mappings (create / edit / toggle / delete) ──

export function useCreateMapping() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateMappingInput) => api.post<MappingDto>("/youtube-comments/mappings", body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["comments", "mappings"] });
      qc.invalidateQueries({ queryKey: ["comments", "channels"] });
      qc.invalidateQueries({ queryKey: ["comments", "stats"] });
    },
  });
}

export function useUpdateMapping() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateMappingInput }) =>
      api.patch<MappingDto>(`/youtube-comments/mappings/${id}`, body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["comments", "mappings"] });
      qc.invalidateQueries({ queryKey: ["comments", "stats"] });
    },
  });
}

export function useDeleteMapping() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.del(`/youtube-comments/mappings/${id}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["comments", "mappings"] });
      qc.invalidateQueries({ queryKey: ["comments", "channels"] });
      qc.invalidateQueries({ queryKey: ["comments", "stats"] });
    },
  });
}

// ── channels (add) ──
// The standalone Channels page was removed; the only entry point for tracking a channel is now the inline
// add-by-URL/@handle/id field in the comment mapping dialog, so creating a mapping still works end-to-end.

export function useCreateChannel() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: AddChannelInput) => api.post<YouTubeChannelDto>("/youtube-comments/youtube/channels", body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["comments", "channels"] });
      qc.invalidateQueries({ queryKey: ["comments", "mapping-options"] });
      qc.invalidateQueries({ queryKey: ["comments", "stats"] });
    },
  });
}

// ── API keys (add + validate-on-create / toggle / delete) ──

export function useCreateApiKey() {
  const qc = useQueryClient();
  return useMutation({
    // The backend validates against the YouTube API and returns 400 (never stores) for a bad key.
    mutationFn: (body: CreateApiKeyInput) => api.post<ApiKeyDto>("/youtube-comments/keys", body),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["comments", "keys"] });
      qc.invalidateQueries({ queryKey: ["comments", "stats"] });
    },
  });
}

export function useToggleApiKey() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.patch<ApiKeyDto>(`/youtube-comments/keys/${id}/toggle`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["comments", "keys"] });
      qc.invalidateQueries({ queryKey: ["comments", "stats"] });
    },
  });
}

export function useDeleteApiKey() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.del(`/youtube-comments/keys/${id}`),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["comments", "keys"] });
      qc.invalidateQueries({ queryKey: ["comments", "stats"] });
    },
  });
}
