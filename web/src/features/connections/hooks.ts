"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { api } from "@/lib/api/client";

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

/** A Google Cloud OAuth project (client id/secret). Connecting an account requires one. */
export interface GoogleProject {
  id: string;
  label: string;
  status: string;
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
  status: string;
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
      return list.map((p): GoogleProject => ({ id: p.id, label: p.label, status: p.status }));
    },
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
