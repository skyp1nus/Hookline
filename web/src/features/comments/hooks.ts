"use client";

import { useQuery } from "@tanstack/react-query";

import { mockFetch } from "@/lib/mock-fetch";
import {
  DATA,
  type CommentMapping,
  type FeedComment,
  type StatItem,
  type YtChannel,
} from "@/lib/mock-data";

export function useCommentStats() {
  return useQuery({
    queryKey: ["comments", "stats"],
    // Phase 1: queryFn = () => api.get<StatItem[]>("/comment-bridge/stats")
    queryFn: () => mockFetch<StatItem[]>(DATA.commentStats),
  });
}

export function useCommentsFeed() {
  return useQuery({
    queryKey: ["comments", "feed"],
    // Phase 1: queryFn = () => api.get<FeedComment[]>("/comment-bridge/feed")
    queryFn: () => mockFetch<FeedComment[]>(DATA.commentsFeed),
  });
}

export function useChannels() {
  return useQuery({
    queryKey: ["comments", "channels"],
    // Phase 1: queryFn = () => api.get<YtChannel[]>("/comment-bridge/channels")
    queryFn: () => mockFetch<YtChannel[]>(DATA.ytChannels),
  });
}

export function useCommentMappings() {
  return useQuery({
    queryKey: ["comments", "mappings"],
    // Phase 1: queryFn = () => api.get<CommentMapping[]>("/comment-bridge/mappings")
    queryFn: () => mockFetch<CommentMapping[]>(DATA.mappings),
  });
}
