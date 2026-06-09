"use client";

import { Search } from "lucide-react";
import { useMemo, useState } from "react";

import { PageHeading } from "@/components/page-heading";
import { StatusDot } from "@/components/status";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { DATA } from "@/lib/mock-data";
import { cn } from "@/lib/utils";

import { CommentCard } from "../_components/comment-card";

const FLAG_TABS: { key: string; label: string }[] = [
  { key: "all", label: "All" },
  { key: "question", label: "Questions" },
  { key: "top-fan", label: "Top fans" },
  { key: "held", label: "Held" },
];

export default function FeedPage() {
  const [query, setQuery] = useState("");
  const [chan, setChan] = useState("all");
  const [flag, setFlag] = useState("all");

  const channels = useMemo(() => [...new Set(DATA.commentsFeed.map((f) => f.ytChannel))], []);

  const shown = DATA.commentsFeed.filter((f) => {
    if (chan !== "all" && f.ytChannel !== chan) return false;
    if (flag === "held" && !f.held) return false;
    if (flag !== "all" && flag !== "held" && !f.flags.includes(flag)) return false;
    if (
      query &&
      !(
        f.text.toLowerCase().includes(query.toLowerCase()) ||
        f.author.toLowerCase().includes(query.toLowerCase())
      )
    )
      return false;
    return true;
  });

  return (
    <div className="flex flex-col gap-[18px]">
      <PageHeading
        title="Feed"
        description="Every comment forwarded to Slack, newest first."
        actions={
          <div className="flex items-center gap-[7px] text-[12.5px] text-muted-foreground">
            <StatusDot tone="ok" pulse />
            Live
          </div>
        }
      />

      {/* Toolbar */}
      <div className="flex flex-wrap items-center gap-2.5">
        <div className="relative w-[260px] max-w-full">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            className="pl-9"
            placeholder="Search comments or authors…"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
          />
        </div>
        <Select value={chan} onValueChange={setChan}>
          <SelectTrigger className="w-[180px]">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All channels</SelectItem>
            {channels.map((ch) => (
              <SelectItem key={ch} value={ch}>
                {ch}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <div className="flex gap-1 rounded-[calc(var(--radius)-1px)] bg-muted p-1">
          {FLAG_TABS.map((tab) => (
            <button
              key={tab.key}
              type="button"
              onClick={() => setFlag(tab.key)}
              className={cn(
                "h-7 rounded-[calc(var(--radius)-4px)] px-[11px] text-[12.5px] font-medium outline-none transition-colors focus-visible:ring-2 focus-visible:ring-ring",
                flag === tab.key
                  ? "bg-background text-foreground shadow-[var(--shadow-xs)]"
                  : "text-muted-foreground hover:text-foreground",
              )}
            >
              {tab.label}
            </button>
          ))}
        </div>
      </div>

      {shown.length === 0 ? (
        <div className="py-12 text-center text-[13.5px] text-muted-foreground">
          No comments match these filters.
        </div>
      ) : (
        <div className="flex flex-col gap-3">
          {shown.map((f) => (
            <CommentCard key={f.id} item={f} />
          ))}
        </div>
      )}
    </div>
  );
}
