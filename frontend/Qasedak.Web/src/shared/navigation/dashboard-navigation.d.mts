export type DashboardNavigationIcon = "dashboard" | "features" | "inbox" | "instagram" | "billing" | "accounts" | "help";
export interface DashboardNavigationItem {
  readonly label: string;
  readonly href?: string;
  readonly icon: DashboardNavigationIcon;
  readonly children?: readonly { readonly label: string; readonly href: string }[];
}
export const dashboardNavigation: readonly DashboardNavigationItem[];
export function dashboardNavigationHrefs(items?: readonly DashboardNavigationItem[]): readonly string[];
