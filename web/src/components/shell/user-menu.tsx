"use client";

import { ChevronsUpDown, LogOut, Moon, Settings, Sun, User } from "lucide-react";
import { useRouter } from "next/navigation";
import { useTheme } from "next-themes";

import { useLogout, useMe } from "@/features/auth/hooks";
import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import {
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from "@/components/ui/sidebar";

export function UserMenu() {
  const router = useRouter();
  const { isMobile } = useSidebar();
  const { resolvedTheme, setTheme } = useTheme();
  const isDark = resolvedTheme === "dark";

  const { data: me } = useMe();
  const logout = useLogout();
  const email = me?.email ?? "daniel@hookline.io";
  const name = me?.email ? me.email.split("@")[0] : "Daniel Cole";
  const initials = (me?.email ?? "DC").slice(0, 2).toUpperCase();

  async function signOut() {
    await logout.mutateAsync();
    router.push("/login");
  }

  return (
    <SidebarMenu>
      <SidebarMenuItem>
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <SidebarMenuButton
              size="lg"
              className="data-[state=open]:bg-sidebar-accent data-[state=open]:text-sidebar-accent-foreground"
            >
              <Avatar className="size-7 rounded-full">
                <AvatarFallback className="rounded-full bg-primary/15 text-[11px] font-semibold text-primary">
                  {initials}
                </AvatarFallback>
              </Avatar>
              <div className="grid flex-1 text-left leading-tight">
                <span className="truncate text-[13px] font-[540]">{name}</span>
                <span className="truncate text-[11px] text-muted-foreground">{email}</span>
              </div>
              <ChevronsUpDown className="ml-auto size-3.5 text-muted-foreground" />
            </SidebarMenuButton>
          </DropdownMenuTrigger>
          <DropdownMenuContent
            side={isMobile ? "bottom" : "right"}
            align="end"
            sideOffset={8}
            className="w-[220px]"
          >
            <DropdownMenuLabel className="font-[540]">{name}</DropdownMenuLabel>
            <DropdownMenuSeparator />
            <DropdownMenuItem>
              <User className="size-4" />
              Account
            </DropdownMenuItem>
            <DropdownMenuItem>
              <Settings className="size-4" />
              Settings
            </DropdownMenuItem>
            <DropdownMenuItem onSelect={() => setTheme(isDark ? "light" : "dark")}>
              {isDark ? <Sun className="size-4" /> : <Moon className="size-4" />}
              {isDark ? "Light mode" : "Dark mode"}
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem className="text-danger focus:bg-danger-bg focus:text-danger" onSelect={signOut}>
              <LogOut className="size-4" />
              Sign out
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </SidebarMenuItem>
    </SidebarMenu>
  );
}
