"use client";

import {
  Activity,
  CircleCheck,
  CircleX,
  Download,
  MessageSquare,
  Plug,
  Search,
  Settings,
  TriangleAlert,
} from "lucide-react";
import { type ComponentType, useState } from "react";

import { StatusDot } from "@/components/status";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { PageHeading } from "@/components/page-heading";
import { type LogEntry, type LogLevel } from "@/lib/mock-data";
import { cn } from "@/lib/utils";
import { useLogs } from "@/features/system/hooks";

type LevelFilter = LogLevel | "all";
type ToolFilter = LogEntry["tool"] | "all";

const LEVEL_META: Record<
  LogLevel,
  { icon: ComponentType<{ className?: string }>; color: string }
> = {
  error: { icon: CircleX, color: "text-danger" },
  warn: { icon: TriangleAlert, color: "text-warn" },
  success: { icon: CircleCheck, color: "text-ok" },
  info: { icon: Activity, color: "text-muted-foreground" },
};

const TOOL_META: Record<
  LogEntry["tool"],
  { label: string; icon: ComponentType<{ className?: string }> }
> = {
  comments: { label: "YT Comments", icon: MessageSquare },
  uploads: { label: "YT Uploads", icon: Plug },
  connections: { label: "Connections", icon: Plug },
  system: { label: "System", icon: Settings },
};

const TOOL_OPTIONS: { value: ToolFilter; label: string }[] = [
  { value: "all", label: "All tools" },
  { value: "comments", label: "YouTube Comments" },
  { value: "uploads", label: "YouTube Uploads" },
  { value: "connections", label: "Connections" },
  { value: "system", label: "System" },
];

const LEVEL_OPTIONS: { value: LevelFilter; label: string }[] = [
  { value: "all", label: "All levels" },
  { value: "error", label: "Errors" },
  { value: "warn", label: "Warnings" },
  { value: "success", label: "Success" },
  { value: "info", label: "Info" },
];

export default function LogsPage() {
  const { data } = useLogs();
  const [query, setQuery] = useState("");
  const [tool, setTool] = useState<ToolFilter>("all");
  const [level, setLevel] = useState<LevelFilter>("all");

  const logs = data ?? [];
  const q = query.trim().toLowerCase();
  const shown = logs.filter((l) => {
    if (tool !== "all" && l.tool !== tool) return false;
    if (level !== "all" && l.level !== level) return false;
    if (
      q &&
      !l.message.toLowerCase().includes(q) &&
      !l.target.toLowerCase().includes(q)
    )
      return false;
    return true;
  });

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Logs"
        description="One unified event stream across both tools and connections."
        actions={
          <>
            <div className="mr-1 flex items-center gap-[7px] text-[12.5px] text-muted-foreground">
              <StatusDot tone="ok" pulse />
              Streaming
            </div>
            <Button variant="outline" size="sm">
              <Download className="size-3.5" />
              Export
            </Button>
          </>
        }
      />

      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-2.5">
        <div className="relative w-[260px] max-w-full">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            className="pl-9"
            placeholder="Search log messages…"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
        </div>
        <Select value={tool} onValueChange={(v) => setTool(v as ToolFilter)}>
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {TOOL_OPTIONS.map((o) => (
              <SelectItem key={o.value} value={o.value}>
                {o.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex-1" />
        <Select value={level} onValueChange={(v) => setLevel(v as LevelFilter)}>
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {LEVEL_OPTIONS.map((o) => (
              <SelectItem key={o.value} value={o.value}>
                {o.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {/* Log list */}
      <Card className="overflow-hidden p-0">
        {shown.length === 0 ? (
          <div className="px-4 py-12 text-center text-[13.5px] text-muted-foreground">
            No log entries match these filters.
          </div>
        ) : (
          shown.map((l, i) => (
            <LogRow key={l.id} log={l} last={i === shown.length - 1} />
          ))
        )}
      </Card>

      {shown.length > 0 && (
        <div className="text-center text-[12.5px] text-muted-foreground">
          Showing {shown.length} of {logs.length} recent events
        </div>
      )}
    </div>
  );
}

function LogRow({ log, last }: { log: LogEntry; last: boolean }) {
  const lm = LEVEL_META[log.level];
  const tm = TOOL_META[log.tool];
  const LevelIcon = lm.icon;
  const ToolIcon = tm.icon;
  return (
    <div
      className={cn(
        "flex items-center gap-[13px] px-4 py-3 transition-colors hover:bg-accent/55",
        !last && "border-b",
      )}
    >
      <span className="mono w-16 shrink-0 text-[12px] text-muted-foreground">
        {log.time}
      </span>
      <div className="flex w-[18px] shrink-0 justify-center">
        <LevelIcon className={cn("size-[15px]", lm.color)} />
      </div>
      <div className="min-w-0 flex-1">
        <span className="text-[13.5px] text-foreground">{log.message}</span>
        {log.target && (
          <span className="mono ml-2 text-[12px] text-muted-foreground">
            {log.target}
          </span>
        )}
      </div>
      <span className="hidden shrink-0 items-center gap-1.5 text-[11.5px] text-muted-foreground sm:inline-flex">
        <ToolIcon className="size-[13px]" />
        {tm.label}
      </span>
      <span className="mono w-16 shrink-0 text-right text-[11.5px] text-muted-foreground">
        {log.ago}
      </span>
    </div>
  );
}
