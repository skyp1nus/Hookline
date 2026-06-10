// Real backend DTOs returned by `/api/youtube-comments/*`. The host serializes with System.Text.Json
// Web defaults: camelCase property names and enums as their underlying NUMERIC value (there is no
// JsonStringEnumConverter). These replace the Phase-0 mock shapes for the read path.

/** `DashboardStatsDto` — a single KPI snapshot object (not an array). */
export interface DashboardStats {
  activeMappings: number;
  totalMappings: number;
  commentsToday: number;
  commentsLast24h: number;
  totalQuotaLimit: number;
  totalQuotaUsedToday: number;
  quotaUsedPercent: number;
  errorsLast24h: number;
  connectedWorkspaces: number;
  apiKeyCount: number;
  channelCount: number;
}

/** `CommentsTimelinePoint` — one hour bucket of the 24h timeline. `bucket` is the ISO start of the UTC hour. */
export interface CommentsTimelinePoint {
  bucket: string;
  count: number;
}

/** `YouTubeChannelDto` — a tracked channel with the count of mappings targeting it. */
export interface YouTubeChannelDto {
  id: string;
  youTubeChannelId: string;
  title: string;
  thumbnailUrl: string | null;
  handle: string | null;
  addedAt: string;
  mappingCount: number;
}

// PollingFrequency / ReplyScanFrequency serialize as their underlying minute value.
export type PollingFrequency = 1 | 5 | 15 | 30 | 60 | 360;
export type ReplyScanFrequency = 0 | 60 | 360 | 1440;

/** `MappingDto` — a mapping flattened with its endpoint display names. */
export interface MappingDto {
  id: string;
  youTubeChannelId: string;
  youTubeChannelTitle: string;
  youTubeChannelThumbnailUrl: string | null;
  slackChannelId: string;
  slackChannelName: string;
  slackWorkspaceName: string;
  frequency: PollingFrequency;
  isActive: boolean;
  includeReplies: boolean;
  replySweepFrequency: ReplyScanFrequency;
  replyWindowDays: number;
  lastPolledAt: string | null;
  lastError: string | null;
  createdAt: string;
}

const POLLING_FREQUENCY_LABEL: Record<number, string> = {
  1: "1 min",
  5: "5 min",
  15: "15 min",
  30: "30 min",
  60: "1 hr",
  360: "6 hr",
};

/** Human label for a polling cadence, e.g. `5 → "5 min"`, `60 → "1 hr"`. */
export function pollingFrequencyLabel(freq: number): string {
  return POLLING_FREQUENCY_LABEL[freq] ?? `${freq} min`;
}
