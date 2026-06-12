"use client";

import { useRouter } from "next/navigation";
import { useTheme } from "next-themes";

import {
  Command,
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from "@/components/ui/command";
import { usePlatform } from "@/components/platform-context";
import {
  buildCommandActions,
  buildCommandItems,
  type CommandEntry,
} from "@/lib/nav";

const PAGE_GROUPS = ["Pages", "Connections", "System"] as const;

export function CommandPalette({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const router = useRouter();
  const { resolvedTheme, setTheme } = useTheme();
  const { platform } = usePlatform();

  const items = buildCommandItems(platform);
  const actions = buildCommandActions();

  const run = (entry: CommandEntry) => {
    onOpenChange(false);
    if (entry.action === "toggle-theme") {
      setTheme(resolvedTheme === "dark" ? "light" : "dark");
    } else if (entry.path) {
      router.push(entry.path);
    }
  };

  return (
    <CommandDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Command palette"
      description="Search pages and actions"
    >
      <Command>
        <CommandInput placeholder="Search pages and actions…" />
        <CommandList>
        <CommandEmpty>No results.</CommandEmpty>
        {PAGE_GROUPS.map((group) => {
          const groupItems = items.filter((i) => i.group === group);
          if (groupItems.length === 0) return null;
          return (
            <CommandGroup key={group} heading={group}>
              {groupItems.map((item) => {
                const Icon = item.icon;
                return (
                  <CommandItem key={item.id} value={item.label} onSelect={() => run(item)}>
                    <Icon className="size-4 text-muted-foreground" />
                    <span>{item.label}</span>
                  </CommandItem>
                );
              })}
            </CommandGroup>
          );
        })}
        <CommandGroup heading="Actions">
          {actions.map((action) => {
            const Icon = action.icon;
            return (
              <CommandItem key={action.id} value={action.label} onSelect={() => run(action)}>
                <Icon className="size-4 text-muted-foreground" />
                <span>{action.label}</span>
              </CommandItem>
            );
          })}
        </CommandGroup>
        </CommandList>
      </Command>
    </CommandDialog>
  );
}
