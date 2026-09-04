export const dashboardNavigation = Object.freeze([
  Object.freeze({ label: "داشبورد", href: "/dashboard", icon: "dashboard" }),
  Object.freeze({ label: "صندوق گفتگو", href: "/dashboard/inbox", icon: "inbox" }),
  Object.freeze({
    label: "امکانات",
    icon: "features",
    children: Object.freeze([
      Object.freeze({ label: "پاسخ‌های خودکار", href: "/dashboard/automations" }),
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
