import {
  CloudUpload,
  Home,
  Link2,
  MessageSquare,
  Moon,
  Plus,
  ScrollText,
  Settings,
} from "lucide-react";

import { SlackIcon } from "@/components/brand-icons";
import type { IconType, Platform } from "./platforms";

/**
 * The single source of truth for navigation. One config drives the sidebar,
 * the breadcrumbs and the ⌘K command palette. Route ids carry the backend
 * module mapping: `ytc-*` → youtube-comments, `ytu-*` → youtube-uploads.
 */
export type RouteId =
  | "overview"
  | "ytc-dashboard"
  | "ytc-mappings"
  | "ytu-queue"
  | "ytu-history"
  | "ytu-mappings"
  | "ytu-settings"
  | "conn-slack"
  | "conn-google"
  | "logs"
  | "settings";

export type ModuleId = "youtube-comments" | "youtube-uploads" | null;

type LabelFn = (p: Platform) => string;
type IconFn = (p: Platform) => IconType;

interface LeafDef {
  id: RouteId;
  path: string;
  label: LabelFn;
  icon: IconFn;
  cmdIcon: IconFn;
  cmdGroup: "Pages" | "Connections" | "System";
  cmdLabel: LabelFn;
  module: ModuleId;
}

interface ToolDef {
  kind: "tool";
  id: string;
  label: LabelFn;
  icon: IconType;
  defaultOpen?: boolean;
  children: LeafDef[];
}

interface GroupDef {
  kind: "group";
  label: string;
  entries: Array<ToolDef | LeafDef>;
}

const overview: LeafDef = {
  id: "overview",
  path: "/",
  label: () => "Overview",
  icon: () => Home,
  cmdIcon: () => Home,
  cmdGroup: "Pages",
  cmdLabel: () => "Overview",
  module: null,
};

const commentsTool: ToolDef = {
  kind: "tool",
  id: "tool-comments",
  label: (p) => p.comments,
  icon: MessageSquare,
  children: [
    { id: "ytc-dashboard", path: "/comments", label: () => "Dashboard", icon: () => MessageSquare, cmdIcon: () => MessageSquare, cmdGroup: "Pages", cmdLabel: (p) => `${p.comments} · Dashboard`, module: "youtube-comments" },
    { id: "ytc-mappings", path: "/comments/mappings", label: () => "Mappings", icon: () => Link2, cmdIcon: () => Link2, cmdGroup: "Pages", cmdLabel: (p) => `${p.comments} · Mappings`, module: "youtube-comments" },
  ],
};

const uploadsTool: ToolDef = {
  kind: "tool",
  id: "tool-uploads",
  label: (p) => p.uploads,
  icon: CloudUpload,
  children: [
    { id: "ytu-queue", path: "/uploads/queue", label: () => "Queue", icon: () => CloudUpload, cmdIcon: () => CloudUpload, cmdGroup: "Pages", cmdLabel: (p) => `${p.uploads} · Queue`, module: "youtube-uploads" },
    { id: "ytu-history", path: "/uploads/history", label: () => "History", icon: () => ScrollText, cmdIcon: () => ScrollText, cmdGroup: "Pages", cmdLabel: (p) => `${p.uploads} · History`, module: "youtube-uploads" },
    { id: "ytu-mappings", path: "/uploads/mappings", label: () => "Mappings", icon: () => Link2, cmdIcon: () => Link2, cmdGroup: "Pages", cmdLabel: (p) => `${p.uploads} · Mappings`, module: "youtube-uploads" },
    { id: "ytu-settings", path: "/uploads/settings", label: () => "Settings", icon: () => Settings, cmdIcon: () => Settings, cmdGroup: "Pages", cmdLabel: (p) => `${p.uploads} · Settings`, module: "youtube-uploads" },
  ],
};

const slackLeaf: LeafDef = { id: "conn-slack", path: "/connections/slack", label: () => "Slack workspaces", icon: () => SlackIcon, cmdIcon: () => SlackIcon, cmdGroup: "Connections", cmdLabel: () => "Slack workspaces", module: null };
const googleLeaf: LeafDef = { id: "conn-google", path: "/connections/google", label: (p) => p.account, icon: (p) => p.icon, cmdIcon: (p) => p.icon, cmdGroup: "Connections", cmdLabel: (p) => p.account, module: null };

const logsLeaf: LeafDef = { id: "logs", path: "/system/logs", label: () => "Logs", icon: () => ScrollText, cmdIcon: () => ScrollText, cmdGroup: "System", cmdLabel: () => "Logs", module: null };
const settingsLeaf: LeafDef = { id: "settings", path: "/system/settings", label: () => "Settings", icon: () => Settings, cmdIcon: () => Settings, cmdGroup: "System", cmdLabel: () => "Settings", module: null };

const GROUPS: GroupDef[] = [
  { kind: "group", label: "Tools", entries: [commentsTool, uploadsTool] },
  { kind: "group", label: "Connections", entries: [slackLeaf, googleLeaf] },
  { kind: "group", label: "System", entries: [logsLeaf, settingsLeaf] },
];

/** Flat list of every leaf, for path/route lookups. */
const ALL_LEAVES: LeafDef[] = [
  overview,
  ...commentsTool.children,
  ...uploadsTool.children,
  slackLeaf,
  googleLeaf,
  logsLeaf,
  settingsLeaf,
];

export const ROUTE_PATH: Record<RouteId, string> = ALL_LEAVES.reduce(
  (acc, l) => ({ ...acc, [l.id]: l.path }),
  {} as Record<RouteId, string>,
);

export function pathToRouteId(pathname: string): RouteId | null {
  // Exact match first, then the longest matching prefix (nested routes).
  const exact = ALL_LEAVES.find((l) => l.path === pathname);
  if (exact) return exact.id;
  const prefix = [...ALL_LEAVES]
    .filter((l) => l.path !== "/" && pathname.startsWith(l.path))
    .sort((a, b) => b.path.length - a.path.length)[0];
  return prefix ? prefix.id : null;
}

export function moduleForRoute(id: RouteId): ModuleId {
  return ALL_LEAVES.find((l) => l.id === id)?.module ?? null;
}

// ── Sidebar model ──────────────────────────────────────────────────────────
export interface SidebarLeaf {
  id: RouteId;
  label: string;
  path: string;
  icon: IconType;
}
export interface SidebarTool {
  id: string;
  label: string;
  icon: IconType;
  defaultOpen: boolean;
  children: SidebarLeaf[];
}
export type SidebarEntry =
  | { type: "tool"; tool: SidebarTool }
  | { type: "item"; leaf: SidebarLeaf };
export interface SidebarGroupModel {
  label: string;
  entries: SidebarEntry[];
}
export interface SidebarModel {
  top: SidebarLeaf;
  groups: SidebarGroupModel[];
}

function resolveLeaf(l: LeafDef, p: Platform): SidebarLeaf {
  return { id: l.id, label: l.label(p), path: l.path, icon: l.icon(p) };
}

export function buildSidebar(p: Platform): SidebarModel {
  return {
    top: resolveLeaf(overview, p),
    groups: GROUPS.map((g) => ({
      label: g.label,
      entries: g.entries.map((entry): SidebarEntry =>
        "kind" in entry && entry.kind === "tool"
          ? {
              type: "tool",
              tool: {
                id: entry.id,
                label: entry.label(p),
                icon: entry.icon,
                defaultOpen: entry.defaultOpen ?? false,
                children: entry.children.map((c) => resolveLeaf(c, p)),
              },
            }
          : { type: "item", leaf: resolveLeaf(entry as LeafDef, p) },
      ),
    })),
  };
}

// ── Breadcrumbs ────────────────────────────────────────────────────────────
export function buildBreadcrumbs(id: RouteId, p: Platform): string[] {
  if (id === "overview") return ["Overview"];
  for (const tool of [commentsTool, uploadsTool]) {
    const child = tool.children.find((c) => c.id === id);
    if (child) return [tool.label(p), child.label(p)];
  }
  for (const g of GROUPS) {
    const leaf = g.entries.find(
      (e): e is LeafDef => !("kind" in e) && (e as LeafDef).id === id,
    );
    if (leaf) return [g.label, leaf.label(p)];
  }
  return ["Overview"];
}

// ── Command palette ────────────────────────────────────────────────────────
export interface CommandEntry {
  id: string;
  label: string;
  group: string;
  icon: IconType;
  /** Navigation target, or undefined for non-navigation actions. */
  path?: string;
  action?: "toggle-theme";
}

export function buildCommandItems(p: Platform): CommandEntry[] {
  return ALL_LEAVES.map((l) => ({
    id: l.id,
    label: l.cmdLabel(p),
    group: l.cmdGroup,
    icon: l.cmdIcon(p),
    path: l.path,
  }));
}

export function buildCommandActions(): CommandEntry[] {
  return [
    { id: "act-upload", label: "Queue an upload", group: "Actions", icon: Plus, path: ROUTE_PATH["ytu-queue"] },
    { id: "act-mapping", label: "Add comment mapping", group: "Actions", icon: MessageSquare, path: ROUTE_PATH["ytc-mappings"] },
    { id: "act-theme", label: "Toggle theme", group: "Actions", icon: Moon, action: "toggle-theme" },
  ];
}
