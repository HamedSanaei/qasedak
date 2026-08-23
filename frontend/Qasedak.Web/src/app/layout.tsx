import type { Metadata } from "next";
import { Vazirmatn } from "next/font/google";
import "./globals.css";

// Font family observed in the Penpot design (all sidebar text styles use Vazirmatn;
// weights 400/500/600/800 appear in the inspected text layers).
const vazirmatn = Vazirmatn({
  subsets: ["arabic", "latin"],
  weight: ["400", "500", "600", "800"],
  variable: "--font-vazirmatn",
  display: "swap",
});

export const metadata: Metadata = {
  title: "Qasedak",
  description: "Instagram automation platform",
};

export default function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="fa" dir="rtl">
      <body className={vazirmatn.variable}>{children}</body>
    </html>
  );
}
