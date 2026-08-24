"use client";

/*
 * Create automation — board "Comment Automation — New" (f5bf3c2c-b970-8002-8008-874ec2cb62fb).
 */
import { useRouter } from "next/navigation";
import { AutomationBuilderForm } from "../../../../features/automations/AutomationBuilderForm";
import { automationsApi } from "../../../../shared/api/automations";
import { readSession, readWorkspaceId } from "../../../../shared/api/identity";

export default function NewAutomationPage() {
  const router = useRouter();
  return (
    <main style={{ padding: "1.5rem 2rem", maxWidth: 760 }}>
      <nav aria-label="مسیر" style={{ fontSize: 12, color: "#88828E", marginBottom: ".25rem" }}>
        داشبورد&nbsp;&nbsp;/&nbsp;&nbsp;کامنت و لایو هوشمند&nbsp;&nbsp;/&nbsp;&nbsp;ایجاد
      </nav>
      <h1 style={{ fontSize: 23, fontWeight: 800, color: "var(--color-text-primary)", margin: "0 0 1rem" }}>
        ایجاد کامنت و لایو هوشمند
      </h1>
      <AutomationBuilderForm
        submitLabel="ثبت"
        onSubmit={async (name, definition) => {
          const session = readSession();
          const workspaceId = readWorkspaceId();
          if (!session || !workspaceId) {
            router.replace("/login");
            return { ok: false, code: null };
          }
          try {
            await automationsApi().create(session.accessToken, workspaceId, { name, definition });
            router.push("/dashboard/automations");
            return { ok: true };
          } catch (error) {
            const code =
              error && typeof error === "object" && "code" in error
                ? String((error as { code: unknown }).code)
                : null;
            return { ok: false, code };
          }
        }}
      />
    </main>
  );
}
