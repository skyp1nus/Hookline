"use client";

import { useQuery } from "@tanstack/react-query";

import { api } from "@/lib/api/client";

import {
  type CommentsTimelinePoint,
  type DashboardStats,
  type MappingDto,
  type YouTubeChannelDto,
} from "./types";

// All four reads go through the BFF (`/api/*` → backend `/api/youtube-comments/*`) and are typed to
// the real backend DTOs (see ./types). The query keys stay `["comments", …]`.

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
