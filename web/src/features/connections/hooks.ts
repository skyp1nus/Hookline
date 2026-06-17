"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api, ApiError } from "@/lib/api/client";

// Connected Slack workspaces + Google accounts live in the shared `connections` store. There is no
// tool-agnostic /api/connections/* endpoint — the YouTube Uploads module exposes the canonical list +
// disconnect routes (the Slack token store is shared across modules, so this list is authoritative). We
// map the richer backend DTOs down to the {id,name,handle,meta} shape the connection card renders.

/** A connected Slack workspace, shaped for the connection card. */
export interface SlackWorkspace {
  id: string;
  name: string;
  handle: string;
  meta: string;
  active: boolean;
}

/** A connected Google account authorized to upload to YouTube. */
export interface GoogleAccount {
  id: string;
  name: string;
  handle: string;
  meta: string;
  active: boolean;
}

/** Per-project YouTube Data API quota snapshot (governed per Google Cloud project). */
export interface GoogleProjectQuota {
  usedUploads: number;
  uploadLimit: number;
  remainingUploads: number;
  totalUploads: number;
  usedUnits: number;
  capUnits: number;
}

/** A Google Cloud OAuth project (client id/secret). Connecting an account requires one. */
export interface GoogleProject {
  id: string;
  label: string;
  /** OAuth client id — NOT a secret (it appears in the consent URL); rendered masked in the UI. */
  clientId: string;
  status: string;
  /** Accounts authorized through (bound to) this project. */
  accountCount: number;
  quota: GoogleProjectQuota;
  createdAt: string;
}

/** Fields for creating a Google Cloud project. The secret is write-only — never read back or cached. */
export interface CreateGoogleProjectInput {
  label: string;
  clientId: string;
  clientSecret: string;
}

// Raw backend DTOs (camelCase per System.Text.Json web defaults).
interface SlackWorkspaceDto {
  id: string;
  slackTeamId: string;
  teamName: string;
  isActive: boolean;
  channelCount: number;
}
interface GoogleAccountDto {
  id: string;
  label: string;
  youTubeChannelTitle: string | null;
  accountEmail: string | null;
  status: string;
  projectLabel: string | null;
}
interface GoogleProjectDto {
  id: string;
  label: string;
  clientId: string;
  status: string;
  createdAt: string;
  updatedAt: string;
  accountCount: number;
  quota: GoogleProjectQuota;
}

export function useSlackWorkspaces() {
  return useQuery({
    queryKey: ["connections", "slack"],
    queryFn: async () => {
      const list = await api.get<SlackWorkspaceDto[]>("/youtube-uploads/slack/workspaces");
      return list.map(
        (w): SlackWorkspace => ({
          id: w.id,
          name: w.teamName,
          handle: w.slackTeamId,
          meta: `${w.channelCount} ${w.channelCount === 1 ? "channel" : "channels"} cached${
            w.isActive ? "" : " · inactive"
          }`,
          active: w.isActive,
        }),
      );
    },
  });
}

// The Comments tool is a SEPARATE Slack app with its own bot, so its workspaces live under its own
// module endpoint (the shared store keys rows per (team, app)). Listing/connecting it here is what makes
// a Comments card post as the Comments bot — and routes its "Reject on YouTube" button back to Comments.
interface CommentsSlackWorkspaceDto {
  id: string;
  teamId: string;
  teamName: string;
  isActive: boolean;
  channelCount: number;
}

export function useCommentsSlackWorkspaces() {
  return useQuery({
    queryKey: ["connections", "slack-comments"],
    queryFn: async () => {
      const list = await api.get<CommentsSlackWorkspaceDto[]>("/youtube-comments/slack/workspaces");
      return list.map(
        (w): SlackWorkspace => ({
          id: w.id,
          name: w.teamName,
          handle: w.teamId,
          meta: `${w.channelCount} ${w.channelCount === 1 ? "channel" : "channels"} cached${
            w.isActive ? "" : " · inactive"
          }`,
          active: w.isActive,
        }),
      );
    },
  });
}

/** Disconnect a Comments-app Slack workspace (its own row in the shared store). */
export function useDisconnectCommentsWorkspace() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.del(`/youtube-comments/slack/workspaces/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["connections", "slack-comments"] }),
  });
}

export function useGoogleAccounts() {
  return useQuery({
    queryKey: ["connections", "google"],
    queryFn: async () => {
      const list = await api.get<GoogleAccountDto[]>("/youtube-uploads/google/accounts");
      return list.map(
        (a): GoogleAccount => ({
          id: a.id,
          name: a.youTubeChannelTitle ?? a.label,
          handle: a.accountEmail ?? a.label,
          meta: `${a.projectLabel ? `${a.projectLabel} · ` : ""}scope youtube.upload`,
          // Listed accounts are authorized; the store only keeps connected accounts.
          active: true,
        }),
      );
    },
  });
}

/** Google Cloud projects. A Google account can only be connected once at least one project exists. */
export function useGoogleProjects() {
  return useQuery({
    queryKey: ["connections", "google-projects"],
    queryFn: async () => {
      const list = await api.get<GoogleProjectDto[]>("/youtube-uploads/google/projects");
      return list.map(
        (p): GoogleProject => ({
          id: p.id,
          label: p.label,
          clientId: p.clientId,
          status: p.status,
          accountCount: p.accountCount,
          quota: p.quota,
          createdAt: p.createdAt,
        }),
      );
    },
  });
}

// Both keys move when a project changes: the projects list, and the accounts list (its meta shows the
// project label, and a deleted/disabled project changes which accounts can be connected/rotated).
const GOOGLE_PROJECT_KEYS = [
  ["connections", "google-projects"],
  ["connections", "google"],
] as const;

/**
 * Remap a backend error CODE to a human message, rethrowing so the mutation still rejects and the caller's
 * `onError`/catch toasts the friendly text. Responses with a ProblemDetails body surface their `error` as
 * the message; bodiless ones (e.g. `Results.NotFound()`) surface only the status text, so a 404 also
 * resolves to the well-known `not_found` key.
 */
function rethrowFriendly(error: unknown, map: Record<string, string>): never {
  if (error instanceof ApiError) {
    const code =
      error.message in map ? error.message : error.status === 404 ? "not_found" : undefined;
    if (code && code in map) {
      throw new ApiError(error.status, map[code], error.problem);
    }
  }
  throw error;
}

function useInvalidateGoogleProjects() {
  const qc = useQueryClient();
  return () => GOOGLE_PROJECT_KEYS.forEach((queryKey) => qc.invalidateQueries({ queryKey: [...queryKey] }));
}

/** Add a Google Cloud project (client id + write-only secret). */
export function useCreateGoogleProject() {
  const invalidate = useInvalidateGoogleProjects();
  return useMutation({
    mutationFn: async (input: CreateGoogleProjectInput) => {
      try {
        return await api.post<GoogleProjectDto>("/youtube-uploads/google/projects", input);
      } catch (error) {
        rethrowFriendly(error, {
          client_id_exists: "This client ID is already added.",
          client_id_and_secret_required: "Client ID and client secret are both required.",
        });
      }
    },
    onSuccess: invalidate,
  });
}

/** Rename a project (label) and/or flip its status (Active/Disabled). */
export function useUpdateGoogleProject() {
  const invalidate = useInvalidateGoogleProjects();
  return useMutation({
    mutationFn: async ({ id, label, status }: { id: string; label?: string; status?: string }) => {
      try {
        return await api.patch<void>(`/youtube-uploads/google/projects/${id}`, { label, status });
      } catch (error) {
        rethrowFriendly(error, { not_found: "This project no longer exists." });
      }
    },
    onSuccess: invalidate,
  });
}

/** Delete a project. The backend refuses (client_in_use) while any account is still bound to it. */
export function useDeleteGoogleProject() {
  const invalidate = useInvalidateGoogleProjects();
  return useMutation({
    mutationFn: async (id: string) => {
      try {
        return await api.del<void>(`/youtube-uploads/google/projects/${id}`);
      } catch (error) {
        rethrowFriendly(error, {
          client_in_use: "Disconnect this project's accounts first.",
          not_found: "This project no longer exists.",
        });
      }
    },
    onSuccess: invalidate,
  });
}

/** Disconnect a Slack workspace (DELETE on the shared store; cascades the module channel cache). */
export function useDisconnectWorkspace() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.del(`/youtube-uploads/slack/workspaces/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["connections", "slack"] }),
  });
}

/** Disconnect a Google account. The backend cascades: any channel mapping targeting it is dropped too. */
export function useDisconnectAccount() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.del(`/youtube-uploads/google/accounts/${id}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["connections", "google"] }),
  });
}
