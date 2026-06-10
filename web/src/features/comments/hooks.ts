"use client";

import { useQuery } from "@tanstack/react-query";

import { api } from "@/lib/api/client";
import {
  type CommentMapping,
  type FeedComment,
  type StatItem,
  type YtChannel,
} from "@/lib/mock-data";

// All four reads now go through the BFF (`/api/*` → backend `/api/youtube-comments/*`). The query
// keys stay `["comments", …]` so the existing Phase-0 components keep working unchanged. Reconciling
// each backend DTO with the design shape is the live-credentialed-pipeline checkpoint (like Phase 1).

export function useCommentStats() {
  return useQuery({
    queryKey: ["comments", "stats"],
    queryFn: () => api.get<StatItem[]>("/youtube-comments/dashboard/stats"),
    refetchInterval: 30_000,
  });
}

// Feed renders the 24h comments timeline (the backend has no per-comment feed endpoint). A true
// per-comment feed is a deferred ticket — out of scope for Phase 2.
export function useCommentsFeed() {
  return useQuery({
    queryKey: ["comments", "feed"],
    queryFn: () => api.get<FeedComment[]>("/youtube-comments/dashboard/comments-timeline"),
  });
}

export function useChannels() {
  return useQuery({
    queryKey: ["comments", "channels"],
    queryFn: () => api.get<YtChannel[]>("/youtube-comments/youtube/channels"),
  });
}

export function useCommentMappings() {
  return useQuery({
    queryKey: ["comments", "mappings"],
    queryFn: () => api.get<CommentMapping[]>("/youtube-comments/mappings"),
  });
}
