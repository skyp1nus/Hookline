"use client";

import { createContext, useContext, useEffect, useState } from "react";

import {
  DEFAULT_PLATFORM_ID,
  getPlatform,
  type Platform,
  type PlatformId,
} from "@/lib/platforms";

interface PlatformContextValue {
  platform: Platform;
  platformId: PlatformId;
  setPlatformId: (id: PlatformId) => void;
}

const PlatformContext = createContext<PlatformContextValue | null>(null);

const STORAGE_KEY = "hl-platform";

export function PlatformProvider({ children }: { children: React.ReactNode }) {
  const [platformId, setPlatformId] = useState<PlatformId>(DEFAULT_PLATFORM_ID);

  useEffect(() => {
    const stored = localStorage.getItem(STORAGE_KEY) as PlatformId | null;
    if (stored) setPlatformId(stored);
  }, []);

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, platformId);
  }, [platformId]);

  return (
    <PlatformContext.Provider
      value={{ platform: getPlatform(platformId), platformId, setPlatformId }}
    >
      {children}
    </PlatformContext.Provider>
  );
}

export function usePlatform(): PlatformContextValue {
  const ctx = useContext(PlatformContext);
  if (!ctx) throw new Error("usePlatform must be used within a PlatformProvider");
  return ctx;
}
