"use client";

import {
  ArrowRight,
  ChevronDown,
  ChevronRight,
  CircleCheck,
  Download,
  FileVideo,
  Hash,
  Lightbulb,
  Plus,
  RotateCcw,
  Undo2,
} from "lucide-react";
import Link from "next/link";
import { type ComponentType, type ReactNode, useEffect, useState } from "react";

import { SlackIcon, YoutubeIcon } from "@/components/brand-icons";
import { BrandMark } from "@/components/brand-mark";
import { PageHeading } from "@/components/page-heading";
import { StatusBadge } from "@/components/status";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible";
import { Progress } from "@/components/ui/progress";
import { cn } from "@/lib/utils";

// End-user onboarding: how to USE Hookline once it is deployed — connect Slack + YouTube, route them, and
// watch it work. Deliberately scoped to what a normal user clicks (no .env, no Slack-app creation, no Google
// Cloud console — an admin handles that). Each step can be marked done / skipped (persisted in localStorage)
// so a half-set-up workspace stays honest about what is left. The "screenshots" are faithful in-app mockups
// rebuilt from the real components (connection cards, the add-route dialog, the queue job card) so they
// always match the live UI and never drift like a stale PNG would.

const STORAGE_KEY = "hookline.setup.completed.v1";

type Where = "slack" | "hookline";

interface Step {
  id: string;
  title: string;
  where: Where;
  summary: string;
  body: ReactNode;
}

// ── Prose building blocks ────────────────────────────────────────────────────

function Steps({ children }: { children: ReactNode }) {
  return (
    <ol className="ml-[18px] flex list-decimal flex-col gap-2 text-[13.5px] leading-relaxed marker:text-muted-foreground">
      {children}
    </ol>
  );
}

function Tip({ children }: { children: ReactNode }) {
  return (
    <div className="flex gap-2.5 rounded-lg border border-[color-mix(in_oklch,var(--primary)_30%,var(--border))] bg-primary/5 p-3 text-[12.5px] leading-relaxed text-foreground">
      <Lightbulb className="mt-px size-4 shrink-0 text-primary" />
      <div>{children}</div>
    </div>
  );
}

function GoTo({ href, label }: { href: string; label: string }) {
  return (
    <Button asChild size="sm" variant="outline" className="w-fit">
      <Link href={href}>
        {label}
        <ArrowRight className="size-3.5" />
      </Link>
    </Button>
  );
}

/** A captioned mockup frame — the in-app screenshot rebuilt as a real, on-brand component. */
function Shot({ caption, children }: { caption: string; children: ReactNode }) {
  return (
    <figure className="m-0 flex flex-col gap-1.5">
      <div className="rounded-xl border bg-muted/30 p-4">{children}</div>
      <figcaption className="text-[12px] text-muted-foreground">{caption}</figcaption>
    </figure>
  );
}

// ── In-app mockups (rebuilt from the real screens, so they stay accurate) ─────

type Glyph = ComponentType<{ className?: string; size?: number }>;

/** Mirrors connections/_components ConnectionCard (connected state). */
function MockConnectedCard({
  icon: Icon,
  iconClassName,
  name,
  handle,
}: {
  icon: Glyph;
  iconClassName?: string;
  name: string;
  handle: string;
}) {
  return (
    <div className="rounded-xl bg-card p-3.5 ring-1 ring-foreground/10">
      <div className="flex items-start justify-between">
        <div
          className={cn(
            "flex size-9 items-center justify-center rounded-[9px] border bg-background",
            iconClassName,
          )}
        >
          <Icon className="size-[18px]" />
        </div>
        <StatusBadge tone="ok" dot>
          Connected
        </StatusBadge>
      </div>
      <div className="mt-2.5 text-[13px] font-[580] tracking-[-0.01em]">{name}</div>
      <div className="mono text-[11px] text-muted-foreground">{handle}</div>
    </div>
  );
}

/** Mirrors connections/_components ConnectCard (the dashed "add new" tile). */
function MockConnectCard({ title, subtitle }: { title: string; subtitle: string }) {
  return (
    <div className="flex min-h-[112px] flex-col items-center justify-center gap-1.5 rounded-xl border border-dashed p-3 text-center text-muted-foreground">
      <div className="flex size-9 items-center justify-center rounded-[9px] border">
        <Plus className="size-[17px]" />
      </div>
      <div className="text-[12px] font-[540] text-foreground">{title}</div>
      <div className="text-[11px]">{subtitle}</div>
    </div>
  );
}

/** A fake "Add workspace" / "Connect account" header button — visual only. */
function FakeButton({ children }: { children: ReactNode }) {
  return (
    <span className="inline-flex h-7 items-center gap-1.5 rounded-md bg-primary px-2.5 text-[12px] font-medium text-primary-foreground">
      <Plus className="size-3.5" />
      {children}
    </span>
  );
}

/** Reproduces a Connections page section: heading + CTA + a connected card next to the dashed add tile. */
function MockConnections({
  heading,
  blurb,
  cta,
  icon,
  iconClassName,
  name,
  handle,
  addTitle,
}: {
  heading: string;
  blurb: string;
  cta: string;
  icon: Glyph;
  iconClassName?: string;
  name: string;
  handle: string;
  addTitle: string;
}) {
  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <div>
          <div className="text-[13.5px] font-[560] tracking-[-0.01em]">{heading}</div>
          <div className="text-[11.5px] text-muted-foreground">{blurb}</div>
        </div>
        <FakeButton>{cta}</FakeButton>
      </div>
      <div className="grid grid-cols-1 gap-2.5 sm:grid-cols-2">
        <MockConnectedCard icon={icon} iconClassName={iconClassName} name={name} handle={handle} />
        <MockConnectCard title={addTitle} subtitle="Authorize via OAuth" />
      </div>
    </div>
  );
}

/** A fake Select field — Label + bordered trigger with a value + chevron. Mirrors the add-route dialog. */
function MockField({ label, icon: Icon, value }: { label: string; icon: Glyph; value: string }) {
  return (
    <div className="flex flex-col gap-1.5">
      <span className="text-[12.5px] font-medium">{label}</span>
      <div className="flex h-9 items-center justify-between rounded-md border bg-background px-3 text-[12.5px]">
        <span className="flex items-center gap-2">
          <Icon className="size-3.5 text-muted-foreground" />
          {value}
        </span>
        <ChevronDown className="size-3.5 text-muted-foreground" />
      </div>
    </div>
  );
}

/** Reproduces the "Add route" dialog (Slack channel → YouTube account). */
function MockAddRoute() {
  return (
    <div className="mx-auto max-w-[380px] rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <div className="text-[14px] font-semibold tracking-[-0.01em]">Add route</div>
      <div className="mb-3.5 text-[12px] text-muted-foreground">
        Uploads dropped in a Slack channel land on the chosen YouTube account.
      </div>
      <div className="flex flex-col gap-3.5">
        <MockField label="Slack channel" icon={SlackIcon} value="#youtube-uploads · Acme" />
        <MockField label="YouTube account" icon={YoutubeIcon} value="Acme Channel" />
      </div>
      <div className="mt-4 flex justify-end gap-2">
        <span className="inline-flex h-7 items-center rounded-md border px-3 text-[12px] font-medium text-muted-foreground">
          Cancel
        </span>
        <span className="inline-flex h-7 items-center rounded-md bg-primary px-3 text-[12px] font-medium text-primary-foreground">
          Create route
        </span>
      </div>
    </div>
  );
}

function RouteChip({ icon: Icon, label, value }: { icon: Glyph; label: string; value: string }) {
  return (
    <div className="flex min-w-0 items-center gap-[7px]">
      <Icon className="size-[15px] shrink-0 text-muted-foreground" />
      <div className="min-w-0 leading-[1.25]">
        <div className="text-[10px] uppercase tracking-[0.03em] text-muted-foreground">{label}</div>
        <div className="truncate text-[12px] font-medium">{value}</div>
      </div>
    </div>
  );
}

/** Reproduces a Queue job card mid-upload (route chips + progress). */
function MockJobCard() {
  return (
    <div className="rounded-xl bg-card p-3.5 ring-1 ring-[color-mix(in_oklch,var(--primary)_30%,var(--border))]">
      <div className="flex items-center gap-[11px]">
        <div className="flex size-9 shrink-0 items-center justify-center rounded-[9px] bg-primary/15 text-primary">
          <FileVideo className="size-[17px]" />
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <span className="text-[13.5px] font-[580] tracking-[-0.01em]">My new video</span>
          <StatusBadge tone="info" dot pulse>
            Uploading
          </StatusBadge>
        </div>
      </div>
      <div className="mt-3 flex flex-wrap items-center gap-2.5 rounded-[9px] bg-muted px-3 py-2.5">
        <RouteChip icon={Download} label="Source" value="video.mp4" />
        <ChevronRight className="size-[15px] shrink-0 text-muted-foreground" />
        <RouteChip icon={YoutubeIcon} label="YouTube" value="Acme Channel" />
        <ChevronRight className="size-[15px] shrink-0 text-muted-foreground" />
        <RouteChip icon={SlackIcon} label="Reporting to" value="#youtube-uploads" />
      </div>
      <div className="mt-3">
        <div className="mb-1.5 flex items-center justify-between text-[12px]">
          <span className="font-medium">Uploading to YouTube</span>
          <span className="mono font-semibold">64%</span>
        </div>
        <div className="h-2 overflow-hidden rounded-full bg-muted">
          <div className="h-full w-[64%] rounded-full bg-primary" />
        </div>
      </div>
    </div>
  );
}

/** A Slack-style channel panel with the /invite line. (This part lives in Slack, not Hookline.) */
function MockSlackChannel({ channel, invite }: { channel: string; invite: string }) {
  return (
    <div className="mx-auto max-w-[420px] overflow-hidden rounded-xl bg-card ring-1 ring-foreground/10">
      <div className="flex items-center gap-2 border-b px-3.5 py-2.5">
        <Hash className="size-4 text-muted-foreground" />
        <span className="text-[13px] font-[580]">{channel}</span>
      </div>
      <div className="p-3.5">
        <div className="flex h-9 items-center rounded-md border bg-background px-3 font-mono text-[12.5px] text-foreground">
          {invite}
        </div>
        <div className="mt-2 text-[11.5px] text-muted-foreground">
          Type it in the channel and press Enter to add the bot.
        </div>
      </div>
    </div>
  );
}

// ── Steps content ────────────────────────────────────────────────────────────

const STEPS: Step[] = [
  {
    id: "slack-channel",
    title: "Create a Slack channel",
    where: "slack",
    summary: "Pick where Hookline will post.",
    body: (
      <>
        <p>
          Hookline talks to you through Slack. In your Slack workspace, create the channel where it should
          post — for example <strong>#youtube-uploads</strong> for upload reports and{" "}
          <strong>#youtube-comments</strong> for new-comment cards. One channel or two is fine.
        </p>
        <Steps>
          <li>
            In Slack, click <strong>+</strong> next to <em>Channels</em> &rarr;{" "}
            <strong>Create a channel</strong>.
          </li>
          <li>Give it a name and create it.</li>
        </Steps>
        <Shot caption="In Slack — the channel Hookline will post to">
          <MockSlackChannel channel="youtube-uploads" invite="# youtube-uploads" />
        </Shot>
      </>
    ),
  },
  {
    id: "connect-slack",
    title: "Connect your Slack workspace",
    where: "hookline",
    summary: "Authorize Hookline to post in Slack.",
    body: (
      <>
        <p>
          Open <strong>Connections &rarr; Slack</strong> and click <strong>Add workspace</strong>. Slack
          asks you to authorize Hookline — approve it. Do this for <em>both</em> bots (one posts upload
          reports, the other posts comment cards), so each tool can reach Slack.
        </p>
        <Tip>
          Connecting the <strong>YouTube Comments</strong> bot is what makes the
          &ldquo;Reject on YouTube&rdquo; button on comment cards work.
        </Tip>
        <Shot caption="Hookline — Connections → Slack (one card per bot)">
          <MockConnections
            heading="YouTube Uploads bot"
            blurb="Posts upload reports + the cancel/confirm buttons."
            cta="Add workspace"
            icon={SlackIcon}
            iconClassName="text-[#4A154B] dark:text-[#E01E5A]"
            name="Acme Workspace"
            handle="acme.slack.com"
            addTitle="Connect a workspace"
          />
        </Shot>
        <GoTo href="/connections/slack" label="Open Connections → Slack" />
      </>
    ),
  },
  {
    id: "connect-google",
    title: "Connect your YouTube account",
    where: "hookline",
    summary: "Sign in with the Google account that owns your channel.",
    body: (
      <>
        <p>
          Open <strong>Connections &rarr; Google / YouTube</strong> and click{" "}
          <strong>Connect account</strong>. Sign in with the Google account that owns your YouTube channel
          and approve the access it asks for. This one account powers both uploading videos and moderating
          comments.
        </p>
        <Tip>
          If <strong>Connect account</strong> is greyed out, your workspace admin still needs to finish the
          one-time YouTube app setup. Ask them, then come back to this step.
        </Tip>
        <Shot caption="Hookline — Connections → Google / YouTube">
          <MockConnections
            heading="Google / YouTube"
            blurb="Accounts authorized to upload + moderate comments."
            cta="Connect account"
            icon={YoutubeIcon}
            iconClassName="text-[#FF0033]"
            name="Acme Channel"
            handle="acme@gmail.com"
            addTitle="Connect account"
          />
        </Shot>
        <GoTo href="/connections/google" label="Open Connections → Google" />
      </>
    ),
  },
  {
    id: "invite-bot",
    title: "Invite the bot to your channel",
    where: "slack",
    summary: "A bot can only post where it is a member.",
    body: (
      <>
        <p>
          A Slack bot only sees channels it has joined. In each channel you created, invite the matching bot
          by typing the invite command and pressing Enter.
        </p>
        <Shot caption="In Slack — invite the bot into its channel">
          <MockSlackChannel channel="youtube-uploads" invite="/invite @YouTube Uploads" />
        </Shot>
      </>
    ),
  },
  {
    id: "mapping",
    title: "Create a route (mapping)",
    where: "hookline",
    summary: "Tell Hookline which Slack channel pairs with which YouTube channel.",
    body: (
      <>
        <p>A route is the rule that links a Slack channel and a YouTube channel.</p>
        <Steps>
          <li>
            <strong>For uploads</strong>: under <em>YouTube Uploads &rarr; Mappings</em>, click{" "}
            <strong>Add route</strong>, pick a Slack channel and your YouTube account, then{" "}
            <strong>Create route</strong>. A video posted in that channel gets uploaded.
          </li>
          <li>
            <strong>For comments</strong>: under <em>YouTube Comments &rarr; Mappings</em>, link a YouTube
            channel to a Slack channel. New comments arrive there as cards.
          </li>
        </Steps>
        <Shot caption="Hookline — the Add route dialog">
          <MockAddRoute />
        </Shot>
        <div className="flex flex-wrap gap-2">
          <GoTo href="/uploads/mappings" label="Uploads → Mappings" />
          <GoTo href="/comments/mappings" label="Comments → Mappings" />
        </div>
      </>
    ),
  },
  {
    id: "try",
    title: "Try it out",
    where: "hookline",
    summary: "Post a video, or wait for the first comment.",
    body: (
      <>
        <Steps>
          <li>
            <strong>Uploads</strong>: post a message with a video file and a title in the mapped Slack
            channel. It shows up in the <em>Queue</em>, uploads to YouTube, and you get a report back in
            Slack.
          </li>
          <li>
            <strong>Comments</strong>: when a new comment lands on your channel, Hookline posts it to Slack as
            a card with a <strong>Reject on YouTube</strong> button.
          </li>
          <li>
            Watch progress on the <em>Overview</em>, or the live <em>Queue</em> below.
          </li>
        </Steps>
        <Shot caption="Hookline — a live upload in the Queue">
          <MockJobCard />
        </Shot>
        <div className="flex flex-wrap gap-2">
          <GoTo href="/uploads/queue" label="Uploads → Queue" />
          <GoTo href="/" label="Overview" />
        </div>
      </>
    ),
  },
];

// ── "Where" chip ─────────────────────────────────────────────────────────────

function WhereBadge({ where }: { where: Where }) {
  const map = {
    slack: { icon: <SlackIcon size={12} />, label: "In Slack" },
    hookline: { icon: <BrandMark size={12} />, label: "In Hookline" },
  }[where];
  return (
    <Badge variant="outline" className="gap-1">
      {map.icon}
      {map.label}
    </Badge>
  );
}

// ── Page ─────────────────────────────────────────────────────────────────────

export default function SetupGuidePage() {
  // SSR-safe: render the deterministic empty default on the server and the first client paint, then
  // reconcile with localStorage once mounted (same pattern as the sidebar's collapsible state).
  const [completed, setCompleted] = useState<string[]>([]);
  const [openIds, setOpenIds] = useState<Set<string>>(new Set());

  useEffect(() => {
    let saved: string[] = [];
    try {
      const raw = window.localStorage.getItem(STORAGE_KEY);
      if (raw) saved = (JSON.parse(raw) as string[]).filter((id) => STEPS.some((s) => s.id === id));
    } catch {
      saved = [];
    }
    setCompleted(saved);
    const firstOpen = STEPS.find((s) => !saved.includes(s.id)) ?? STEPS[0];
    setOpenIds(new Set(firstOpen ? [firstOpen.id] : []));
  }, []);

  function persist(next: string[]) {
    setCompleted(next);
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(next));
    } catch {
      // progress is a convenience, not critical state
    }
  }

  function toggleDone(id: string) {
    const isDone = completed.includes(id);
    const next = isDone ? completed.filter((x) => x !== id) : [...completed, id];
    persist(next);
    setOpenIds((prev) => {
      const s = new Set(prev);
      if (isDone) {
        s.add(id); // re-opened for editing
      } else {
        s.delete(id); // collapse the finished step…
        const nextStep = STEPS.find((st) => st.id !== id && !next.includes(st.id));
        if (nextStep) s.add(nextStep.id); // …and surface the next unfinished one
      }
      return s;
    });
  }

  function toggleOpen(id: string) {
    setOpenIds((prev) => {
      const s = new Set(prev);
      if (s.has(id)) s.delete(id);
      else s.add(id);
      return s;
    });
  }

  function reset() {
    persist([]);
    setOpenIds(new Set(STEPS[0] ? [STEPS[0].id] : []));
  }

  const doneCount = completed.length;
  const total = STEPS.length;
  const allDone = doneCount === total;

  return (
    <div className="flex max-w-[820px] flex-col gap-[22px]">
      <PageHeading
        title="Setup guide"
        description="Get Hookline working in a few clicks — connect Slack and YouTube, then route them. Mark a step done to collapse it, or skip any you have already set up."
      />

      {/* Progress */}
      <Card className="p-0">
        <div className="flex items-center gap-4 p-[18px]">
          <div className="flex size-[38px] shrink-0 items-center justify-center rounded-lg bg-muted text-muted-foreground">
            {allDone ? (
              <CircleCheck className="size-5 text-ok" />
            ) : (
              <span className="text-[14px] font-semibold tabular-nums">{doneCount}</span>
            )}
          </div>
          <div className="flex-1">
            <div className="text-[13.5px] font-[540]">
              {allDone ? "All set — you are ready to go." : `${doneCount} of ${total} steps done`}
            </div>
            <Progress value={total ? (doneCount / total) * 100 : 0} className="mt-2 h-1.5" />
          </div>
          {doneCount > 0 && (
            <Button variant="ghost" size="sm" className="shrink-0 text-muted-foreground" onClick={reset}>
              <RotateCcw className="size-3.5" />
              Reset
            </Button>
          )}
        </div>
      </Card>

      {/* Steps */}
      <div className="flex flex-col gap-3">
        {STEPS.map((step, i) => {
          const done = completed.includes(step.id);
          const open = openIds.has(step.id);
          return (
            <Card key={step.id} className={cn("p-0", done && "opacity-[0.92]")}>
              <Collapsible open={open} onOpenChange={() => toggleOpen(step.id)}>
                <CollapsibleTrigger asChild>
                  <button
                    type="button"
                    className="flex w-full items-center gap-3.5 p-[18px] text-left outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  >
                    {/* Step index / done marker */}
                    <span
                      className={cn(
                        "flex size-7 shrink-0 items-center justify-center rounded-full text-[13px] font-semibold tabular-nums",
                        done
                          ? "bg-ok/15 text-ok"
                          : open
                            ? "bg-primary text-primary-foreground"
                            : "bg-muted text-muted-foreground",
                      )}
                    >
                      {done ? <CircleCheck className="size-4" /> : i + 1}
                    </span>

                    <div className="min-w-0 flex-1">
                      <div className="flex flex-wrap items-center gap-2">
                        <span
                          className={cn(
                            "text-[14.5px] font-[560] tracking-[-0.01em]",
                            done && "text-muted-foreground line-through decoration-1",
                          )}
                        >
                          {step.title}
                        </span>
                        <WhereBadge where={step.where} />
                      </div>
                      {!open && (
                        <div className="mt-0.5 truncate text-[12.5px] text-muted-foreground">
                          {step.summary}
                        </div>
                      )}
                    </div>
                  </button>
                </CollapsibleTrigger>

                <CollapsibleContent>
                  <div className="flex flex-col gap-3.5 px-[18px] pb-[18px] pl-[60px] text-[13.5px] leading-relaxed text-foreground/90 [&_p]:m-0">
                    {step.body}

                    <div className="mt-1 flex flex-wrap items-center gap-2 border-t pt-3.5">
                      {done ? (
                        <Button variant="outline" size="sm" onClick={() => toggleDone(step.id)}>
                          <Undo2 className="size-3.5" />
                          Mark as not done
                        </Button>
                      ) : (
                        <>
                          <Button size="sm" onClick={() => toggleDone(step.id)}>
                            <CircleCheck className="size-3.5" />
                            Mark done
                          </Button>
                          <Button
                            variant="ghost"
                            size="sm"
                            className="text-muted-foreground"
                            onClick={() => toggleDone(step.id)}
                          >
                            Skip — already set up
                          </Button>
                        </>
                      )}
                    </div>
                  </div>
                </CollapsibleContent>
              </Collapsible>
            </Card>
          );
        })}
      </div>

      <p className="text-[12.5px] text-muted-foreground">
        Your progress is saved on this device. Stuck on a greyed-out button? The one-time app setup is done by
        your workspace admin — once that is in place, every step here is just a few clicks.
      </p>
    </div>
  );
}
