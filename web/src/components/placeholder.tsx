import { PageHeading } from "@/components/page-heading";
import { Card } from "@/components/ui/card";
import type { IconType } from "@/lib/platforms";

/** Polished placeholder for routes whose real UI ships in a later slice. */
export function Placeholder({
  title,
  icon: Icon,
  description = "This module is part of the next delivery slice.",
}: {
  title: string;
  icon: IconType;
  description?: string;
}) {
  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading title={title} description={description} />
      <Card className="border-dashed bg-transparent shadow-none">
        <div className="flex flex-col items-center px-6 py-[60px] text-center">
          <div className="mb-4 flex size-14 items-center justify-center rounded-[14px] bg-muted text-muted-foreground">
            <Icon className="size-[26px]" />
          </div>
          <div className="text-base font-semibold">{title}</div>
          <p className="mx-auto mt-2 max-w-[400px] text-[13.5px] leading-relaxed text-muted-foreground">
            Designed next: we&rsquo;re shipping the shell, Overview and the upload Queue first so you
            can react to the system, then expanding to the table pages and Connections.
          </p>
        </div>
      </Card>
    </div>
  );
}
