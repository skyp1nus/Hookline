"use client";

import { useId, useState } from "react";

/** Minimal inline-SVG charts ported from the design (violet lead). */

/**
 * Activity area chart: a primary `forwarded` area+line with an optional `removed` overlay line. `labels`
 * are the per-bucket x-axis labels (hourly for 24h, daily for 7d/30d); a handful are shown evenly. Hover
 * reads out the bucket label plus both series.
 */
export function AreaChart({
  forwarded,
  removed,
  labels,
  height = 220,
}: {
  forwarded: number[];
  removed?: number[];
  labels: string[];
  height?: number;
}) {
  const [hover, setHover] = useState<number | null>(null);
  const gid = useId();
  const W = 600;
  const H = height;
  const padB = 26;
  const padT = 12;
  const padL = 4;
  const padR = 4;
  const n = Math.max(forwarded.length, 1);
  const max = Math.max(1, ...forwarded, ...(removed ?? [])) * 1.12;
  const iw = W - padL - padR;
  const ih = H - padB - padT;
  const x = (i: number) => padL + (n === 1 ? iw / 2 : (i / (n - 1)) * iw);
  const y = (v: number) => padT + ih - (v / max) * ih;
  const path = (data: number[]) =>
    data.map((v, i) => `${i ? "L" : "M"}${x(i).toFixed(1)} ${y(v).toFixed(1)}`).join(" ");
  const line = path(forwarded);
  const area = `${line} L${x(n - 1)} ${padT + ih} L${x(0)} ${padT + ih} Z`;
  const removedLine = removed && removed.length ? path(removed) : null;
  const gridVals = [0, 0.5, 1].map((f) => f * max);

  // Up to 5 evenly-spaced x-axis ticks.
  const tickCount = Math.min(5, n);
  const ticks = Array.from({ length: tickCount }, (_, i) =>
    tickCount === 1 ? 0 : Math.round((i / (tickCount - 1)) * (n - 1)),
  );

  // Percent positions for the HTML overlay (dots + tooltip) so they are NOT distorted by the
  // width-filling SVG (preserveAspectRatio="none" would stretch SVG <text>/<circle> horizontally).
  const leftPct = (i: number) => (x(i) / W) * 100;
  const topPct = (v: number) => (y(v) / H) * 100;

  return (
    <div className="w-full">
      <div
        className="relative w-full"
        style={{ height: H }}
        onMouseLeave={() => setHover(null)}
        onMouseMove={(ev) => {
          const r = ev.currentTarget.getBoundingClientRect();
          const rx = ((ev.clientX - r.left) / r.width) * W;
          const i = Math.round(((rx - padL) / iw) * (n - 1));
          setHover(Math.max(0, Math.min(n - 1, i)));
        }}
      >
        <svg
          viewBox={`0 0 ${W} ${H}`}
          width="100%"
          height={H}
          preserveAspectRatio="none"
          className="block"
        >
          <defs>
            <linearGradient id={gid} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0" stopColor="var(--primary)" stopOpacity="0.26" />
              <stop offset="1" stopColor="var(--primary)" stopOpacity="0.01" />
            </linearGradient>
          </defs>
          {gridVals.map((gv, i) => (
            <line
              key={i}
              x1={padL}
              x2={W - padR}
              y1={y(gv)}
              y2={y(gv)}
              stroke="var(--border)"
              strokeWidth={1}
              strokeDasharray={i === 0 ? "0" : "3 4"}
              vectorEffect="non-scaling-stroke"
            />
          ))}
          <path d={area} fill={`url(#${gid})`} />
          <path
            d={line}
            fill="none"
            stroke="var(--primary)"
            strokeWidth={2}
            strokeLinejoin="round"
            strokeLinecap="round"
            vectorEffect="non-scaling-stroke"
          />
          {removedLine && (
            <path
              d={removedLine}
              fill="none"
              stroke="var(--danger)"
              strokeWidth={1.6}
              strokeDasharray="4 3"
              strokeLinejoin="round"
              strokeLinecap="round"
              vectorEffect="non-scaling-stroke"
            />
          )}
          {hover != null && (
            <line
              x1={x(hover)}
              x2={x(hover)}
              y1={padT}
              y2={padT + ih}
              stroke="var(--primary)"
              strokeWidth={1}
              strokeOpacity={0.4}
              vectorEffect="non-scaling-stroke"
            />
          )}
        </svg>

        {/* Hover markers as HTML so they stay round (SVG circles would render as ellipses here). */}
        {hover != null && (
          <>
            <span
              className="pointer-events-none absolute size-2 -translate-x-1/2 -translate-y-1/2 rounded-full bg-primary ring-2 ring-background"
              style={{ left: `${leftPct(hover)}%`, top: `${topPct(forwarded[hover])}%` }}
            />
            {removed && removed.length > hover && (
              <span
                className="pointer-events-none absolute size-[7px] -translate-x-1/2 -translate-y-1/2 rounded-full bg-danger ring-2 ring-background"
                style={{ left: `${leftPct(hover)}%`, top: `${topPct(removed[hover])}%` }}
              />
            )}
            <div
              className="pointer-events-none absolute top-0 z-10 whitespace-nowrap rounded-[7px] border bg-popover px-[9px] py-1.5 shadow-[var(--shadow-md)]"
              style={{ left: `${leftPct(hover)}%`, marginLeft: hover > n / 2 ? -132 : 8 }}
            >
              <div className="text-[11px] text-muted-foreground">{labels[hover] ?? ""}</div>
              <div className="mono text-[13px] font-semibold">{`${forwarded[hover]} forwarded`}</div>
              {removed && removed.length > hover && (
                <div className="mono text-[12px] text-danger">{`${removed[hover]} removed`}</div>
              )}
            </div>
          </>
        )}
      </div>

      {/* X-axis labels in HTML (undistorted), aligned to the evenly-spaced ticks. */}
      <div className="mono relative mt-2 h-3 text-[10px] text-muted-foreground">
        {ticks.map((t, i) => (
          <span
            key={i}
            className="absolute whitespace-nowrap"
            style={{
              left: `${leftPct(t)}%`,
              transform:
                i === 0 ? "translateX(0)" : i === ticks.length - 1 ? "translateX(-100%)" : "translateX(-50%)",
            }}
          >
            {labels[t] ?? ""}
          </span>
        ))}
      </div>
    </div>
  );
}
