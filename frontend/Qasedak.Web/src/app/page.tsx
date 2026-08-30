import type { Metadata } from "next";
import { LandingPage } from "@/features/landing/ui/LandingPage";

export const metadata: Metadata = {
  title: "دایرکتم | دستیار هوشمند فروش در اینستاگرام",
  description:
    "پاسخ‌گویی خودکار دایرکت، مدیریت کامنت، پیگیری مشتری و ابزارهای فروش اینستاگرامی در یک پنل ساده و حرفه‌ای.",
};

export default function Home() {
  return <LandingPage />;
}
