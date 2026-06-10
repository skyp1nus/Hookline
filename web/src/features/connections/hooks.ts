"use client";

import { useQuery } from "@tanstack/react-query";

import { mockFetch } from "@/lib/mock-fetch";

/** A connected Slack workspace — shaped for the connection card. */
export interface SlackWorkspace {
  id: string;
  name: string;
  handle: string;
  meta: string;
}

/** A connected Google account authorized to upload to YouTube. */
export interface GoogleAccount {
  id: string;
  name: string;
  handle: string;
  meta: string;
}

/** Slack-specific detail beyond DATA.health (which only knows the 2/2 count). */
const SLACK_WORKSPACES: SlackWorkspace[] = [
  {
    id: "ws-daniels-team",
    name: "Daniel's Team",
    handle: "daniels-team.slack.com",
    meta: "5 channels mapped · OAuth valid",
  },
  {
    id: "ws-side-project",
    name: "Side Project Co.",
    handle: "sideproject.slack.com",
    meta: "2 channels mapped · OAuth valid",
  },
];

/** Google accounts authorized to upload to YouTube — richer than DATA.ytChannels. */
const GOOGLE_ACCOUNTS: GoogleAccount[] = [
  {
    id: "acct-daniels-channel",
    name: "Daniel's Channel",
    handle: "daniel@hookline.io",
    meta: "2 keys · scope: youtube.upload",
  },
  {
    id: "acct-tutorials",
    name: "Tutorials by Daniel",
    handle: "tutorials@hookline.io",
    meta: "1 key · scope: youtube.upload",
  },
  {
    id: "acct-side-project",
    name: "Side Project Co.",
    handle: "team@sideproject.co",
    meta: "1 key · scope: youtube.upload",
  },
];

export function useSlackWorkspaces() {
  return useQuery({
    queryKey: ["connections", "slack"],
    // Phase 1: queryFn = () => api.get<SlackWorkspace[]>("/connections/slack")
    queryFn: () => mockFetch<SlackWorkspace[]>(SLACK_WORKSPACES),
  });
}

export function useGoogleAccounts() {
  return useQuery({
    queryKey: ["connections", "google"],
    // Phase 1: queryFn = () => api.get<GoogleAccount[]>("/connections/google")
    queryFn: () => mockFetch<GoogleAccount[]>(GOOGLE_ACCOUNTS),
  });
}
