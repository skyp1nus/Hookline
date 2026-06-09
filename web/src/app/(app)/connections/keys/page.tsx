"use client";

import {
  ExternalLink,
  Key,
  MoreHorizontal,
  Pause,
  Play,
  Plus,
  RefreshCw,
  RotateCw,
  Search,
  Trash2,
} from "lucide-react";
import { useMemo, useState } from "react";

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
import { PageHeading } from "@/components/page-heading";
import { DATA, type ApiKey } from "@/lib/mock-data";
import { usePlatform } from "@/components/platform-context";

type StatusFilter = "all" | "active" | "disabled";

export default function ApiKeysPage() {
  const { platform } = usePlatform();
  const [keys, setKeys] = useState<ApiKey[]>(() => DATA.apiKeys.map((k) => ({ ...k })));
  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState<StatusFilter>("all");

  const toggle = (id: string) =>
    setKeys((prev) =>
      prev.map((k) =>
        k.id === id ? { ...k, status: k.status === "active" ? "disabled" : "active" } : k,
      ),
    );
  const del = (id: string) => setKeys((prev) => prev.filter((k) => k.id !== id));

  const shown = useMemo(() => {
    const q = query.trim().toLowerCase();
    return keys.filter((k) => {
      if (statusFilter !== "all" && k.status !== statusFilter) return false;
      if (q && !(k.name.toLowerCase().includes(q) || k.account.toLowerCase().includes(q)))
        return false;
      return true;
    });
  }, [keys, query, statusFilter]);

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title={platform.keys}
        description="Keys used to upload videos. Quota resets daily at 00:00 PT."
        actions={
          <Button size="sm">
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
              placeholder="Search keys or accounts…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          </div>
          <Select
            value={statusFilter}
            onValueChange={(v) => setStatusFilter(v as StatusFilter)}
          >
            <SelectTrigger size="sm" className="w-[150px]">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">All statuses</SelectItem>
              <SelectItem value="active">Active</SelectItem>
              <SelectItem value="disabled">Disabled</SelectItem>
            </SelectContent>
          </Select>
          <div className="flex-1" />
          <Button variant="outline" size="sm">
            <RefreshCw className="size-3.5" />
            Validate all
          </Button>
        </div>

        {/* Table */}
        {shown.length === 0 ? (
          <EmptyState />
        ) : (
          <Table className="min-w-[760px]">
            <TableHeader>
              <TableRow className="hover:bg-transparent">
                <TableHead className="px-4 text-xs font-medium text-muted-foreground">
                  Name
                </TableHead>
                <TableHead className="px-4 text-xs font-medium text-muted-foreground">
                  Key
                </TableHead>
                <TableHead className="px-4 text-xs font-medium text-muted-foreground">
                  Account
                </TableHead>
                <TableHead className="w-[240px] px-4 text-xs font-medium text-muted-foreground">
                  Usage
                </TableHead>
                <TableHead className="px-4 text-xs font-medium text-muted-foreground">
                  Status
                </TableHead>
                <TableHead className="px-4 text-xs font-medium text-muted-foreground">
                  Added
                </TableHead>
                <TableHead className="w-12 px-4" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {shown.map((k) => (
                <KeyRow key={k.id} apiKey={k} onToggle={() => toggle(k.id)} onDelete={() => del(k.id)} />
              ))}
            </TableBody>
          </Table>
        )}

        {/* Pagination footer */}
        {shown.length > 0 && (
          <div className="flex items-center justify-between border-t px-4 py-3 text-[13px]">
            <span className="text-muted-foreground">
              {shown.length} {shown.length === 1 ? "key" : "keys"}
            </span>
            <div className="flex items-center gap-2">
              <span className="text-[12.5px] text-muted-foreground">Page 1 of 1</span>
              <Button variant="outline" size="sm" disabled>
                Previous
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

function KeyRow({
  apiKey,
  onToggle,
  onDelete,
}: {
  apiKey: ApiKey;
  onToggle: () => void;
  onDelete: () => void;
}) {
  const active = apiKey.status === "active";
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
      <TableCell className="mono px-4 py-3 text-xs text-muted-foreground">{apiKey.key}</TableCell>
      <TableCell className="px-4 py-3 text-[13.5px]">{apiKey.account}</TableCell>
      <TableCell className="px-4 py-3">
        {active ? (
          <QuotaBar used={apiKey.used} total={apiKey.total} className="w-[210px]" />
        ) : (
          <span className="text-[12.5px] text-muted-foreground">—</span>
        )}
      </TableCell>
      <TableCell className="px-4 py-3">
        {active ? (
          <StatusBadge tone="ok" dot>
            Active
          </StatusBadge>
        ) : (
          <StatusBadge tone="neutral" dot>
            Disabled
          </StatusBadge>
        )}
      </TableCell>
      <TableCell className="mono px-4 py-3 text-[12.5px] text-muted-foreground">
        {apiKey.added}
      </TableCell>
      <TableCell className="px-4 py-3 text-right">
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon">
              <MoreHorizontal className="size-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="w-48">
            <DropdownMenuItem>
              <RefreshCw className="size-4" />
              Validate
            </DropdownMenuItem>
            <DropdownMenuItem>
              <RotateCw className="size-4" />
              Rotate
            </DropdownMenuItem>
            <DropdownMenuItem onSelect={onToggle}>
              {active ? <Pause className="size-4" /> : <Play className="size-4" />}
              {active ? "Disable" : "Enable"}
            </DropdownMenuItem>
            <DropdownMenuItem>
              <ExternalLink className="size-4" />
              Open in Google Cloud
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

function EmptyState() {
  return (
    <div className="flex flex-col items-center gap-2.5 px-6 py-11 text-muted-foreground">
      <div className="flex size-11 items-center justify-center rounded-[11px] bg-muted">
        <Key className="size-5" />
      </div>
      <span className="text-[13.5px]">No keys match your search.</span>
    </div>
  );
}
