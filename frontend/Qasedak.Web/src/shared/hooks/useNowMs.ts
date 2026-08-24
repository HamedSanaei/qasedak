"use client";

/*
 * Returns the wall-clock time captured shortly after mount (or null before hydration).
 * Deferring through a timer keeps Date.now() out of render (react-hooks/purity) while
 * still enabling relative-time labels.
 */
import { useEffect, useState } from "react";

export function useNowMs(): number | null {
  const [nowMs, setNowMs] = useState<number | null>(null);
  useEffect(() => {
    const timer = window.setTimeout(() => setNowMs(Date.now()), 0);
    return () => window.clearTimeout(timer);
  }, []);
  return nowMs;
}
