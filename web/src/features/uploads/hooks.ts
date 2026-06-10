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
