/**
 * Phase 0 mock data — a faithful port of the design prototype's `window.DATA`.
 * Pages render from this until the real BFF/modules land in later phases.
 */

export type StatusTone = "neutral" | "info" | "ok" | "warn" | "danger";
export type JobStatus =
  | "queued"
  | "downloading"
  | "uploading"
  | "processing"
  | "done"
  | "failed"
  | "canceled";
export type Severity = "danger" | "warn" | "neutral";
export type LogLevel = "error" | "warn" | "info" | "success";
export type Privacy = "Public" | "Unlisted" | "Private" | "—";

export interface StatItem {
  id: string;
  label: string;
  value: string;
  sub: string;
  trend: "up" | "down" | "flat";
  spark?: number[];
  quota?: { used: number; total: number };
}

export interface Job {
  id: string;
  title: string;
  status: JobStatus;
  progress: number;
  source: string;
  sizeMB: number;
  target: string;
  channel: string;
  account: string;
  elapsed: number;
  eta: number | null;
  by: string;
  videoUrl?: string;
  finishedAgo?: string;
  error?: string;
}

export interface CommentMapping {
  id: string;
  channel: string;
  channelId: string;
  slack: string;
  freq: string;
  active: boolean;
  fwd24: number;
}

export interface FeedComment {
  id: string;
  author: string;
  handle: string;
  text: string;
  video: string;
  ytChannel: string;
  slack: string;
  time: string;
  likes: number;
  flags: string[];
  held?: boolean;
}

export interface YtChannel {
  id: string;
  name: string;
  handle: string;
  subs: string;
  videos: number;
  mappings: number;
  fwd24: number;
  fwd7: number;
  status: "ok" | "idle";
  lastComment: string;
}

export interface UploadHistoryItem {
  id: string;
  title: string;
  account: string;
  slack: string;
  sizeMB: number;
  duration: number | null;
  privacy: Privacy;
  status: "done" | "failed" | "canceled";
  finished: string;
  by: string;
  views?: number;
  videoUrl?: string;
  error?: string;
}

export interface UploadMapping {
  id: string;
  slack: string;
  workspace: string;
  account: string;
  key: string;
  privacy: Privacy;
  playlist: string;
  active: boolean;
  up24: number;
}

export interface LogEntry {
  id: string;
  tool: "uploads" | "comments" | "connections" | "system";
  level: LogLevel;
  message: string;
  target: string;
  time: string;
  ago: string;
}

export const DATA = {
  stats: [
    { id: "mappings", label: "Active mappings", value: "12", sub: "+2 this week", trend: "up", spark: [6, 7, 7, 8, 9, 10, 10, 11, 12] },
    { id: "comments", label: "Comments forwarded · 24h", value: "1,284", sub: "+18.2%", trend: "up", spark: [40, 55, 48, 62, 70, 65, 88, 96, 110] },
    { id: "uploads", label: "Uploads · 24h", value: "37", sub: "-4.1%", trend: "down", spark: [9, 12, 8, 14, 11, 7, 10, 6, 8] },
    { id: "quota", label: "Quota used · today", value: "73%", sub: "7,300 / 10,000 units", trend: "flat", quota: { used: 7300, total: 10000 } },
  ] as StatItem[],

  commentsSeries: [18, 22, 15, 12, 9, 7, 11, 19, 34, 52, 61, 70, 66, 72, 81, 78, 69, 74, 88, 96, 90, 84, 77, 64],

  uploadsSeries: [
    { d: "May 25", v: 22 }, { d: "May 26", v: 18 }, { d: "May 27", v: 27 }, { d: "May 28", v: 31 },
    { d: "May 29", v: 24 }, { d: "May 30", v: 12 }, { d: "May 31", v: 9 }, { d: "Jun 1", v: 19 },
    { d: "Jun 2", v: 33 }, { d: "Jun 3", v: 28 }, { d: "Jun 4", v: 41 }, { d: "Jun 5", v: 36 },
    { d: "Jun 6", v: 30 }, { d: "Jun 7", v: 37 },
  ],

  jobs: [
    { id: "job_8f21a", title: "Q3 launch teaser — final cut.mp4", status: "uploading", progress: 64, source: "Drive · launch-teaser-final.mp4", sizeMB: 842, target: "Daniel’s Channel", channel: "#content-drops", account: "prod-yt-01", elapsed: 184, eta: 96, by: "Maya R." },
    { id: "job_7d05c", title: "Customer story — Northwind.mov", status: "downloading", progress: 28, source: "Drive · northwind-customer-story.mov", sizeMB: 1240, target: "Daniel’s Channel", channel: "#content-drops", account: "prod-yt-02", elapsed: 47, eta: 210, by: "Daniel C." },
    { id: "job_6b9e2", title: "Weekly standup recap 06-07.mp4", status: "queued", progress: 0, source: "Drive · standup-recap-0607.mp4", sizeMB: 318, target: "Side Project Co.", channel: "#team-uploads", account: "side-yt-01", elapsed: 0, eta: null, by: "Priya S." },
    { id: "job_5a3f8", title: "Tutorial — advanced filters.mp4", status: "done", progress: 100, source: "Drive · tutorial-advanced-filters.mp4", sizeMB: 564, target: "Daniel’s Channel", channel: "#content-drops", account: "prod-yt-01", elapsed: 263, eta: 0, by: "Daniel C.", videoUrl: "youtu.be/x7Kd9", finishedAgo: "8m ago" },
    { id: "job_4c1d7", title: "Promo cut — spring sale.mov", status: "failed", progress: 41, source: "Drive · promo-spring-sale.mov", sizeMB: 980, target: "Daniel’s Channel", channel: "#content-drops", account: "prod-yt-02", elapsed: 119, eta: null, by: "Maya R.", error: "YouTube API quota exceeded (key prod-yt-02)" },
  ] as Job[],

  mappings: [
    { id: "m1", channel: "Daniel’s Channel", channelId: "UC_x9k...3Qa", slack: "#yt-comments", freq: "1 min", active: true, fwd24: 312 },
    { id: "m2", channel: "Daniel’s Channel", channelId: "UC_x9k...3Qa", slack: "#yt-superfans", freq: "5 min", active: true, fwd24: 88 },
    { id: "m3", channel: "Side Project Co.", channelId: "UC_p2m...7Lz", slack: "#side-comments", freq: "15 min", active: false, fwd24: 0 },
    { id: "m4", channel: "Tutorials by Daniel", channelId: "UC_q4n...1Bd", slack: "#tut-feedback", freq: "5 min", active: true, fwd24: 47 },
  ] as CommentMapping[],

  commentStats: [
    { id: "c-fwd", label: "Forwarded · 24h", value: "447", sub: "+18.2%", trend: "up", spark: [40, 55, 48, 62, 70, 65, 88, 96, 110] },
    { id: "c-spam", label: "Spam filtered · 24h", value: "63", sub: "12% of total", trend: "flat", spark: [4, 6, 5, 8, 7, 9, 6, 8, 7] },
    { id: "c-latency", label: "Median forward latency", value: "8s", sub: "−3s", trend: "up", spark: [14, 12, 11, 13, 10, 9, 9, 8, 8] },
    { id: "c-chan", label: "Channels watched", value: "3", sub: "1 idle", trend: "flat" },
  ] as StatItem[],

  commentsFeed: [
    { id: "f1", author: "Marta Köhler", handle: "@martakohler", text: "This walkthrough finally made the export settings click for me. Could you do a follow-up on the color pipeline?", video: "Q2 product walkthrough", ytChannel: "Daniel’s Channel", slack: "#yt-comments", time: "2m ago", likes: 14, flags: ["question"] },
    { id: "f2", author: "devon_makes", handle: "@devon_makes", text: "🔥🔥 been waiting for this one. instant subscribe", video: "Behind the scenes vlog", ytChannel: "Daniel’s Channel", slack: "#yt-superfans", time: "9m ago", likes: 3, flags: ["top-fan"] },
    { id: "f3", author: "Anonymous", handle: "@user-7x2k9", text: "Check my channel for FREE editing presets!!! link in bio 👀💰", video: "Tutorial — advanced filters", ytChannel: "Tutorials by Daniel", slack: "#tut-feedback", time: "14m ago", likes: 0, flags: ["spam"], held: true },
    { id: "f4", author: "Priya Nair", handle: "@priyaedits", text: "At 4:32 — is that a LUT or are you grading manually? Looks incredible.", video: "Tutorial — advanced filters", ytChannel: "Tutorials by Daniel", slack: "#tut-feedback", time: "22m ago", likes: 28, flags: ["question", "top-fan"] },
    { id: "f5", author: "Tomás Rivera", handle: "@trivera", text: "Followed every step but my render keeps failing at the upload stage. Anyone else?", video: "Q2 product walkthrough", ytChannel: "Daniel’s Channel", slack: "#yt-comments", time: "38m ago", likes: 6, flags: ["question"] },
    { id: "f6", author: "lena.wood", handle: "@lenawood", text: "The pacing on this is so much better than the last one. Clear improvement!", video: "Weekly standup recap", ytChannel: "Daniel’s Channel", slack: "#yt-comments", time: "51m ago", likes: 11, flags: [] },
    { id: "f7", author: "Anonymous", handle: "@cryptoking_44", text: "DM me to double your subscribers in 24h guaranteed 🚀", video: "Behind the scenes vlog", ytChannel: "Daniel’s Channel", slack: "#yt-comments", time: "1h ago", likes: 0, flags: ["spam"], held: true },
    { id: "f8", author: "Sam Okafor", handle: "@samok", text: "Would love a written version of this as a blog post. Sharing with my team regardless 🙌", video: "Q2 product walkthrough", ytChannel: "Daniel’s Channel", slack: "#yt-superfans", time: "1h ago", likes: 19, flags: ["top-fan"] },
  ] as FeedComment[],

  ytChannels: [
    { id: "ch1", name: "Daniel’s Channel", handle: "@danielcole", subs: "184K", videos: 212, mappings: 2, fwd24: 400, fwd7: 2840, status: "ok", lastComment: "2m ago" },
    { id: "ch2", name: "Tutorials by Daniel", handle: "@danieltutorials", subs: "57.2K", videos: 88, mappings: 1, fwd24: 47, fwd7: 410, status: "ok", lastComment: "22m ago" },
    { id: "ch3", name: "Side Project Co.", handle: "@sideprojectco", subs: "3.1K", videos: 24, mappings: 1, fwd24: 0, fwd7: 0, status: "idle", lastComment: "3d ago" },
  ] as YtChannel[],

  uploadHistory: [
    { id: "h1", title: "Tutorial — advanced filters.mp4", account: "Daniel’s Channel", slack: "#content-drops", sizeMB: 564, duration: 263, privacy: "Private", status: "done", finished: "Jun 9, 14:21", by: "Daniel C.", views: 1240, videoUrl: "youtu.be/x7Kd9" },
    { id: "h2", title: "Promo cut — spring sale.mov", account: "Daniel’s Channel", slack: "#content-drops", sizeMB: 980, duration: null, privacy: "—", status: "failed", finished: "Jun 9, 13:58", by: "Maya R.", error: "Quota exceeded (prod-yt-02)" },
    { id: "h3", title: "Q2 product walkthrough.mp4", account: "Daniel’s Channel", slack: "#content-drops", sizeMB: 712, duration: 198, privacy: "Public", status: "done", finished: "Jun 9, 11:02", by: "Daniel C.", views: 8930, videoUrl: "youtu.be/p2Lm4" },
    { id: "h4", title: "Customer story — Eastpoint.mov", account: "Side Project Co.", slack: "#team-uploads", sizeMB: 1180, duration: 341, privacy: "Unlisted", status: "done", finished: "Jun 8, 17:44", by: "Priya S.", views: 312, videoUrl: "youtu.be/k9Qd2" },
    { id: "h5", title: "Behind the scenes vlog.mov", account: "Daniel’s Channel", slack: "#vlog-uploads", sizeMB: 842, duration: 224, privacy: "Private", status: "done", finished: "Jun 8, 09:15", by: "Daniel C.", views: 0, videoUrl: "youtu.be/v3Nb8" },
    { id: "h6", title: "Weekly standup recap 06-06.mp4", account: "Daniel’s Channel", slack: "#content-drops", sizeMB: 318, duration: 142, privacy: "Private", status: "done", finished: "Jun 6, 16:30", by: "Priya S.", views: 24, videoUrl: "youtu.be/r5Wc1" },
    { id: "h7", title: "Feature teaser — filters v2.mp4", account: "Tutorials by Daniel", slack: "#tut-uploads", sizeMB: 446, duration: null, privacy: "—", status: "canceled", finished: "Jun 6, 10:08", by: "Maya R." },
  ] as UploadHistoryItem[],

  uploadMappings: [
    { id: "u1", slack: "#content-drops", workspace: "Daniel’s Team", account: "Daniel’s Channel", key: "prod-yt-01", privacy: "Private", playlist: "Drafts", active: true, up24: 12 },
    { id: "u2", slack: "#vlog-uploads", workspace: "Daniel’s Team", account: "Daniel’s Channel", key: "prod-yt-01", privacy: "Private", playlist: "Vlogs", active: true, up24: 3 },
    { id: "u3", slack: "#tut-uploads", workspace: "Daniel’s Team", account: "Tutorials by Daniel", key: "prod-yt-02", privacy: "Unlisted", playlist: "—", active: true, up24: 1 },
    { id: "u4", slack: "#team-uploads", workspace: "Side Project Co.", account: "Side Project Co.", key: "side-yt-01", privacy: "Unlisted", playlist: "Customer stories", active: false, up24: 0 },
  ] as UploadMapping[],

};

export type AppData = typeof DATA;
