"use client";

import { useQuery } from "@tanstack/react-query";

import { api } from "@/lib/api/client";

// ── Shapes returned by GET /api/overview (ASP.NET serializes camelCase, matching these field names) ──

export interface CommentsWindowCounts {
  forwarded: number;
  removed: number;
}

export interface CommentsChannelStat {
  channelTitle: string;
  forwarded: number;
  forwarded24h: number;
  forwarded7d: number;
  forwarded30d: number;
  removed24h: number;
  removed7d: number;
  removed30d: number;
}

export interface CommentsQuota {
  used: number;
  ceiling: number;
  percent: number;
}

export interface CommentsOverview {
  totalForwarded: number;
  window24h: CommentsWindowCounts;
  window7d: CommentsWindowCounts;
  window30d: CommentsWindowCounts;
  perChannel: CommentsChannelStat[];
  quota: CommentsQuota;
}

export interface UploadsWindowCounts {
  done: number;
  failed: number;
  canceled: number;
}

export interface UploadsAccountStat {
  accountTitle: string;
  total: number;
  done: number;
  failed: number;
  canceled: number;
}

export interface UploadsBucket {
  used: number;
  limit: number;
}

export interface UploadsOverview {
  totalUploads: number;
  window24h: UploadsWindowCounts;
  window7d: UploadsWindowCounts;
  window30d: UploadsWindowCounts;
  perAccount: UploadsAccountStat[];
  bucket: UploadsBucket;
}

export interface OverviewData {
  comments: CommentsOverview;
  uploads: UploadsOverview;
}

export function useOverview() {
  return useQuery({
    queryKey: ["overview"],
    queryFn: () => api.get<OverviewData>("/overview"),
  });
}
