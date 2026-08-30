import { redirect } from "next/navigation";
import { WorkspaceOnboarding } from "@/features/identity/ui/WorkspaceOnboarding";
import { readSession } from "@/shared/server/session";

export default async function WorkspaceOnboardingPage() {
  const session = await readSession();
  if (!session.token) redirect("/login");
  return <WorkspaceOnboarding workspaceReady={Boolean(session.workspaceId)} />;
}
