// Real backend DTOs returned by `/api/youtube-comments/*`. The host serializes with System.Text.Json
// Web defaults: camelCase property names and enums as their underlying NUMERIC value (there is no
// JsonStringEnumConverter). These replace the Phase-0 mock shapes for the read path.

/** `DashboardStatsDto` — a single KPI snapshot object (not an array). The quota figure is an
 * APPROXIMATION: estimated daily units (from each active mapping's cadence) against the single OAuth
 * project's ceiling — not metered actual usage. */
export interface DashboardStats {
  activeMappings: number;
  totalMappings: number;
  commentsToday: number;
  commentsLast24h: number;
  quotaCeiling: number;
  estimatedDailyUnits: number;
  estimatedPercent: number;
  errorsLast24h: number;
  connectedWorkspaces: number;
  channelCount: number;
}

/** `ActivityPoint` — one bucket of the activity timeline. `bucket` is the ISO bucket start (UTC hour for
 * 24h, UTC day for 7d/30d). `replies` is the reply subset of `forwarded`; `removed` is rejected on YouTube. */
export interface ActivityPoint {
  bucket: string;
  forwarded: number;
  replies: number;
  removed: number;
}

/** `ActivityTimelineDto` — `range` echoes the requested window ("24h" | "7d" | "30d"); `points` is 0-filled. */
export interface ActivityTimeline {
  range: string;
  points: ActivityPoint[];
}

/** `ModeratorStat` — one Slack moderator's removed-comment counts across windows + all time. */
export interface ModeratorStat {
  name: string;
  slackUserId: string | null;
  removed24h: number;
  removed7d: number;
  removed30d: number;
  removedAllTime: number;
}

/** `ModerationStatsDto` — outcome split (rejected vs already-gone) + windowed totals + per-moderator leaderboard. */
export interface ModerationStats {
  totalRemoved: number;
  rejected: number;
  alreadyGone: number;
  removed24h: number;
  removed7d: number;
  removed30d: number;
  perModerator: ModeratorStat[];
}

/** `DeliveryHealthDto` — the durable retry queue's live backlog (dead-lettered rows are dropped, not kept). */
export interface DeliveryHealth {
  pending: number;
  failing: number;
  nearGiveUp: number;
  oldestPendingAt: string | null;
}

/** `MappingHealthDto` — per-mapping freshness + recent throughput. */
export interface MappingHealth {
  mappingId: string;
  channelTitle: string;
  slackChannelName: string;
  isActive: boolean;
  frequencyMinutes: number;
  lastPolledAt: string | null;
  lastError: string | null;
  forwarded24h: number;
}

/** `CommentsHealthDto` — the operational-health panel: delivery backlog + a row per mapping. */
export interface CommentsHealth {
  delivery: DeliveryHealth;
  mappings: MappingHealth[];
}

/** `EngagementSummary` — headline figures over the engagement-enriched subset. `captured` is how many
 * forwarded comments carry the snapshot (rows from before capture are excluded); `avgForwardLatencySeconds`
 * is null when nothing enriched is in range. */
export interface EngagementSummary {
  captured: number;
  avgLikes: number;
  avgCommentLength: number;
  avgForwardLatencySeconds: number | null;
}

/** `VideoStat` — one video's forwarded volume + total likes. `videoTitle` is null until an enriched row lands. */
export interface VideoStat {
  videoId: string;
  videoTitle: string | null;
  forwarded: number;
  likes: number;
}

/** `AuthorStat` — one author's forwarded volume + total likes (grouped by channel-URL identity). */
export interface AuthorStat {
  name: string;
  authorChannelUrl: string | null;
  forwarded: number;
  likes: number;
}

/** `EngagementDto` — engagement analytics: headline summary, busiest videos, most-forwarded authors. */
export interface Engagement {
  summary: EngagementSummary;
  perVideo: VideoStat[];
  topAuthors: AuthorStat[];
}

/** `HistoryPoint` — one rolled-up day. `date` is an ISO date ("yyyy-MM-dd"). */
export interface HistoryPoint {
  date: string;
  forwarded: number;
  replies: number;
  removed: number;
}

/** `HistoryDto` — long-horizon daily history from the durable nightly rollup (survives retention trim). */
export interface History {
  days: number;
  points: HistoryPoint[];
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

const REPLY_SCAN_FREQUENCY_LABEL: Record<number, string> = {
  0: "Off",
  60: "Hourly",
  360: "Every 6 hr",
  1440: "Daily",
};

/** Human label for a reply-sweep cadence, e.g. `0 → "Off"`, `1440 → "Daily"`. */
export function replyScanFrequencyLabel(freq: number): string {
  return REPLY_SCAN_FREQUENCY_LABEL[freq] ?? `${freq} min`;
}

/** Polling-cadence options for the mapping form, ordered fastest → slowest. */
export const POLLING_FREQUENCY_OPTIONS: { value: PollingFrequency; label: string }[] = [
  { value: 1, label: "Every minute" },
  { value: 5, label: "Every 5 minutes" },
  { value: 15, label: "Every 15 minutes" },
  { value: 30, label: "Every 30 minutes" },
  { value: 60, label: "Every hour" },
  { value: 360, label: "Every 6 hours" },
];

/** Reply-sweep cadence options for the mapping form. */
export const REPLY_SCAN_FREQUENCY_OPTIONS: { value: ReplyScanFrequency; label: string }[] = [
  { value: 0, label: "Off" },
  { value: 60, label: "Hourly" },
  { value: 360, label: "Every 6 hours" },
  { value: 1440, label: "Daily" },
];

// ── Write-path DTOs (1:1 with the backend records under /api/youtube-comments/*) ──
// The host serializes with System.Text.Json Web defaults: camelCase keys and enums as their NUMERIC
// value. The MutationContractTests on the backend lock these shapes so the buttons can't silently break.

/** `ConnectedChannelOption` — one of the operator's own channels that CAN be monitored: a connected
 * Google account owns it and has granted the comment-management (force-ssl) scope. Empty list ⇒ the
 * honest "connect Google to enable monitoring" gated state. */
export interface ConnectedChannelOption {
  youTubeChannelId: string;
  title: string;
  thumbnailUrl: string | null;
  alreadyTracked: boolean;
}

/** `AddChannelRequest` — track one of the operator's connected channels by its channel id. */
export interface AddChannelInput {
  youTubeChannelId: string;
}

/** `ChannelOption` / `SlackChannelOption` / `MappingOptionsDto` — the pickers for the mapping form. */
export interface ChannelOption {
  id: string;
  title: string;
}
export interface SlackChannelOption {
  id: string;
  name: string;
  workspaceName: string;
  isPrivate: boolean;
}
export interface MappingOptions {
  youTubeChannels: ChannelOption[];
  slackChannels: SlackChannelOption[];
}

/** `CreateMappingRequest` — link a tracked YouTube channel to a Slack channel with a polling cadence. */
export interface CreateMappingInput {
  youTubeChannelId: string;
  slackChannelId: string;
  frequency: PollingFrequency;
  includeReplies: boolean;
  replySweepFrequency: ReplyScanFrequency;
  replyWindowDays: number;
}

/** `UpdateMappingRequest` — partial update; only the provided fields are applied. */
export interface UpdateMappingInput {
  frequency?: PollingFrequency;
  isActive?: boolean;
  includeReplies?: boolean;
  replySweepFrequency?: ReplyScanFrequency;
  replyWindowDays?: number;
}
