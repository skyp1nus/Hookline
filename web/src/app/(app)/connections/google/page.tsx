"use client";

import { Plus } from "lucide-react";
import { useEffect, useState } from "react";

import { GoogleIcon, YoutubeIcon } from "@/components/brand-icons";
import { PageHeading } from "@/components/page-heading";
import { usePlatform } from "@/components/platform-context";
import { Button } from "@/components/ui/button";
import { Card } from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

import {
  ConnectCard,
  ConnectionCard,
  ConnectionGrid,
  type Connection,
} from "../_components/connection-card";

const GOOGLE_OAUTH_START = `${process.env.NEXT_PUBLIC_BACKEND_URL ?? ""}/google/oauth/start`;

/** Google accounts authorized to upload to YouTube — richer than DATA.ytChannels. */
const GOOGLE_ACCOUNTS: Connection[] = [
  {
    id: "acct-daniels-channel",
    name: "Daniel's Channel",
    handle: "daniel@hookline.io",
    meta: "2 keys · scope: youtube.upload",
  },
  {
    id: "acct-tutorials",
    name: "Tutorials by Daniel",
    handle: "tutorials@hookline.io",
    meta: "1 key · scope: youtube.upload",
  },
  {
    id: "acct-side-project",
    name: "Side Project Co.",
    handle: "team@sideproject.co",
    meta: "1 key · scope: youtube.upload",
  },
];

export default function GoogleConnectionsPage() {
  const { platform } = usePlatform();
  const [loading, setLoading] = useState(true);
  useEffect(() => {
    const t = setTimeout(() => setLoading(false), 600);
    return () => clearTimeout(t);
  }, []);

  return (
    <div className="flex flex-col gap-[22px]">
      <PageHeading
        title={platform.account}
        description="Google accounts authorized to upload videos to YouTube."
        actions={
          <Button size="sm" asChild>
            <a href={GOOGLE_OAUTH_START}>
              <Plus className="size-3.5" />
              Connect account
            </a>
          </Button>
        }
      />

      <ConnectionGrid>
        {loading ? (
          [0, 1, 2].map((i) => (
            <Card key={i} className="p-0">
              <div className="flex flex-col gap-3.5 p-[18px]">
                <div className="flex items-start justify-between">
                  <Skeleton className="size-10 rounded-[10px]" />
                  <Skeleton className="h-[22px] w-24 rounded-full" />
                </div>
                <Skeleton className="h-4 w-32" />
                <Skeleton className="h-3 w-40" />
                <Skeleton className="h-8 w-full" />
              </div>
            </Card>
          ))
        ) : (
          <>
            {GOOGLE_ACCOUNTS.map((acct) => (
              <ConnectionCard
                key={acct.id}
                connection={acct}
                icon={YoutubeIcon}
                iconClassName="text-[#FF0033]"
              />
            ))}
            <ConnectCard
              title="Connect account"
              subtitle="Authorize a new Google account"
              href={GOOGLE_OAUTH_START}
            />
          </>
        )}
      </ConnectionGrid>

      <p className="flex items-center gap-2 text-[12.5px] text-muted-foreground">
        <GoogleIcon size={14} />
        Hookline only requests the youtube.upload scope. Quota is governed per API key.
      </p>
    </div>
  );
}
