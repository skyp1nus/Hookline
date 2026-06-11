"use client";

import { useId, useState } from "react";

/** Minimal inline-SVG charts ported from the design (violet lead). */

export function Sparkline({
  data,
  width = 88,
  height = 28,
  color = "var(--primary)",
  fill = true,
}: {
  data: number[];
  width?: number;
  height?: number;
  color?: string;
  fill?: boolean;
}) {
  const gid = useId();
  const max = Math.max(...data);
  const min = Math.min(...data);
  const range = max - min || 1;
  const pts = data.map((v, i) => [
    (i / (data.length - 1)) * width,
    height - ((v - min) / range) * (height - 4) - 2,
  ]);
  const line = pts.map((p, i) => `${i ? "L" : "M"}${p[0].toFixed(1)} ${p[1].toFixed(1)}`).join(" ");
  const area = `${line} L${width} ${height} L0 ${height} Z`;
  return (
    <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} className="block overflow-visible">
      {fill && (
        <defs>
          <linearGradient id={gid} x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stopColor={color} stopOpacity="0.22" />
            <stop offset="1" stopColor={color} stopOpacity="0" />
          </linearGradient>
        </defs>
      )}
      {fill && <path d={area} fill={`url(#${gid})`} />}
      <path d={line} fill="none" stroke={color} strokeWidth={1.6} strokeLinejoin="round" strokeLinecap="round" />
    </svg>
  );
}

export function AreaChart({ data, height = 220 }: { data: number[]; height?: number }) {
  const [hover, setHover] = useState<number | null>(null);
  const W = 600;
  const H = height;
  const padB = 26;
  const padT = 12;
  const padL = 4;
  const padR = 4;
  const max = Math.max(...data) * 1.12;
  const iw = W - padL - padR;
  const ih = H - padB - padT;
  const x = (i: number) => padL + (i / (data.length - 1)) * iw;
  const y = (v: number) => padT + ih - (v / max) * ih;
  const line = data.map((v, i) => `${i ? "L" : "M"}${x(i).toFixed(1)} ${y(v).toFixed(1)}`).join(" ");
  const area = `${line} L${x(data.length - 1)} ${padT + ih} L${x(0)} ${padT + ih} Z`;
  const ticks = [0, 6, 12, 18, 23];
  const labels = ["00:00", "06:00", "12:00", "18:00", "now"];
  const gridVals = [0, 0.5, 1].map((f) => f * max);

  return (
    <div className="relative w-full">
      <svg
        viewBox={`0 0 ${W} ${H}`}
        width="100%"
        height={H}
        preserveAspectRatio="none"
        className="block overflow-visible"
        onMouseLeave={() => setHover(null)}
        onMouseMove={(ev) => {
          const r = ev.currentTarget.getBoundingClientRect();
          const rx = ((ev.clientX - r.left) / r.width) * W;
          const i = Math.round(((rx - padL) / iw) * (data.length - 1));
          setHover(Math.max(0, Math.min(data.length - 1, i)));
        }}
      >
        <defs>
          <linearGradient id="areaFill" x1="0" y1="0" x2="0" y2="1">
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
          />
        ))}
        <path d={area} fill="url(#areaFill)" />
        <path d={line} fill="none" stroke="var(--primary)" strokeWidth={2} strokeLinejoin="round" strokeLinecap="round" />
        {hover != null && (
          <g>
            <line x1={x(hover)} x2={x(hover)} y1={padT} y2={padT + ih} stroke="var(--primary)" strokeWidth={1} strokeOpacity={0.4} />
            <circle cx={x(hover)} cy={y(data[hover])} r={4} fill="var(--primary)" stroke="var(--background)" strokeWidth={2} />
          </g>
        )}
        {ticks.map((t, i) => (
          <text
            key={i}
            x={x(t)}
            y={H - 6}
            fill="var(--muted-foreground)"
            fontSize={11}
            textAnchor={i === 0 ? "start" : i === ticks.length - 1 ? "end" : "middle"}
            className="mono"
          >
            {labels[i]}
          </text>
        ))}
      </svg>
      {hover != null && (
        <div
          className="pointer-events-none absolute top-0 whitespace-nowrap rounded-[7px] border bg-popover px-[9px] py-1.5 shadow-[var(--shadow-md)]"
          style={{ left: `${(x(hover) / W) * 100}%`, marginLeft: hover > data.length / 2 ? -120 : 8 }}
        >
          <div className="text-[11px] text-muted-foreground">{`${String(hover).padStart(2, "0")}:00`}</div>
          <div className="mono text-[13px] font-semibold">{`${data[hover]} comments`}</div>
        </div>
      )}
    </div>
  );
}

export function BarChart({
  data,
  height = 220,
}: {
  data: { d: string; v: number }[];
  height?: number;
}) {
  const [hover, setHover] = useState<number | null>(null);
  const max = Math.max(...data.map((d) => d.v)) * 1.15;
  const padB = 26;
  const padT = 12;
  const ih = height - padB - padT;

  return (
    <div className="w-full">
      <div className="flex items-end gap-1.5" style={{ height: ih + padT, paddingTop: padT }}>
        {data.map((d, i) => {
          const isLast = i === data.length - 1;
          const hv = hover === i;
          return (
            <div
              key={i}
              onMouseEnter={() => setHover(i)}
              onMouseLeave={() => setHover(null)}
              className="relative flex h-full flex-1 flex-col items-center justify-end"
            >
              {hv && (
                <div
                  className="absolute z-[5] whitespace-nowrap rounded-md border bg-popover px-[7px] py-1 shadow-[var(--shadow-md)]"
                  style={{ bottom: `calc(${(d.v / max) * 100}% + 6px)` }}
                >
                  <span className="mono text-xs font-semibold">{d.v}</span>
                  <span className="text-[11px] text-muted-foreground"> uploads</span>
                </div>
              )}
              <div
                className="w-full max-w-[30px] rounded-t-[5px] transition-[height,background] duration-300"
                style={{
                  height: `${(d.v / max) * 100}%`,
                  background: isLast
                    ? "var(--primary)"
                    : hv
                      ? "color-mix(in oklch, var(--primary) 55%, var(--muted))"
                      : "color-mix(in oklch, var(--muted-foreground) 26%, transparent)",
                }}
              />
            </div>
          );
        })}
      </div>
      <div className="mt-1.5 flex gap-1.5">
        {data.map((d, i) => (
          <div
            key={i}
            className="mono flex-1 overflow-hidden whitespace-nowrap text-center text-[10px] text-muted-foreground"
          >
            {i % 2 === 0 || i === data.length - 1 ? d.d.split(" ")[1] : ""}
          </div>
        ))}
      </div>
    </div>
  );
}
