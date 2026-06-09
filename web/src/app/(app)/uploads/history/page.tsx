"use client";

import {
  Download,
  ExternalLink,
  FileVideo,
  Globe,
  Link2,
  Lock,
  MoreHorizontal,
  RefreshCw,
  ScrollText,
  Search,
  Trash2,
  X,
} from "lucide-react";
import type { ComponentType } from "react";
import { useMemo, useState } from "react";

import { SlackIcon, YoutubeIcon } from "@/components/brand-icons";
import { JOB_STATUS, StatusBadge } from "@/components/status";
import { PageHeading } from "@/components/page-heading";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { formatDuration, formatMB } from "@/lib/format";
import { DATA, type Privacy, type UploadHistoryItem } from "@/lib/mock-data";

const PRIVACY_ICON: Record<Privacy, ComponentType<{ className?: string }>> = {
  Public: Globe,
  Unlisted: Link2,
  Private: Lock,
  "—": X,
};

export default function HistoryPage() {
  const [query, setQuery] = useState("");
  const [acct, setAcct] = useState("all");
  const [status, setStatus] = useState("all");

  const accounts = useMemo(
    () => [...new Set(DATA.uploadHistory.map((h) => h.account))],
    [],
  );

  const shown = DATA.uploadHistory.filter((h) => {
    if (acct !== "all" && h.account !== acct) return false;
    if (status !== "all" && h.status !== status) return false;
    if (query && !h.title.toLowerCase().includes(query.toLowerCase())) return false;
    return true;
  });

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="History"
        description="Past uploads to YouTube, newest first."
        actions={
          <Button variant="outline" size="sm">
            <Download className="size-3.5" />
            Export CSV
          </Button>
        }
      />

      <Card className="gap-0 overflow-hidden p-0">
        {/* toolbar */}
        <div className="flex flex-wrap items-center gap-2.5 border-b p-3">
          <div className="relative w-[260px] max-w-full">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              className="h-7 pl-9 text-[13px]"
              placeholder="Search uploads…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          </div>
          <Select value={acct} onValueChange={setAcct}>
            <SelectTrigger size="sm" className="text-[13px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All accounts</SelectItem>
              {accounts.map((a) => (
                <SelectItem key={a} value={a}>
                  {a}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          <Select value={status} onValueChange={setStatus}>
            <SelectTrigger size="sm" className="text-[13px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All statuses</SelectItem>
              <SelectItem value="done">Done</SelectItem>
              <SelectItem value="failed">Failed</SelectItem>
              <SelectItem value="canceled">Canceled</SelectItem>
            </SelectContent>
          </Select>
        </div>

        {/* table */}
        <Table>
          <TableHeader>
            <TableRow className="hover:bg-transparent">
              <TableHead className="px-4 text-[12.5px] text-muted-foreground">Video</TableHead>
              <TableHead className="px-4 text-[12.5px] text-muted-foreground">
                Destination
              </TableHead>
              <TableHead className="px-4 text-right text-[12.5px] text-muted-foreground">
                Size
              </TableHead>
              <TableHead className="px-4 text-right text-[12.5px] text-muted-foreground">
                Length
              </TableHead>
              <TableHead className="px-4 text-[12.5px] text-muted-foreground">Privacy</TableHead>
              <TableHead className="px-4 text-[12.5px] text-muted-foreground">Status</TableHead>
              <TableHead className="px-4 text-[12.5px] text-muted-foreground">Finished</TableHead>
              <TableHead className="w-12 px-4" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {shown.length === 0 ? (
              <TableRow className="hover:bg-transparent">
                <TableCell
                  colSpan={8}
                  className="px-4 py-11 text-center text-[13.5px] text-muted-foreground"
                >
                  No uploads match your filters.
                </TableCell>
              </TableRow>
            ) : (
              shown.map((h) => <HistoryRow key={h.id} item={h} />)
            )}
          </TableBody>
        </Table>

        {/* pagination */}
        {shown.length > 0 && (
          <div className="flex items-center justify-between border-t p-3">
            <span className="text-[12.5px] text-muted-foreground">
              <span className="mono">{shown.length}</span> uploads
            </span>
            <div className="flex gap-2">
              <Button variant="outline" size="sm" disabled>
                Prev
              </Button>
              <Button variant="outline" size="sm" disabled>
                Next
              </Button>
            </div>
          </div>
        )}
      </Card>
    </div>
  );
}

function HistoryRow({ item }: { item: UploadHistoryItem }) {
  const st = JOB_STATUS[item.status] ?? { tone: "neutral" as const, label: item.status };
  const PrivacyIcon = PRIVACY_ICON[item.privacy];

  return (
    <TableRow>
      <TableCell className="px-4 py-[13px] align-middle text-[13.5px]">
        <div className="flex items-center gap-[11px]">
          <div className="flex h-7 w-10 shrink-0 items-center justify-center rounded-[5px] bg-muted text-muted-foreground">
            <FileVideo className="size-[15px]" />
          </div>
          <div className="min-w-0">
            <div className="max-w-[260px] truncate font-[540]">{item.title}</div>
            {item.status === "done" ? (
              <a
                href="#"
                className="mono text-[11.5px] text-primary no-underline"
                onClick={(e) => e.preventDefault()}
              >
                {item.videoUrl}
              </a>
            ) : item.error ? (
              <div className="text-[11.5px] text-danger">{item.error}</div>
            ) : (
              <div className="text-[11.5px] text-muted-foreground">by {item.by}</div>
            )}
          </div>
        </div>
      </TableCell>
      <TableCell className="px-4 py-[13px] align-middle">
        <div className="flex items-center gap-1.5 text-[13px]">
          <YoutubeIcon className="size-3.5 text-muted-foreground" />
          {item.account}
        </div>
        <div className="mt-0.5 flex items-center gap-1.5 text-[11.5px] text-muted-foreground">
          <SlackIcon className="size-3" />
          <span className="mono">{item.slack}</span>
        </div>
      </TableCell>
      <TableCell className="mono px-4 py-[13px] text-right align-middle text-[12.5px]">
        {formatMB(item.sizeMB)}
      </TableCell>
      <TableCell className="px-4 py-[13px] text-right align-middle">
        <span
          className={
            item.duration ? "mono text-[12.5px]" : "mono text-[12.5px] text-muted-foreground"
          }
        >
          {item.duration != null ? formatDuration(item.duration) : "—"}
        </span>
      </TableCell>
      <TableCell className="px-4 py-[13px] align-middle">
        {item.privacy === "—" ? (
          <span className="text-muted-foreground">—</span>
        ) : (
          <span className="inline-flex items-center gap-1.5 text-[12.5px]">
            <PrivacyIcon className="size-[13px] text-muted-foreground" />
            {item.privacy}
          </span>
        )}
      </TableCell>
      <TableCell className="px-4 py-[13px] align-middle">
        <StatusBadge tone={st.tone} dot>
          {st.label}
        </StatusBadge>
      </TableCell>
      <TableCell className="px-4 py-[13px] align-middle text-muted-foreground">
        <span className="mono text-[12.5px]">{item.finished}</span>
      </TableCell>
      <TableCell className="px-4 py-[13px] text-right align-middle">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon" className="size-[30px]">
              <MoreHorizontal className="size-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-48">
            {item.status === "done" && (
              <DropdownMenuItem>
                <ExternalLink className="size-4" />
                Open on YouTube
              </DropdownMenuItem>
            )}
            <DropdownMenuItem>
              <ScrollText className="size-4" />
              View job log
            </DropdownMenuItem>
            {item.status === "failed" && (
              <DropdownMenuItem>
                <RefreshCw className="size-4" />
                Retry upload
              </DropdownMenuItem>
            )}
            <DropdownMenuSeparator />
            <DropdownMenuItem variant="destructive">
              <Trash2 className="size-4" />
              Remove from history
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </TableCell>
    </TableRow>
  );
}
