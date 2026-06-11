"use client";

import { Key, MoreHorizontal, Pause, Play, Plus, Search, Trash2 } from "lucide-react";
import { useMemo, useState } from "react";
import { toast } from "sonner";

import { QuotaBar } from "@/components/quota-bar";
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
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { PageHeading } from "@/components/page-heading";
import { apiErrorMessage } from "@/lib/api/client";
import { usePlatform } from "@/components/platform-context";
import { useApiKeys, useDeleteApiKey, useToggleApiKey } from "@/features/comments/hooks";
import { type ApiKeyDto } from "@/features/comments/types";

import { AddApiKeyDialog } from "./_components/add-api-key-dialog";

type StatusFilter = "all" | "active" | "disabled";

export default function ApiKeysPage() {
  const { platform } = usePlatform();
  const { data } = useApiKeys();
  const toggleKey = useToggleApiKey();
  const deleteKey = useDeleteApiKey();

  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");
  const [adding, setAdding] = useState(false);
  const [removing, setRemoving] = useState<ApiKeyDto | null>(null);

  const keys = useMemo(() => data ?? [], [data]);

  const shown = useMemo(() => {
    const q = query.trim().toLowerCase();
    return keys.filter((k) => {
      if (statusFilter === "active" && !k.isActive) return false;
      if (statusFilter === "disabled" && k.isActive) return false;
      if (q && !(k.name.toLowerCase().includes(q) || k.keyHint.toLowerCase().includes(q))) return false;
      return true;
    });
  }, [keys, query, statusFilter]);

  async function toggle(k: ApiKeyDto) {
    try {
      await toggleKey.mutateAsync(k.id);
      toast.success(k.isActive ? "Key disabled." : "Key enabled.");
    } catch (error) {
      toast.error(apiErrorMessage(error));
    }
  }

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title={platform.keys}
        description="YouTube Data API keys for comment polling. Quota resets daily at 00:00 PT."
        actions={
          <Button size="sm" onClick={() => setAdding(true)}>
            <Plus className="size-3.5" />
            Add API key
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
              placeholder="Search keys…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          </div>
          <Select value={statusFilter} onValueChange={(v) => setStatusFilter(v as StatusFilter)}>
            <SelectTrigger size="sm" className="w-[150px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All statuses</SelectItem>
              <SelectItem value="active">Active</SelectItem>
              <SelectItem value="disabled">Disabled</SelectItem>
            </SelectContent>
          </Select>
        </div>

        {/* Table */}
        {shown.length === 0 ? (
          <EmptyState empty={keys.length === 0} />
        ) : (
          <Table className="min-w-[720px]">
            <TableHeader>
              <TableRow className="hover:bg-transparent">
                <TableHead className="px-4 text-xs font-medium text-muted-foreground">Name</TableHead>
                <TableHead className="px-4 text-xs font-medium text-muted-foreground">Key</TableHead>
                <TableHead className="w-[240px] px-4 text-xs font-medium text-muted-foreground">Usage · today</TableHead>
                <TableHead className="px-4 text-xs font-medium text-muted-foreground">Status</TableHead>
                <TableHead className="px-4 text-xs font-medium text-muted-foreground">Added</TableHead>
                <TableHead className="w-12 px-4" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {shown.map((k) => (
                <KeyRow
                  key={k.id}
                  apiKey={k}
                  onToggle={() => toggle(k)}
                  onDelete={() => setRemoving(k)}
                />
              ))}
            </TableBody>
          </Table>
        )}

        {shown.length > 0 && (
          <div className="flex items-center justify-between border-t px-4 py-3 text-[13px]">
            <span className="text-muted-foreground">
              {shown.length} {shown.length === 1 ? "key" : "keys"}
            </span>
          </div>
        )}
      </Card>

      <AddApiKeyDialog open={adding} onOpenChange={setAdding} />
      <ConfirmDialog
        open={removing !== null}
        onOpenChange={(open) => !open && setRemoving(null)}
        title="Delete API key?"
        description={removing ? `Remove ${removing.name} (${removing.keyHint}). This can't be undone.` : undefined}
        confirmLabel="Delete key"
        successMessage="Key deleted."
        onConfirm={() => deleteKey.mutateAsync(removing!.id)}
      />
    </div>
  );
}

function KeyRow({
  apiKey,
  onToggle,
  onDelete,
}: {
  apiKey: ApiKeyDto;
  onToggle: () => void;
  onDelete: () => void;
}) {
  const active = apiKey.isActive;
  return (
    <TableRow>
      <TableCell className="px-4 py-3">
        <div className="flex items-center gap-2.5">
          <div className="flex size-[30px] shrink-0 items-center justify-center rounded-[7px] bg-muted text-muted-foreground">
            <Key className="size-[15px]" />
          </div>
          <div className="text-[13.5px] font-[540]">{apiKey.name}</div>
        </div>
      </TableCell>
      <TableCell className="mono px-4 py-3 text-xs text-muted-foreground">{apiKey.keyHint}</TableCell>
      <TableCell className="px-4 py-3">
        {active ? (
          <QuotaBar used={apiKey.todayUnitsUsed} total={apiKey.dailyQuotaLimit} className="w-[210px]" />
        ) : (
          <span className="text-[12.5px] text-muted-foreground">—</span>
        )}
      </TableCell>
      <TableCell className="px-4 py-3">
        <StatusBadge tone={active ? "ok" : "neutral"} dot>
          {active ? "Active" : "Disabled"}
        </StatusBadge>
      </TableCell>
      <TableCell className="mono px-4 py-3 text-[12.5px] text-muted-foreground">
        {new Date(apiKey.createdAt).toLocaleDateString()}
      </TableCell>
      <TableCell className="px-4 py-3 text-right">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon">
              <MoreHorizontal className="size-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-44">
            <DropdownMenuItem onSelect={onToggle}>
              {active ? <Pause className="size-4" /> : <Play className="size-4" />}
              {active ? "Disable" : "Enable"}
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem variant="destructive" onSelect={onDelete}>
              <Trash2 className="size-4" />
              Delete key
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </TableCell>
    </TableRow>
  );
}

function EmptyState({ empty }: { empty: boolean }) {
  return (
    <div className="flex flex-col items-center gap-2.5 px-6 py-11 text-muted-foreground">
      <div className="flex size-11 items-center justify-center rounded-[11px] bg-muted">
        <Key className="size-5" />
      </div>
      <span className="text-[13.5px]">{empty ? "No API keys yet. Add one to start polling." : "No keys match your search."}</span>
    </div>
  );
}
