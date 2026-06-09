import type { ComponentType } from "react";
import { Globe } from "lucide-react";

import { LinkedinIcon, YoutubeIcon } from "@/components/brand-icons";

/** A lucide icon or any compatible glyph (accepts `className` / `size`). */
export type IconType = ComponentType<{ className?: string; size?: number }>;

/**
 * Platforms drive the whole platform-first IA: the switcher recontexts the
 * Tools + Connections nav, the breadcrumbs and the ⌘K palette. Only YouTube is
 * live today; LinkedIn + Web are shown disabled ("Soon").
 */
export type PlatformId = "youtube" | "linkedin" | "web";

export interface Platform {
  id: PlatformId;
  name: string;
  abbr: string;
  handle: string;
  icon: IconType;
  /** Brand color for the switcher chip (fixed, not theme-driven). */
  color: string;
  soon?: boolean;
  /** Platform-specific labels the nav/breadcrumbs/palette recontext to. */
  comments: string;
  uploads: string;
  channels: string;
  account: string;
  keys: string;
}

export const PLATFORMS: Platform[] = [
  {
    id: "youtube",
    name: "YouTube",
    abbr: "YT",
    handle: "",
    icon: YoutubeIcon,
    color: "#FF0033",
    comments: "YouTube Comments",
    uploads: "YouTube Uploads",
    channels: "Channels",
    account: "Google / YouTube",
    keys: "YouTube API keys",
  },
  {
    id: "linkedin",
    name: "LinkedIn",
    abbr: "in",
    handle: "Coming soon",
    icon: LinkedinIcon,
    color: "#0A66C2",
    soon: true,
    comments: "LinkedIn Comments",
    uploads: "LinkedIn Posts",
    channels: "Pages",
    account: "LinkedIn account",
    keys: "LinkedIn API keys",
  },
  {
    id: "web",
    name: "Web",
    abbr: "W",
    handle: "Coming soon",
    icon: Globe,
    color: "#64748B",
    soon: true,
    comments: "Web Comments",
    uploads: "Web Publish",
    channels: "Sites",
    account: "Web account",
    keys: "Web API keys",
  },
];

export const DEFAULT_PLATFORM_ID: PlatformId = "youtube";

export function getPlatform(id: string | null | undefined): Platform {
  return PLATFORMS.find((p) => p.id === id) ?? PLATFORMS[0];
}
