export const dashboardNavigation = Object.freeze([
  Object.freeze({ label: "داشبورد", href: "/dashboard", icon: "dashboard" }),
  Object.freeze({ label: "صندوق گفتگو", href: "/dashboard/inbox", icon: "inbox" }),
  Object.freeze({
    label: "امکانات",
    icon: "features",
    children: Object.freeze([
      Object.freeze({ label: "پاسخ هوشمند", href: "/dashboard/features/smart-answer" }),
      Object.freeze({ label: "ویترین‌ساز", href: "/dashboard/features/cards" }),
      Object.freeze({ label: "پشتیبان هوشمند", href: "/dashboard/features/follow-up" }),
      Object.freeze({ label: "کامنت / لایو هوشمند", href: "/dashboard/features/comment-automation" }),
      Object.freeze({ label: "فرم‌ساز", href: "/dashboard/features/form-maker" }),
      Object.freeze({ label: "پیام خوش‌آمدگویی", href: "/dashboard/features/ice-breakers" }),
    ]),
  }),
  Object.freeze({ label: "اتصال اینستاگرام", href: "/dashboard/settings/instagram", icon: "instagram" }),
  Object.freeze({ label: "اشتراک", href: "/dashboard/billing", icon: "billing" }),
  Object.freeze({ label: "حساب من", href: "/dashboard/accounts", icon: "accounts" }),
  Object.freeze({ label: "راهنما و پشتیبانی", href: "/dashboard/help", icon: "help" }),
]);

export function dashboardNavigationHrefs(items = dashboardNavigation) {
  return items.flatMap((item) => [
    ...(item.href ? [item.href] : []),
    ...(item.children?.map((child) => child.href) ?? []),
  ]);
}
