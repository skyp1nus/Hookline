import { clampPercent, formatNumber } from "@/lib/format";
import { cn } from "@/lib/utils";

/** Thin quota meter with a mono readout; tone shifts at 80% / 95%. */
export function QuotaBar({
  used,
  total,
  unit = "units",
  className,
}: {
  used: number;
  total: number;
  unit?: string;
  className?: string;
}) {
  const pct = total ? (used / total) * 100 : 0;
  const tone = pct >= 95 ? "danger" : pct >= 80 ? "warn" : "primary";
  const bar = tone === "danger" ? "bg-danger" : tone === "warn" ? "bg-warn" : "bg-primary";
  const pctText =
    tone === "danger" ? "text-danger" : tone === "warn" ? "text-warn" : "text-muted-foreground";

  return (
    <div className={className}>
      <div className="mb-1.5 flex justify-between">
        <span className="mono text-xs text-foreground">
          {formatNumber(used)}
          <span className="text-muted-foreground"> / {formatNumber(total)} {unit}</span>
        </span>
        <span className={cn("mono text-xs", pctText)}>{Math.round(pct)}%</span>
      </div>
      <div className="h-1.5 w-full overflow-hidden rounded-full bg-muted-foreground/15">
        <div
          className={cn("h-full rounded-full transition-[width] duration-500", bar)}
          style={{ width: `${clampPercent(pct)}%` }}
        />
      </div>
    </div>
  );
}
