import type { ReactNode } from "react";

export function PageHeading({
  title,
  description,
  actions,
}: {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-start justify-between gap-4">
      <div>
        <h1 className="m-0 text-[22px] font-semibold tracking-[-0.02em]">{title}</h1>
        {description && (
          <p className="mt-1.5 text-[13.5px] text-muted-foreground">{description}</p>
        )}
      </div>
      {actions && <div className="flex items-center gap-2">{actions}</div>}
    </div>
  );
}
