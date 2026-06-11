"use client";

import { ChevronRight, MoreHorizontal, Plus, Search, Trash2 } from "lucide-react";
import { useMemo, useState } from "react";

import { SlackIcon, YoutubeIcon } from "@/components/brand-icons";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { NotYet } from "@/components/not-yet";
import { PageHeading } from "@/components/page-heading";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { type UploadMapping } from "@/lib/mock-data";
import { useDeleteUploadMapping, useUploadMappings } from "@/features/uploads/hooks";

import { AddRouteDialog } from "../_components/add-route-dialog";

export default function UploadMappingsPage() {
  const { data } = useUploadMappings();
  const deleteMapping = useDeleteUploadMapping();
  const [query, setQuery] = useState("");
  const [addOpen, setAddOpen] = useState(false);

  const rows = useMemo(() => data ?? [], [data]);
  const q = query.trim().toLowerCase();
  const shown = rows.filter(
    (m) =>
      !q ||
      m.slack.toLowerCase().includes(q) ||
      m.workspace.toLowerCase().includes(q) ||
      m.account.toLowerCase().includes(q) ||
      m.key.toLowerCase().includes(q),
  );

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Mappings"
        description="Where uploads from each Slack channel land on YouTube."
        actions={
          <Button size="sm" onClick={() => setAddOpen(true)}>
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
            <Input
              className="h-7 pl-9 text-[13px]"
              placeholder="Search routes…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          </div>
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
            {shown.length === 0 ? (
              <TableRow className="hover:bg-transparent">
                <TableCell
                  colSpan={8}
                  className="px-4 py-11 text-center text-[13.5px] text-muted-foreground"
                >
                  {rows.length === 0 ? "No routes yet — add one to get started." : "No routes match your search."}
                </TableCell>
              </TableRow>
            ) : (
              shown.map((m) => (
                <MappingRow key={m.id} mapping={m} onDelete={() => deleteMapping.mutateAsync(m.id)} />
              ))
            )}
          </TableBody>
        </Table>

        {/* footer */}
        <div className="flex items-center justify-between border-t p-3">
          <span className="text-[12.5px] text-muted-foreground">
            <span className="mono">{shown.length}</span> routes
          </span>
        </div>
      </Card>

      <AddRouteDialog open={addOpen} onOpenChange={setAddOpen} />
    </div>
  );
}

function MappingRow({ mapping, onDelete }: { mapping: UploadMapping; onDelete: () => Promise<unknown> }) {
  const [confirmOpen, setConfirmOpen] = useState(false);

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
        {/* Privacy is the GLOBAL default visibility for every route; there is no per-route privacy column
            or mutation, so it is read-only here (not a fake editable control). */}
        <NotYet reason="Per-route privacy isn't editable yet — this is the global default.">
          <span className="inline-flex items-center text-[12.5px] text-muted-foreground">
            {mapping.privacy === "—" ? "—" : mapping.privacy}
          </span>
        </NotYet>
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
        {/* Pausing a route is a wanted feature but has no backend toggle yet — disabled, never faked. */}
        <NotYet reason="Pausing a route isn't wired yet — backend pending.">
          <div className="inline-flex items-center gap-[9px]">
            <Switch checked={mapping.active} disabled className="pointer-events-none" />
            <span className="w-12 text-left text-[12.5px] text-muted-foreground">
              {mapping.active ? "Active" : "Paused"}
            </span>
          </div>
        </NotYet>
      </TableCell>
      <TableCell className="px-4 py-[13px] text-right align-middle">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon" className="size-[30px]">
              <MoreHorizontal className="size-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-44">
            <DropdownMenuItem variant="destructive" onSelect={() => setConfirmOpen(true)}>
              <Trash2 className="size-4" />
              Delete
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>

        <ConfirmDialog
          open={confirmOpen}
          onOpenChange={setConfirmOpen}
          title="Delete route?"
          description={`Stop forwarding uploads from ${mapping.slack} to ${mapping.account}? This can't be undone.`}
          confirmLabel="Delete"
          successMessage="Route deleted."
          onConfirm={onDelete}
        />
      </TableCell>
    </TableRow>
  );
}
