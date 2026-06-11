import { clampPercent } from "@/lib/format";
import { cn } from "@/lib/utils";

type ProgressTone = "primary" | "warn" | "danger" | "ok";

/** Progress bar with tone + optional animated stripes (used by the Queue). */
export function ProgressBar({
  value,
  tone = "primary",
  animated = false,
  height = 8,
  className,
}: {
  value: number;
  tone?: ProgressTone;
  animated?: boolean;
  height?: number;
  className?: string;
}) {
  const color = `var(--${tone})`;
  return (
    <div
      className={cn("w-full overflow-hidden rounded-full bg-muted-foreground/15", className)}
      style={{ height }}
    >
      <div
        className="h-full rounded-full transition-[width] duration-500 ease-[cubic-bezier(.4,0,.2,1)]"
        style={{
          width: `${clampPercent(value)}%`,
          background: animated
            ? `repeating-linear-gradient(90deg, ${color} 0 14px, color-mix(in oklch, ${color} 78%, white) 14px 28px)`
            : color,
          backgroundSize: animated ? "28px 100%" : undefined,
          animation: animated ? "barstripes .8s linear infinite" : undefined,
        }}
      />
    </div>
  );
}
