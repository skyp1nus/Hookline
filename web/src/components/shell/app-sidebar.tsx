"use client";

import { ChevronRight } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";

import { BrandMark } from "@/components/brand-mark";
import { usePlatform } from "@/components/platform-context";
import { PlatformSwitcher } from "@/components/shell/platform-switcher";
import { UserMenu } from "@/components/shell/user-menu";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSub,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
  SidebarRail,
} from "@/components/ui/sidebar";
import { buildSidebar, pathToRouteId, type SidebarLeaf, type SidebarTool } from "@/lib/nav";

const ACTIVE_VIOLET =
  "data-[active=true]:bg-sidebar-primary data-[active=true]:font-[540] data-[active=true]:text-sidebar-primary-foreground data-[active=true]:hover:bg-sidebar-primary data-[active=true]:hover:text-sidebar-primary-foreground";

export function AppSidebar() {
  const pathname = usePathname();
  const activeId = pathToRouteId(pathname);
  const { platform } = usePlatform();
  const model = buildSidebar(platform);
  const TopIcon = model.top.icon;

  return (
    <Sidebar collapsible="icon">
      <SidebarHeader className="gap-2">
        <div className="flex items-center gap-2.5 px-1 py-0.5">
          <BrandMark size={30} className="block shrink-0" />
          <span className="text-[15.5px] font-[640] leading-tight tracking-[-0.02em] group-data-[collapsible=icon]:hidden">
            Hookline
          </span>
        </div>
        <div className="group-data-[collapsible=icon]:hidden">
          <PlatformSwitcher />
        </div>
      </SidebarHeader>

      <SidebarContent>
        <SidebarGroup className="py-1">
          <SidebarMenu>
            <SidebarMenuItem>
              <SidebarMenuButton
                asChild
                isActive={activeId === model.top.id}
                tooltip={model.top.label}
                className={ACTIVE_VIOLET}
              >
                <Link href={model.top.path}>
                  <TopIcon />
                  <span>{model.top.label}</span>
                </Link>
              </SidebarMenuButton>
            </SidebarMenuItem>
          </SidebarMenu>
        </SidebarGroup>

        {model.groups.map((group) => (
          <SidebarGroup key={group.label}>
            <SidebarGroupLabel>{group.label}</SidebarGroupLabel>
            <SidebarMenu>
              {group.entries.map((entry) =>
                entry.type === "tool" ? (
                  <ToolNav key={entry.tool.id} tool={entry.tool} activeId={activeId} />
                ) : (
                  <LeafNav key={entry.leaf.id} leaf={entry.leaf} activeId={activeId} />
                ),
              )}
            </SidebarMenu>
          </SidebarGroup>
        ))}
      </SidebarContent>

      <SidebarFooter>
        <UserMenu />
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  );
}

function LeafNav({ leaf, activeId }: { leaf: SidebarLeaf; activeId: string | null }) {
  const Icon = leaf.icon;
  return (
    <SidebarMenuItem>
      <SidebarMenuButton
        asChild
        isActive={activeId === leaf.id}
        tooltip={leaf.label}
        className={ACTIVE_VIOLET}
      >
        <Link href={leaf.path}>
          <Icon />
          <span>{leaf.label}</span>
        </Link>
      </SidebarMenuButton>
    </SidebarMenuItem>
  );
}

function ToolNav({ tool, activeId }: { tool: SidebarTool; activeId: string | null }) {
  const Icon = tool.icon;
  const childActive = tool.children.some((c) => c.id === activeId);
  return (
    <Collapsible
      asChild
      defaultOpen={tool.defaultOpen || childActive}
      className="group/collapsible"
    >
      <SidebarMenuItem>
        <CollapsibleTrigger asChild>
          <SidebarMenuButton tooltip={tool.label} className="font-medium">
            <Icon />
            <span>{tool.label}</span>
            <ChevronRight className="ml-auto transition-transform duration-200 group-data-[state=open]/collapsible:rotate-90" />
          </SidebarMenuButton>
        </CollapsibleTrigger>
        <CollapsibleContent>
          <SidebarMenuSub>
            {tool.children.map((child) => (
              <SidebarMenuSubItem key={child.id}>
                <SidebarMenuSubButton
                  asChild
                  isActive={activeId === child.id}
                  className={ACTIVE_VIOLET}
                >
                  <Link href={child.path}>
                    <span>{child.label}</span>
                  </Link>
                </SidebarMenuSubButton>
              </SidebarMenuSubItem>
            ))}
          </SidebarMenuSub>
        </CollapsibleContent>
      </SidebarMenuItem>
    </Collapsible>
  );
}
