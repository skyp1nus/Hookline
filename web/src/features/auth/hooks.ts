"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api, ApiError } from "@/lib/api/client";

export interface Me {
  id: string;
  email: string;
  role: string;
  isSystem: boolean;
}

export interface BootstrapState {
  ownerExists: boolean;
  userCount: number;
}

/** Current user, or null when unauthenticated (401 is not an error here). */
export function useMe() {
  return useQuery({
    queryKey: ["auth", "me"],
    queryFn: async () => {
      try {
        return await api.get<Me>("/auth/me");
      } catch (error) {
        if (error instanceof ApiError && error.status === 401) return null;
        throw error;
      }
    },
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
}

export function useBootstrapState() {
  return useQuery({
    queryKey: ["auth", "bootstrap-state"],
    queryFn: () => api.get<BootstrapState>("/auth/bootstrap-state"),
  });
}

export function useLogin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { email: string; password: string }) => api.post<Me>("/login", vars),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["auth"] }),
  });
}

export function useLogout() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: () => api.post<void>("/logout"),
    onSuccess: () => qc.clear(),
  });
}

export function useCreateOwner() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (vars: { email: string; password: string }) => api.post<Me>("/auth/owner", vars),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["auth"] }),
  });
}
