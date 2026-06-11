"use client";

import { useQuery } from "@tanstack/react-query";

import { mockFetch } from "@/lib/mock-fetch";
import {
  DATA,
  type ActivityItem,
  type HealthItem,
  type Metric,
  type NeedsAttentionItem,
} from "@/lib/mock-data";

export interface OverviewData {
  needsAttention: NeedsAttentionItem[];
  metrics: Metric[];
  activity: ActivityItem[];
  health: HealthItem[];
}

export function useOverview() {
  return useQuery({
    queryKey: ["overview"],
    // Phase 1: queryFn = () => api.get<OverviewData>("/overview")
    queryFn: () =>
      mockFetch<OverviewData>({
        needsAttention: DATA.needsAttention,
        metrics: DATA.metrics,
        activity: DATA.activity,
        health: DATA.health,
      }),
  });
}
