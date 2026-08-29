"use client";

/*
 * Application entry route (`/`).
 *
 * Qasedak has no approved Penpot public landing page yet, so `/` acts purely as an
 * entry point: with a valid session the user is forwarded to /dashboard, otherwise to
 * /login. Session state lives in localStorage (shared/api/identity.ts), so the redirect
 * is resolved on the client via readSession(), which clears expired sessions. Nothing is
 * rendered while redirecting — the old starter placeholder is deliberately gone.
 */
import { useEffect } from "react";
import { useRouter } from "next/navigation";
import { readSession } from "@/shared/api/identity";
import { resolveRootTarget } from "@/features/auth/rootRedirect";

export default function RootEntry() {
  const router = useRouter();

  useEffect(() => {
    router.replace(resolveRootTarget(readSession()));
  }, [router]);

  return null;
}