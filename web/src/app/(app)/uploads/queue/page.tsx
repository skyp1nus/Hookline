"use client";

import { Inbox, Plus } from "lucide-react";
import { useEffect, useState } from "react";

import { SlackIcon } from "@/components/brand-icons";
import { PageHeading } from "@/components/page-heading";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { type Job } from "@/lib/mock-data";
import { cn } from "@/lib/utils";
import { useCancelJob, useJobs } from "@/features/uploads/hooks";

import { JobCard } from "./_components/job-card";

type FilterKey = "active" | "finished" | "all";

const ACTIVE_STATUSES: Job["status"][] = ["downloading", "uploading", "queued"];
const FINISHED_STATUSES: Job["status"][] = ["done", "failed", "canceled"];

const FILTERS: { key: FilterKey; label: string }[] = [
  { key: "active", label: "Active" },
  { key: "finished", label: "Finished" },
  { key: "all", label: "All" },
];

export default function QueuePage() {
  const { data } = useJobs();
  const cancelJob = useCancelJob();
  const [jobs, setJobs] = useState<Job[]>([]);
  const [filter, setFilter] = useState<FilterKey>("active");

  // Seed local state from the hook; live tick then mutates this local copy.
  useEffect(() => {
    if (data) setJobs(data.map((j) => ({ ...j })));
  }, [data]);

  // Live ticking: advance active jobs every second and transition phases.
  useEffect(() => {
    const iv = setInterval(() => {
      setJobs((prev) =>
        prev.map((j) => {
          if (j.status !== "downloading" && j.status !== "uploading") return j;
          let next: Job = { ...j };
          let progress =
            j.progress + (j.status === "downloading" ? 1.4 : 0.9) * (0.6 + Math.random() * 0.8);
          const elapsed = j.elapsed + 1;
          let status: Job["status"] = j.status;
          let eta = j.eta != null ? Math.max(0, j.eta - 1) : null;
          if (progress >= 100) {
            if (j.status === "downloading") {
              status = "uploading";
              progress = 0;
              eta = 150 + Math.floor(Math.random() * 80);
            } else {
              status = "done";
              progress = 100;
              eta = 0;
              next.videoUrl = "youtu.be/" + Math.random().toString(36).slice(2, 7);
            }
          }
          next = { ...next, progress: Math.min(100, progress), elapsed, status, eta };
          return next;
        }),
      );
    }, 1000);
    return () => clearInterval(iv);
  }, []);

  const cancel = (id: string) => {
    setJobs((p) => p.map((j) => (j.id === id ? { ...j, status: "canceled" } : j)));
    cancelJob.mutate(id);
  };
  const retry = (id: string) =>
    setJobs((p) =>
      p.map((j) =>
        j.id === id
          ? { ...j, status: "downloading", progress: 0, elapsed: 0, eta: 220, error: undefined }
          : j,
      ),
    );
  const dismiss = (id: string) => setJobs((p) => p.filter((j) => j.id !== id));

  const active = jobs.filter((j) => ACTIVE_STATUSES.includes(j.status));
  const finished = jobs.filter((j) => FINISHED_STATUSES.includes(j.status));
  const shown = filter === "active" ? active : filter === "finished" ? finished : jobs;

  const counts: Record<FilterKey, number> = {
    active: active.length,
    finished: finished.length,
    all: jobs.length,
  };

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title="Queue"
        description="Live upload jobs from Slack. Cards update in place as they progress."
        actions={
          <>
            <div className="mr-1 flex items-center gap-[7px] text-[12.5px] text-muted-foreground">
              <span className="size-[7px] animate-pulse-dot rounded-full bg-ok" />2 workers online
            </div>
            <Button size="sm">
              <Plus className="size-3.5" />
              Queue an upload
            </Button>
          </>
        }
      />

      {/* filter tabs */}
      <div className="flex w-fit gap-1 rounded-[calc(var(--radius)-1px)] bg-muted p-1">
        {FILTERS.map(({ key, label }) => (
          <button
            key={key}
            type="button"
            onClick={() => setFilter(key)}
            className={cn(
              "flex h-[30px] items-center gap-[7px] rounded-[calc(var(--radius)-4px)] px-3 text-[13px] font-medium outline-none transition-colors focus-visible:ring-2 focus-visible:ring-ring",
              filter === key
                ? "bg-background text-foreground shadow-xs"
                : "text-muted-foreground hover:text-foreground",
            )}
          >
            {label}
            <span className="mono text-[11px] text-muted-foreground">{counts[key]}</span>
          </button>
        ))}
      </div>

      {shown.length === 0 ? (
        filter === "active" ? (
          <QueueEmpty />
        ) : (
          <div className="px-6 py-10 text-center text-[13.5px] text-muted-foreground">
            Nothing here.
          </div>
        )
      ) : (
        <div className="flex flex-col gap-[14px]">
          {shown.map((j) => (
            <JobCard key={j.id} job={j} onCancel={cancel} onRetry={retry} onDismiss={dismiss} />
          ))}
        </div>
      )}
    </div>
  );
}

function QueueEmpty() {
  return (
    <Card className="border border-dashed bg-transparent shadow-none ring-0">
      <div className="flex flex-col items-center px-6 py-14 text-center">
        <div className="mb-4 flex size-14 items-center justify-center rounded-[14px] bg-muted text-muted-foreground">
          <Inbox className="size-[26px]" />
        </div>
        <div className="text-base font-semibold">The queue is clear</div>
        <p className="mx-0 mb-[18px] mt-2 max-w-[380px] text-[13.5px] leading-relaxed text-muted-foreground">
          Post a templated message with a Google Drive link in a mapped Slack channel, and the job
          will appear here automatically.
        </p>
        <div className="flex gap-[9px]">
          <Button variant="outline" size="sm">
            <SlackIcon className="size-[15px]" />
            View Slack mappings
          </Button>
          <Button size="sm">
            <Plus className="size-[15px]" />
            Queue an upload
          </Button>
        </div>
      </div>
    </Card>
  );
}
