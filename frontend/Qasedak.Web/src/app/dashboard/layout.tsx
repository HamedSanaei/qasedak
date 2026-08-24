import Sidebar from "../../shared/design/Sidebar";

/*
 * Dashboard shell composition — the application-owned half of the
 * global-navigation.sidebar mapping (design/penpot-sync.json). The visual layer comes
 * from the Penpot-synchronized Sidebar component; navigation targets are application
 * decisions mapped in docs/design/SCREEN-INVENTORY.md. Screen content itself lands with
 * the M08 tasks after their designs are synced.
 */
const navItems = [
  { label: "داشبورد", href: "/dashboard", icon: "Dashboard" },
  { label: "امکانات", href: "/dashboard/features", icon: "Features" },
  { label: "پیامک هوشمند", href: "/dashboard/smart-sms", icon: "SmartSMS" },
  { label: "اتصال پیج اینستاگرام", href: "/dashboard/settings/instagram", icon: "Instagram" },
  { label: "خرید اشتراک", href: "/dashboard/billing", icon: "Pricing" },
  { label: "حساب‌های من", href: "/dashboard/accounts", icon: "Accounts" },
  { label: "راهنمایی و پشتیبانی", href: "/dashboard/help", icon: "Help" },
] as const;

const subItems = [
  { label: "پاسخ هوشمند", href: "/dashboard/features/smart-answer" },
  { label: "ویترین‌ساز", href: "/dashboard/features/cards" },
  { label: "پشتیبان هوشمند", href: "/dashboard/features/follow-up" },
  { label: "کامنت / لایو هوشمند", href: "/dashboard/features/comment-automation" },
  { label: "فرم‌ساز", href: "/dashboard/features/form-maker" },
  { label: "پیام خوش‌آمدگویی", href: "/dashboard/features/ice-breakers" },
];

export default function DashboardLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <div style={{ display: "flex" }}>
      <Sidebar
        navItems={navItems}
        subItems={subItems}
        activeHref="/dashboard"
        planLabel="اشتراک آزمایشی"
        planTimeLabel="۱۴ روز باقی‌مانده"
      />
      <main style={{ flex: 1, padding: "2rem" }}>{children}</main>
    </div>
  );
}
