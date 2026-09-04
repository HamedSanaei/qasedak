"use client";

import { useRouter } from "next/navigation";
import { AutomationBuilderForm } from "@/features/automations/AutomationBuilderForm";
import { automationsApi } from "@/shared/api/automations";
import { readSession, readWorkspaceId } from "@/shared/api/identity";
import { FeatureCrumb } from "@/features/product/FeatureScreens";
import styles from "@/features/product/FeatureScreens.module.css";

export default function NewCommentAutomationPage() {
  const router = useRouter();
  return (
    <main className={styles.page}>
      <FeatureCrumb trail={["داشبورد", "کامنت و لایو هوشمند", "ایجاد کامنت و لایو هوشمند"]} />
      <h1 className={styles.title}>ایجاد کامنت و لایو هوشمند</h1>
      <section className={styles.formCard} aria-label="فرم ایجاد دستور">
        <AutomationBuilderForm
          submitLabel="ذخیره"
          onSubmit={async (name, definition) => {
            const session = readSession();
            const workspaceId = readWorkspaceId();
            if (!session || !workspaceId) {
              router.replace("/login");
              return { ok: false, code: null };
            }
            try {
              await automationsApi().create(session.accessToken, workspaceId, { name, definition });
              router.push("/dashboard/features/comment-automation");
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
      </section>
    </main>
  );
}
