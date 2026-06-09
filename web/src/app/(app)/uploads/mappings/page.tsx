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
import { useEffect, useState } from "react";

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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { type Privacy, type UploadMapping } from "@/lib/mock-data";
import { useUploadMappings } from "@/features/uploads/hooks";

const PRIVACY_OPTIONS: Privacy[] = ["Public", "Unlisted", "Private"];

export default function UploadMappingsPage() {
  const { data } = useUploadMappings();
  const [rows, setRows] = useState<UploadMapping[]>([]);
  const [statusFilter, setStatusFilter] = useState("all");

  // Seed local state from the hook; privacy/active toggles mutate this local copy.
  useEffect(() => {
    if (data) setRows(data.map((m) => ({ ...m })));
  }, [data]);

  const toggle = (id: string) =>
    setRows((p) => p.map((m) => (m.id === id ? { ...m, active: !m.active } : m)));
  const setPrivacy = (id: string, privacy: Privacy) =>
    setRows((p) => p.map((m) => (m.id === id ? { ...m, privacy } : m)));

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Mappings"
        description="Where uploads from each Slack channel land on YouTube."
        actions={
          <Button size="sm">
            <Plus className="size-3.5" />
            Add route
          </Button>
        }
      />

      <Card className="gap-0 overflow-hidden p-0">
        {/* toolbar */}
        <div className="flex flex-wrap items-center gap-2.5 border-b p-3">
          <div className="relative w-[260px] max-w-full">
            <Search className="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
            <Input className="h-7 pl-9 text-[13px]" placeholder="Search routes…" />
          </div>
          <Select value={statusFilter} onValueChange={setStatusFilter}>
            <SelectTrigger size="sm" className="text-[13px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All statuses</SelectItem>
              <SelectItem value="active">Active</SelectItem>
              <SelectItem value="paused">Paused</SelectItem>
            </SelectContent>
          </Select>
        </div>

        {/* table */}
        <Table>
          <TableHeader>
            <TableRow className="hover:bg-transparent">
              <TableHead className="px-4 text-[12.5px] text-muted-foreground">
                Slack channel
              </TableHead>
              <TableHead className="w-10 px-4" />
              <TableHead className="px-4 text-[12.5px] text-muted-foreground">
                YouTube account
              </TableHead>
              <TableHead className="w-[150px] px-4 text-[12.5px] text-muted-foreground">
                Default privacy
              </TableHead>
              <TableHead className="px-4 text-[12.5px] text-muted-foreground">Playlist</TableHead>
              <TableHead className="px-4 text-right text-[12.5px] text-muted-foreground">
                Up · 24h
              </TableHead>
              <TableHead className="px-4 text-center text-[12.5px] text-muted-foreground">
                Status
              </TableHead>
              <TableHead className="w-12 px-4" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {rows
              .filter((m) =>
                statusFilter === "all"
                  ? true
                  : statusFilter === "active"
                    ? m.active
                    : !m.active,
              )
              .map((m) => (
                <MappingRow
                  key={m.id}
                  mapping={m}
                  onToggle={toggle}
                  onPrivacyChange={setPrivacy}
                />
              ))}
          </TableBody>
        </Table>

        {/* pagination */}
        <div className="flex items-center justify-between border-t p-3">
          <span className="text-[12.5px] text-muted-foreground">
            <span className="mono">{rows.length}</span> routes
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
      </Card>
    </div>
  );
}

function MappingRow({
  mapping,
  onToggle,
  onPrivacyChange,
}: {
  mapping: UploadMapping;
  onToggle: (id: string) => void;
  onPrivacyChange: (id: string, privacy: Privacy) => void;
}) {
  return (
    <TableRow>
      <TableCell className="px-4 py-[13px] align-middle">
        <div className="flex items-center gap-2.5">
          <div className="flex size-[30px] shrink-0 items-center justify-center rounded-[7px] bg-muted text-muted-foreground">
            <SlackIcon className="size-[15px]" />
          </div>
          <div>
            <div className="mono text-[13px] font-[540]">{mapping.slack}</div>
            <div className="text-[11.5px] text-muted-foreground">{mapping.workspace}</div>
          </div>
        </div>
      </TableCell>
      <TableCell className="px-4 py-[13px] text-center align-middle text-muted-foreground">
        <ChevronRight className="inline size-[15px]" />
      </TableCell>
      <TableCell className="px-4 py-[13px] align-middle">
        <div className="flex items-center gap-2">
          <YoutubeIcon className="size-[15px] text-muted-foreground" />
          <div>
            <div className="text-[13px] font-medium">{mapping.account}</div>
            <div className="mono text-[11.5px] text-muted-foreground">key {mapping.key}</div>
          </div>
        </div>
      </TableCell>
      <TableCell className="px-4 py-[13px] align-middle">
        <Select
          value={mapping.privacy === "—" ? undefined : mapping.privacy}
          onValueChange={(v) => onPrivacyChange(mapping.id, v as Privacy)}
        >
          <SelectTrigger size="sm" className="w-full text-[12.5px]">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {PRIVACY_OPTIONS.map((p) => (
              <SelectItem key={p} value={p}>
                {p}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </TableCell>
      <TableCell className="px-4 py-[13px] align-middle">
        {mapping.playlist === "—" ? (
          <span className="text-muted-foreground">—</span>
        ) : (
          <span className="text-[13px]">{mapping.playlist}</span>
        )}
      </TableCell>
      <TableCell className="px-4 py-[13px] text-right align-middle">
        <span
          className={
            mapping.up24 ? "mono text-[13px]" : "mono text-[13px] text-muted-foreground"
          }
        >
          {mapping.up24}
        </span>
      </TableCell>
      <TableCell className="px-4 py-[13px] text-center align-middle">
        <div className="inline-flex items-center gap-[9px]">
          <Switch checked={mapping.active} onCheckedChange={() => onToggle(mapping.id)} />
          <span className="w-12 text-left text-[12.5px] text-muted-foreground">
            {mapping.active ? "Active" : "Paused"}
          </span>
        </div>
      </TableCell>
      <TableCell className="px-4 py-[13px] text-right align-middle">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon" className="size-[30px]">
              <MoreHorizontal className="size-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-44">
            <DropdownMenuItem>
              <Settings className="size-4" />
              Edit route
            </DropdownMenuItem>
            <DropdownMenuItem onSelect={() => onToggle(mapping.id)}>
              {mapping.active ? <Pause className="size-4" /> : <Play className="size-4" />}
              {mapping.active ? "Pause" : "Resume"}
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
  );
}
