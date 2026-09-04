import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { test } from "node:test";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

function read(relativePath) {
  return readFileSync(path.join(root, relativePath), "utf8");
}

const SCREENS = [
  {
    page: "src/app/dashboard/features/smart-answer/page.tsx",
    copy: ["دستورات خود را جستجو کنید", "هنوز دستوری ساخته نشده", "اضافه کردن دستور", "نیاز به دیدن آموزش دارید؟"],
  },
  {
    page: "src/app/dashboard/features/smart-answer/new/page.tsx",
    copy: ["فعال‌کننده‌ها", "ساخت پاسخ", "پیش‌نمایش", "مثال: یک، سلام، ۱"],
  },
  {
    page: "src/app/dashboard/features/cards/page.tsx",
    copy: ["آموزش ویترین‌ساز", "اضافه کردن ویترین", "پیشنهادهای ویژه", "ویترین‌ها در دایرکت چه شکلی نمایش داده می‌شوند؟"],
  },
  {
    page: "src/app/dashboard/features/cards/new/page.tsx",
    copy: ["ایجاد ویترین‌ساز", "فعال‌کننده‌ها", "پاسخ‌ساز", "پیش‌نمایش"],
  },
  {
    page: "src/app/dashboard/features/comment-automation/page.tsx",
    copy: ["کامنت و لایو هوشمند", "اضافه کردن دستور", "دستور فعال"],
  },
  {
    page: "src/app/dashboard/features/comment-automation/new/page.tsx",
    copy: ["ایجاد کامنت و لایو هوشمند", "AutomationBuilderForm"],
  },
  {
    page: "src/app/dashboard/features/follow-up/page.tsx",
    copy: ["پشتیبان هوشمند", "فالوآپ جدید", "بازگشت مشتری", "فالوآپ چطور کار می‌کند؟"],
  },
  {
    page: "src/app/dashboard/features/form-maker/page.tsx",
    copy: ["آموزش فرم‌ساز", "فرم جدید", "عضویت در خبرنامه", "خروجی پاسخ‌ها"],
  },
  {
    page: "src/app/dashboard/features/ice-breakers/page.tsx",
    copy: ["پیام خوش‌آمدگویی", "فعال‌کننده‌ها", "ذخیره و اعمال", "پاکسازی آیس بریکرها"],
  },
  {
    page: "src/app/dashboard/smart-sms/page.tsx",
    copy: ["ارسال پیامک به مخاطبین", "پیامک انبوه", "پیامک همگام", "سه مسیر برای ارتباط سریع و هوشمند"],
  },
];

test("feature screens render the Penpot-approved copy and keep real destinations", () => {
  for (const screen of SCREENS) {
    assert.ok(existsSync(path.join(root, screen.page)), `missing route page: ${screen.page}`);
    const source = read(screen.page);
    assert.doesNotMatch(source, /redirect\(/, `${screen.page} must render instead of redirecting`);
    for (const text of screen.copy) {
      assert.ok(source.includes(text), `${screen.page} missing copy: ${text}`);
    }
  }
});

test("feature builder screens validate locally and never invent server state", () => {
  for (const page of [
    "src/app/dashboard/features/smart-answer/new/page.tsx",
    "src/app/dashboard/features/cards/new/page.tsx",
    "src/app/dashboard/features/follow-up/new/page.tsx",
    "src/app/dashboard/features/form-maker/new/page.tsx",
    "src/app/dashboard/features/ice-breakers/page.tsx",
  ]) {
    const source = read(page);
    assert.match(source, /role="alert"|noticeError/, `${page} must surface validation failures`);
    assert.doesNotMatch(source, /fetch\(/, `${page} must not call an invented endpoint`);
  }
});

test("comment automation stays wired to the real automations API", () => {
  assert.match(read("src/app/dashboard/features/comment-automation/page.tsx"), /automationsApi\(\)\.list/);
  assert.match(read("src/app/dashboard/features/comment-automation/new/page.tsx"), /automationsApi\(\)\.create/);
});

test("features navigation exposes all six Penpot sidebar destinations", () => {
  const navigation = read("src/shared/navigation/dashboard-navigation.mjs");
  for (const href of [
    "/dashboard/features/smart-answer",
    "/dashboard/features/cards",
    "/dashboard/features/follow-up",
    "/dashboard/features/comment-automation",
    "/dashboard/features/form-maker",
    "/dashboard/features/ice-breakers",
  ]) {
    assert.ok(navigation.includes(href), `navigation missing ${href}`);
    assert.ok(
      existsSync(path.join(root, "src/app", href.replace(/^\//, ""), "page.tsx")),
      `missing destination: ${href}`,
    );
  }
});
