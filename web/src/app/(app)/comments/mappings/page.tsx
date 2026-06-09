"use client";

import {
  ChevronRight,
  ExternalLink,
  MoreHorizontal,
  Pause,
  Play,
  Plus,
  Search,
  Settings,
  Trash2,
} from "lucide-react";
import { useState } from "react";

import { SlackIcon, YoutubeIcon } from "@/components/brand-icons";
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
import { Switch } from "@/components/ui/switch";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { DATA, type CommentMapping } from "@/lib/mock-data";
import { formatNumber } from "@/lib/format";

const FREQ_OPTIONS = ["1 min", "5 min", "15 min", "30 min", "1 hr"];

export default function MappingsPage() {
  const [rows, setRows] = useState<CommentMapping[]>(() => DATA.mappings.map((m) => ({ ...m })));
  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("all");

  const toggle = (id: string) =>
    setRows((prev) => prev.map((m) => (m.id === id ? { ...m, active: !m.active } : m)));
  const setFreq = (id: string, freq: string) =>
    setRows((prev) => prev.map((m) => (m.id === id ? { ...m, freq } : m)));

  const shown = rows.filter((m) => {
    if (status === "active" && !m.active) return false;
    if (status === "paused" && m.active) return false;
    if (
      query &&
      !(
        m.channel.toLowerCase().includes(query.toLowerCase()) ||
        m.slack.toLowerCase().includes(query.toLowerCase())
      )
    )
      return false;
    return true;
  });

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Mappings"
        description="Forward new comments from a YouTube channel into a Slack channel."
        actions={
          <Button size="sm">
            <Plus className="size-3.5" />
            Add mapping
          </Button>
        }
      />

      <Card className="overflow-hidden p-0">
        {/* Toolbar */}
        <div className="flex flex-wrap items-center gap-2.5 border-b p-3.5">
          <div className="relative w-[260px] max-w-full">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              className="pl-9"
              placeholder="Search mappings…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          </div>
          <Select value={status} onValueChange={setStatus}>
            <SelectTrigger className="w-[150px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All statuses</SelectItem>
              <SelectItem value="active">Active</SelectItem>
              <SelectItem value="paused">Paused</SelectItem>
            </SelectContent>
          </Select>
        </div>

        <Table className="min-w-[720px]">
          <TableHeader>
            <TableRow>
              <TableHead className="px-4 text-[12px] font-medium text-muted-foreground">
                YouTube channel
              </TableHead>
              <TableHead className="w-10 px-4 text-center" />
              <TableHead className="px-4 text-[12px] font-medium text-muted-foreground">
                Slack channel
              </TableHead>
              <TableHead className="w-[130px] px-4 text-[12px] font-medium text-muted-foreground">
                Polling
              </TableHead>
              <TableHead className="px-4 text-right text-[12px] font-medium text-muted-foreground">
                Fwd · 24h
              </TableHead>
              <TableHead className="px-4 text-center text-[12px] font-medium text-muted-foreground">
                Status
              </TableHead>
              <TableHead className="w-12 px-4" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {shown.length === 0 ? (
              <TableRow>
                <TableCell colSpan={7} className="px-4 py-11 text-center text-[13.5px] text-muted-foreground">
                  No mappings match your search.
                </TableCell>
              </TableRow>
            ) : (
              shown.map((m) => (
                <TableRow key={m.id}>
                  <TableCell className="px-4 py-3.5">
                    <div className="flex items-center gap-2.5">
                      <div className="flex size-[30px] items-center justify-center rounded-[7px] bg-muted text-muted-foreground">
                        <YoutubeIcon size={15} />
                      </div>
                      <div>
                        <div className="text-[13.5px] font-[540]">{m.channel}</div>
                        <div className="mono text-[11.5px] text-muted-foreground">{m.channelId}</div>
                      </div>
                    </div>
                  </TableCell>
                  <TableCell className="px-4 text-center text-muted-foreground">
                    <ChevronRight className="mx-auto size-[15px]" />
                  </TableCell>
                  <TableCell className="px-4 py-3.5">
                    <span className="inline-flex items-center gap-1.5">
                      <SlackIcon size={14} className="text-muted-foreground" />
                      <span className="mono text-[13px]">{m.slack}</span>
                    </span>
                  </TableCell>
                  <TableCell className="px-4 py-3.5">
                    <Select value={m.freq} onValueChange={(v) => setFreq(m.id, v)}>
                      <SelectTrigger size="sm" className="w-full text-[12.5px]">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        {FREQ_OPTIONS.map((f) => (
                          <SelectItem key={f} value={f}>
                            {f}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </TableCell>
                  <TableCell className="px-4 py-3.5 text-right">
                    <span
                      className={`mono text-[13px] ${m.fwd24 ? "text-foreground" : "text-muted-foreground"}`}
                    >
                      {m.fwd24 ? formatNumber(m.fwd24) : "—"}
                    </span>
                  </TableCell>
                  <TableCell className="px-4 py-3.5 text-center">
                    <div className="inline-flex items-center gap-[9px]">
                      <Switch checked={m.active} onCheckedChange={() => toggle(m.id)} />
                      <span className="w-12 text-left text-[12.5px] text-muted-foreground">
                        {m.active ? "Active" : "Paused"}
                      </span>
                    </div>
                  </TableCell>
                  <TableCell className="px-4 py-3.5 text-right">
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon">
                          <MoreHorizontal className="size-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end" className="w-44">
                        <DropdownMenuItem>
                          <Settings className="size-4" />
                          Edit mapping
                        </DropdownMenuItem>
                        <DropdownMenuItem onSelect={() => toggle(m.id)}>
                          {m.active ? <Pause className="size-4" /> : <Play className="size-4" />}
                          {m.active ? "Pause" : "Resume"}
                        </DropdownMenuItem>
                        <DropdownMenuItem>
                          <ExternalLink className="size-4" />
                          Open Slack channel
                        </DropdownMenuItem>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem variant="destructive">
                          <Trash2 className="size-4" />
                          Delete
                        </DropdownMenuItem>
                      </DropdownMenuContent>
                    </DropdownMenu>
                  </TableCell>
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>

        {/* Pagination footer */}
        <div className="flex items-center justify-between border-t px-4 py-3 text-[13px]">
          <span className="text-muted-foreground">{shown.length} mappings</span>
          <div className="flex items-center gap-[7px]">
            <span className="text-[12.5px] text-muted-foreground">Page 1 of 1</span>
            <Button variant="outline" size="sm" disabled className="opacity-50">
              Previous
            </Button>
            <Button variant="outline" size="sm" disabled className="opacity-50">
              Next
            </Button>
          </div>
        </div>
      </Card>
    </div>
  );
}
