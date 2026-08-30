import { PageHeader } from "@/shared/design/PageHeader";
import { readSession } from "@/shared/server/session";
import { getIdentity, getWorkspaceMembers } from "@/features/identity/api/server";
import { getInboxPage } from "@/features/conversations/api/server";
import { DashboardOverview } from "@/features/dashboard/DashboardOverview";

export default async function DashboardPage() {
  const session = await readSession();
  const identity = await getIdentity(session.token!);
  const workspace = session.workspaceId ? await getWorkspaceMembers(session.token!, session.workspaceId) : null;
  const inbox = session.workspaceId && workspace?.ok ? await getInboxPage(session.token!, session.workspaceId) : null;
  return <><PageHeader eyebrow="فضای کاری" title="داشبورد" description="نمای واقعی وضعیت حساب، فضای کاری و صندوق گفتگو؛ بدون آمار ساختگی." /><DashboardOverview identity={identity} workspace={workspace} inbox={inbox} workspaceSelected={Boolean(session.workspaceId)} /></>;
}
