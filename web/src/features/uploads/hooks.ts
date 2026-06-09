"use client";

import { useQuery } from "@tanstack/react-query";

import { mockFetch } from "@/lib/mock-fetch";
import {
  DATA,
  type Job,
  type UploadHistoryItem,
  type UploadMapping,
} from "@/lib/mock-data";

export function useJobs() {
  return useQuery({
    queryKey: ["uploads", "jobs"],
    // Phase 1: queryFn = () => api.get<Job[]>("/slacktube/jobs")
    queryFn: () => mockFetch<Job[]>(DATA.jobs),
  });
}

export function useUploadHistory() {
  return useQuery({
    queryKey: ["uploads", "history"],
    // Phase 1: queryFn = () => api.get<UploadHistoryItem[]>("/slacktube/upload-history")
    queryFn: () => mockFetch<UploadHistoryItem[]>(DATA.uploadHistory),
  });
}

export function useUploadMappings() {
  return useQuery({
    queryKey: ["uploads", "mappings"],
    // Phase 1: queryFn = () => api.get<UploadMapping[]>("/slacktube/upload-mappings")
    queryFn: () => mockFetch<UploadMapping[]>(DATA.uploadMappings),
  });
}
