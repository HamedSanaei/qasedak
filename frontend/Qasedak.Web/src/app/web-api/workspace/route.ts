import { NextResponse } from "next/server";
import { requestBackend, BackendUnavailableError } from "@/shared/server/backend";
import { attachWorkspaceCookie, readSession } from "@/shared/server/session";

export async function POST(request: Request) {
  const session = await readSession();
  if (!session.token) return NextResponse.json({ message: "نشست شما پایان یافته است." }, { status: 401 });
  try {
    const backend = await requestBackend("/api/v1/workspaces", { method: "POST", headers: { "content-type": "application/json", accept: "application/json", authorization: `Bearer ${session.token}` }, body: await request.text() });
    const body = await backend.json().catch(() => ({})) as { workspaceId?: string; name?: string; code?: string };
    if (!backend.ok || !body.workspaceId) return NextResponse.json({ message: body.code === "workspace.invalidName" ? "نام فضای کاری معتبر نیست." : "فضای کاری ساخته نشد." }, { status: backend.status });
    const response = NextResponse.json({ workspaceId: body.workspaceId, name: body.name }, { status: 201 });
    attachWorkspaceCookie(response, body.workspaceId);
    return response;
  } catch (error) {
    if (error instanceof BackendUnavailableError) return NextResponse.json({ message: "ارتباط با سرویس برقرار نشد." }, { status: 503 });
    return NextResponse.json({ message: "درخواست قابل پردازش نیست." }, { status: 400 });
  }
}
