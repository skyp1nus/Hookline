import type { ReactNode } from "react";

import { cn } from "@/lib/utils";

export function Kbd({ children, className }: { children: ReactNode; className?: string }) {
  return (
    <kbd
      className={cn(
        "mono inline-flex items-center rounded-[5px] border bg-muted px-[5px] py-[2px] text-[11px] leading-none text-muted-foreground",
        className,
      )}
    >
      {children}
    </kbd>
  );
}
