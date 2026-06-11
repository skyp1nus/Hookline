"use client";

import {
  ChevronRight,
  MoreHorizontal,
  Pause,
  Play,
  Plus,
  Search,
  Settings,
  Trash2,
} from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";

import { SlackIcon, YoutubeIcon } from "@/components/brand-icons";
import { PageHeading } from "@/components/page-heading";
import { StatusBadge } from "@/components/status";
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
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { apiErrorMessage } from "@/lib/api/client";
import { useCommentMappings, useDeleteMapping, useUpdateMapping } from "@/features/comments/hooks";
import { type MappingDto, pollingFrequencyLabel } from "@/features/comments/types";

import { ConfirmDialog } from "@/components/confirm-dialog";

import { MappingFormDialog } from "../_components/mapping-form-dialog";

export default function MappingsPage() {
  const { data } = useCommentMappings();
  const updateMapping = useUpdateMapping();
  const deleteMapping = useDeleteMapping();

  const [query, setQuery] = useState("");
  const [status, setStatus] = useState("all");
  const [creating, setCreating] = useState(false);
  const [editing, setEditing] = useState<MappingDto | null>(null);
  const [deleting, setDeleting] = useState<MappingDto | null>(null);

  const rows = data ?? [];
  const shown = rows.filter((m) => {
    if (status === "active" && !m.isActive) return false;
    if (status === "paused" && m.isActive) return false;
    if (
      query &&
      !(
        m.youTubeChannelTitle.toLowerCase().includes(query.toLowerCase()) ||
        m.slackChannelName.toLowerCase().includes(query.toLowerCase())
      )
    )
      return false;
    return true;
  });

  async function togglePause(m: MappingDto) {
    try {
      await updateMapping.mutateAsync({ id: m.id, body: { isActive: !m.isActive } });
      toast.success(m.isActive ? "Mapping paused." : "Mapping resumed.");
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Mappings"
        description="Forward new comments from a YouTube channel into a Slack channel."
        actions={
          <Button size="sm" onClick={() => setCreating(true)}>
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
              <TableHead className="px-4 text-center text-[12px] font-medium text-muted-foreground">
                Status
              </TableHead>
              <TableHead className="w-12 px-4" />
            </TableRow>
          </TableHeader>
          <TableBody>
            {shown.length === 0 ? (
              <TableRow>
                <TableCell colSpan={6} className="px-4 py-11 text-center text-[13.5px] text-muted-foreground">
                  {rows.length === 0 ? "No mappings yet. Add one to start forwarding comments." : "No mappings match your search."}
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
                        <div className="text-[13.5px] font-[540]">{m.youTubeChannelTitle}</div>
                        <div className="mono text-[11.5px] text-muted-foreground">{m.youTubeChannelId}</div>
                      </div>
                    </div>
                  </TableCell>
                  <TableCell className="px-4 text-center text-muted-foreground">
                    <ChevronRight className="mx-auto size-[15px]" />
                  </TableCell>
                  <TableCell className="px-4 py-3.5">
                    <span className="inline-flex items-center gap-1.5">
                      <SlackIcon size={14} className="text-muted-foreground" />
                      <span className="mono text-[13px]">{m.slackChannelName}</span>
                      <span className="text-[11.5px] text-muted-foreground">· {m.slackWorkspaceName}</span>
                    </span>
                  </TableCell>
                  <TableCell className="px-4 py-3.5">
                    <span className="text-[12.5px]">{pollingFrequencyLabel(m.frequency)}</span>
                  </TableCell>
                  <TableCell className="px-4 py-3.5 text-center">
                    <StatusBadge tone={m.isActive ? "ok" : "neutral"} dot>
                      {m.isActive ? "Active" : "Paused"}
                    </StatusBadge>
                  </TableCell>
                  <TableCell className="px-4 py-3.5 text-right">
                    <DropdownMenu>
                      <DropdownMenuTrigger asChild>
                        <Button variant="ghost" size="icon">
                          <MoreHorizontal className="size-4" />
                        </Button>
                      </DropdownMenuTrigger>
                      <DropdownMenuContent align="end" className="w-44">
                        <DropdownMenuItem onSelect={() => setEditing(m)}>
                          <Settings className="size-4" />
                          Edit mapping
                        </DropdownMenuItem>
                        <DropdownMenuItem onSelect={() => togglePause(m)}>
                          {m.isActive ? <Pause className="size-4" /> : <Play className="size-4" />}
                          {m.isActive ? "Pause" : "Resume"}
                        </DropdownMenuItem>
                        <DropdownMenuSeparator />
                        <DropdownMenuItem variant="destructive" onSelect={() => setDeleting(m)}>
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

        {/* Footer */}
        <div className="flex items-center justify-between border-t px-4 py-3 text-[13px]">
          <span className="text-muted-foreground">{shown.length} mappings</span>
        </div>
      </Card>

      {/* Create */}
      <MappingFormDialog open={creating} onOpenChange={setCreating} />
      {/* Edit (keyed so the form reseeds per mapping) */}
      <MappingFormDialog
        key={editing?.id ?? "edit"}
        open={editing !== null}
        onOpenChange={(open) => !open && setEditing(null)}
        mapping={editing ?? undefined}
      />
      {/* Delete */}
      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title="Delete mapping?"
        description={
          deleting
            ? `Stop forwarding ${deleting.youTubeChannelTitle} → ${deleting.slackChannelName}. This can't be undone.`
            : undefined
        }
        confirmLabel="Delete mapping"
        successMessage="Mapping deleted."
        onConfirm={() => deleteMapping.mutateAsync(deleting!.id)}
      />
    </div>
  );
}
