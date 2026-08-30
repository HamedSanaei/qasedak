import { redirect } from "next/navigation";
import type { ReactNode } from "react";
import { getIdentity, getWorkspaceMembers } from "@/features/identity/api/server";
import { DashboardShell } from "@/shared/design/DashboardShell";
import { readSession } from "@/shared/server/session";

const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

export default async function DashboardLayout({ children }: Readonly<{ children: ReactNode }>) {
  const session = await readSession();
  if (!session.token) redirect("/login");
  const identity = await getIdentity(session.token);
  if (!identity.ok && identity.status === 401) redirect("/login?reason=session");
  let workspaceLabel = "فضای کاری قاصدک";
  let workspaceMeta = "فضای کاری انتخاب نشده است";
  if (session.workspaceId && uuid.test(session.workspaceId)) {
    const workspace = await getWorkspaceMembers(session.token, session.workspaceId);
    if (workspace.ok) {
      workspaceLabel = workspace.data.workspaceName;
      workspaceMeta = `${workspace.data.members.length.toLocaleString("fa-IR")} عضو`;
    } else if (workspace.status === 503) {
      workspaceMeta = "اطلاعات موقتاً در دسترس نیست";
    }
  }
  return <DashboardShell email={identity.ok ? identity.data.email : "حساب قاصدک"} workspaceLabel={workspaceLabel} workspaceMeta={workspaceMeta}>{children}</DashboardShell>;
}
