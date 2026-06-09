"use client";

import { useQuery } from "@tanstack/react-query";

import { mockFetch } from "@/lib/mock-fetch";
import { DATA, type LogEntry, type TeamMember } from "@/lib/mock-data";

export function useLogs() {
  return useQuery({
    queryKey: ["system", "logs"],
    // Phase 1: queryFn = () => api.get<LogEntry[]>("/system/logs")
    queryFn: () => mockFetch<LogEntry[]>(DATA.unifiedLogs),
  });
}

export function useTeam() {
  return useQuery({
    queryKey: ["system", "team"],
    // Phase 1: queryFn = () => api.get<TeamMember[]>("/system/team")
    queryFn: () => mockFetch<TeamMember[]>(DATA.team),
  });
}
