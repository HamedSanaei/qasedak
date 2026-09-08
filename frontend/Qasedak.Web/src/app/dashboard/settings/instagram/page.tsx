"use client";

/*
 * Instagram connection management — synchronized from canonical Penpot boards
 * "Connect to Instagram — Desktop" (f5bf3c2c-b970-8002-8008-874ac4b51953) and
 * "Profile — Connected Accounts" (f5bf3c2c-b970-8002-8008-874a8c53c34c), page
 * f5bf3c2c-b970-8002-8008-874a8ad9b1a7 / f5bf3c2c-b970-8002-8008-874ac4aa747b.
 * Visual layer only; all connection behavior is application-owned below.
 */
import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import { Button, Card, StatusPill } from "../../../../shared/design/ui";
import { connectionsApi, type ConnectionsApi } from "../../../../shared/api/connections";
import { readSession, readWorkspaceId } from "../../../../shared/api/identity";
import { InstagramSurfaceNav } from "../../../../features/instagram/ui/InstagramSurfaceNav";
import surfaceStyles from "../../../../features/instagram/ui/InstagramSurface.module.css";
import {
  describeConnectionFailure,
  describeSubscriptionHealth,
  healthPresentation,
  subscriptionNeedsRepair,
  type ConnectionState,
} from "../../../../features/instagram/health";

const client: ConnectionsApi = connectionsApi();

export default function InstagramConnectionPage() {
  const router = useRouter();
  const [state, setState] = useState<"loading" | "error" | "ready">("loading");
  const [items, setItems] = useState<ConnectionState[] | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [busyAccountId, setBusyAccountId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setState("loading");
    setErrorMessage(null);
    try {
      const session = readSession();
      const workspaceId = readWorkspaceId();
      if (!session || !workspaceId) {
        router.replace("/login");
        return;
      }
      const result = await client.list(session.accessToken, workspaceId, true);
      setItems(result.items);
      setState("ready");
    } catch (error) {
      const code = error && typeof error === "object" && "code" in error ? String((error as { code: unknown }).code) : null;
      setErrorMessage(describeConnectionFailure(code));
      setState("error");
    }
  }, [router]);

  // Completes a Meta OAuth callback landing on this page (?code=&state=).
  // The state round-trips the server-issued single-use value; the query is
  // scrubbed before listing so refresh never replays the code.
  const completeCallback = useCallback(async () => {
    const params = new URLSearchParams(window.location.search);
    const code = params.get("code");
    if (!code) return;
    const scrub = () => window.history.replaceState(null, "", window.location.pathname);
    const session = readSession();
    const workspaceId = readWorkspaceId();
    if (!session || !workspaceId) {
      router.replace("/login");
      return;
    }
    try {
      const redirectUri = `${window.location.origin}/dashboard/settings/instagram`;
      await client.connect(session.accessToken, workspaceId, {
        authorizationCode: code,
        redirectUri,
        state: params.get("state"),
      });
      scrub();
    } catch (error) {
      scrub();
      const errorCode = error && typeof error === "object" && "code" in error ? String((error as { code: unknown }).code) : null;
      setErrorMessage(describeConnectionFailure(errorCode));
      setState("error");
    }
  }, [router]);

  useEffect(() => {
    // Defer so the first setState happens outside the effect body (react-hooks lint).
    const timer = window.setTimeout(() => {
      void (async () => {
        try {
          await completeCallback();
        } finally {
          await load();
        }
      })();
    }, 0);
    return () => window.clearTimeout(timer);
  }, [completeCallback, load]);

  async function startConnect() {
    try {
      const session = readSession();
      const workspaceId = readWorkspaceId();
      if (!session || !workspaceId) {
        router.replace("/login");
        return;
      }
      const redirectUri = `${window.location.origin}/dashboard/settings/instagram`;
      const { url } = await client.authorizeUrl(session.accessToken, workspaceId, redirectUri);
      // External Meta host — full navigation is intentional here.
      window.location.href = url;
    } catch (error) {
      const code = error && typeof error === "object" && "code" in error ? String((error as { code: unknown }).code) : null;
      setErrorMessage(describeConnectionFailure(code));
      setState("error");
    }
  }

  async function repair(accountId: string) {
    setBusyAccountId(accountId);
    try {
      const session = readSession();
      const workspaceId = readWorkspaceId();
      if (!session || !workspaceId) return;
      const result = await client.repairSubscription(session.accessToken, workspaceId, accountId);
      setItems((prev) =>
        prev
          ? prev.map((a) =>
              a.accountId === accountId
                ? { ...a, subscriptionHealth: result.subscriptionHealth, subscriptionDetail: null }
                : a,
            )
          : prev,
      );
    } catch (error) {
      const code = error && typeof error === "object" && "code" in error ? String((error as { code: unknown }).code) : null;
      setErrorMessage(describeConnectionFailure(code));
    } finally {
      setBusyAccountId(null);
    }
  }

  async function disconnect(accountId: string) {
    setBusyAccountId(accountId);
    try {
      const session = readSession();
      const workspaceId = readWorkspaceId();
      if (!session || !workspaceId) return;
      await client.disconnect(session.accessToken, workspaceId, accountId);
      setItems((prev) =>
        prev
          ? prev.map((a) =>
              a.accountId === accountId
                ? { ...a, health: "Disconnected", healthDetail: null }
                : a,
            )
          : prev,
      );
    } finally {
      setBusyAccountId(null);
    }
  }

  if (state === "loading") {
    return (
      <main className={surfaceStyles.page}>
        <p role="status" aria-live="polite" style={{ color: "var(--color-text-secondary)", fontSize: 14 }}>در حال دریافت وضعیت اتصال…</p>
      </main>
    );
  }

  if (state === "error") {
    return (
      <main className={surfaceStyles.page}>
        <Card>
          <div role="alert" style={{ color: "var(--color-status-danger)", fontSize: 14 }}>{errorMessage}</div>
          <div style={{ marginTop: "0.75rem" }}>
            <Button variant="outline" size="small" onClick={() => void load()}>تلاش مجدد</Button>
          </div>
        </Card>
      </main>
    );
  }

  const connected = items?.filter((a) => a.health !== "Disconnected") ?? [];
  const disconnected = items?.filter((a) => a.health === "Disconnected") ?? [];

  return (
    <main className={surfaceStyles.page}>
      <nav aria-label="مسیر" style={{ fontSize: 13, color: "#88828E", marginBottom: ".25rem" }}>
        داشبورد&nbsp;&nbsp;/&nbsp;&nbsp;اتصال پیج اینستاگرام
      </nav>
      <h1 style={{ fontSize: 24, fontWeight: 800, color: "var(--color-text-primary)", margin: "0 0 1rem" }}>
        اتصال پیج اینستاگرام
      </h1>
      <InstagramSurfaceNav />

      {connected.length === 0 ? (
        <Card>
          <h2 style={{ fontSize: 22, fontWeight: 800, color: "#141414", margin: "0 0 .5rem" }}>
            پیج اینستاگرام خود را متصل کنید
          </h2>
          <p style={{ fontSize: 14, color: "#737373", margin: 0 }}>
            برای استفاده از دایرکتم، پیج اینستاگرام خود را از مسیر رسمی متصل کنید.
          </p>

          <ul style={{ listStyle: "none", padding: 0, margin: "1.25rem 0", display: "grid", gap: ".75rem" }}>
            {[
              ["✓", "پیش‌نیاز اتصال", "نوع پیج باید Business یا Creator باشد."],
              ["?", "چرا اتصال لازم است؟", "برای پاسخ به دایرکت‌ها و کامنت‌های پیج شما."],
              ["↗", "نحوه اتصال", "در مرورگر وارد حساب اینستاگرام شوید و دسترسی را تأیید کنید."],
            ].map(([glyph, title, desc]) => (
              <li key={title} style={{ display: "flex", gap: ".75rem", alignItems: "flex-start" }}>
                <span
                  aria-hidden="true"
                  style={{
                    display: "inline-flex",
                    alignItems: "center",
                    justifyContent: "center",
                    width: 28,
                    height: 28,
                    borderRadius: "50%",
                    background: "var(--color-accent-softer)",
                    color: "var(--color-brand-accent)",
                    fontWeight: 800,
                    fontSize: 14,
                  }}
                >
                  {glyph}
                </span>
                <span>
                  <span style={{ display: "block", fontSize: 14, fontWeight: 700, color: "#141414" }}>{title}</span>
                  <span style={{ display: "block", fontSize: 12, color: "#737373" }}>{desc}</span>
                </span>
              </li>
            ))}
          </ul>

          <p style={{ fontSize: 16, fontWeight: 700, color: "#141414", margin: "0 0 .75rem" }}>
            روش اتصال را انتخاب کنید
          </p>
          <div style={{ display: "flex", gap: ".75rem", flexWrap: "wrap" }}>
            <Button onClick={() => void startConnect()}>
              <span aria-hidden="true">◉</span> اتصال با اینستاگرام
            </Button>
            <Button variant="outline" disabled title="فقط مسیر رسمی Business Login پشتیبانی می‌شود">
              <span aria-hidden="true" style={{ fontWeight: 800 }}>f</span> اتصال با فیسبوک
            </Button>
          </div>
          <p style={{ fontSize: 12, color: "var(--color-text-muted)", marginTop: ".75rem" }}>
            با اتصال پیج، شما با شرایط استفاده و سیاست حریم خصوصی دایرکتم موافقت می‌کنید.
          </p>
        </Card>
      ) : (
        <>
          <Card>
            <div style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: "1rem" }}>
              <div>
                <h2 style={{ fontSize: 18, fontWeight: 700, color: "#141414", margin: 0 }}>حساب‌های متصل</h2>
                <p style={{ fontSize: 13, color: "#737373", margin: ".25rem 0 0" }}>
                  وضعیت اتصال پیج‌های اینستاگرام ورک‌اسپیس شما
                </p>
              </div>
              <Button size="small" variant="secondary" onClick={() => void startConnect()}>
                ＋ افزودن حساب جدید
              </Button>
            </div>

            <ul style={{ listStyle: "none", margin: "1rem 0 0", padding: 0, display: "grid", gap: ".75rem" }}>
              {connected.map((account) => {
                const presentation = healthPresentation(account.health);
                return (
                  <li
                    key={account.accountId}
                    className="connection-row"
                    style={{
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "space-between",
                      gap: "1rem",
                      background: "var(--color-surface-subtle)",
                      borderRadius: "var(--radius-card)",
                      padding: ".875rem 1rem",
                    }}
                  >
                    <div className={surfaceStyles.profileRow}>
                      <span
                        className={surfaceStyles.avatar}
                        aria-label={account.profilePictureUrl ? "تصویر پروفایل حساب" : "تصویر پروفایل موجود نیست"}
                        style={account.profilePictureUrl ? { backgroundImage: `url(${account.profilePictureUrl})`, backgroundSize: "cover", backgroundPosition: "center" } : undefined}
                      >{account.profilePictureUrl ? null : "IG"}</span>
                      <div className={surfaceStyles.profileCopy}>
                      <div style={{ display: "flex", alignItems: "center", gap: ".5rem", flexWrap: "wrap" }}>
                        <strong className={surfaceStyles.profileName}>{account.displayName || account.username || account.providerIdentity}</strong>
                        <StatusPill tone={presentation.tone}>{presentation.label}</StatusPill>
                      </div>
                      <div className={surfaceStyles.profileUsername}>{account.username ? `@${account.username}` : account.providerIdentity}{account.accountType ? ` · ${account.accountType}` : ""}</div>
                      <div style={{ fontSize: 12, color: "#737373", marginTop: ".25rem" }}>
                        اتصال از {account.path === "InstagramLogin" ? "اینستاگرام لاگین" : account.path}
                        {account.tokenExpiresAtUtc
                          ? ` · انقضای توکن: ${new Date(account.tokenExpiresAtUtc).toLocaleDateString("fa-IR")}`
                          : ""}
                        {account.healthDetail ? ` · ${account.healthDetail}` : ""}
                      </div>
                      <div style={{ fontSize: 12, color: "#737373", marginTop: ".25rem" }}>
                        {describeSubscriptionHealth(account.subscriptionHealth)}
                        {account.subscriptionDetail ? ` · ${account.subscriptionDetail}` : ""}
                      </div>
                      </div>
                    </div>
                    <div style={{ display: "flex", gap: ".5rem", flexWrap: "wrap" }}>
                      <Link className={surfaceStyles.tab} href={`/dashboard/instagram/insights?accountId=${encodeURIComponent(account.accountId)}`}>آمار</Link>
                      <Link className={surfaceStyles.tab} href={`/dashboard/instagram/media?accountId=${encodeURIComponent(account.accountId)}`}>رسانه‌ها</Link>
                      <Link className={surfaceStyles.tab} href={`/dashboard/instagram/history?accountId=${encodeURIComponent(account.accountId)}`}>تاریخچه</Link>
                      {subscriptionNeedsRepair(account.subscriptionHealth) ? (
                        <Button
                          size="small"
                          variant="secondary"
                          disabled={busyAccountId === account.accountId}
                          onClick={() => void repair(account.accountId)}
                        >
                          تعمیر اعلان‌ها
                        </Button>
                      ) : null}
                      <Button
                        size="small"
                        variant="danger"
                        disabled={busyAccountId === account.accountId}
                        onClick={() => void disconnect(account.accountId)}
                      >
                        قطع اتصال
                      </Button>
                    </div>
                  </li>
                );
              })}
            </ul>
          </Card>

          {disconnected.length > 0 ? (
            <p style={{ fontSize: 12, color: "var(--color-text-muted)", marginTop: ".75rem" }}>
              {disconnected.length} حساب قطع‌شده در تاریخچه ثبت شده است.
            </p>
          ) : null}
        </>
      )}

      {errorMessage ? (
        <div role="alert" style={{ marginTop: "1rem", color: "var(--color-status-danger)", fontSize: 13 }}>
          {errorMessage}
        </div>
      ) : null}
    </main>
  );
}
