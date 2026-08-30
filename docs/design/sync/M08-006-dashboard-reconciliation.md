# M08-006 — Dashboard shell and source reconciliation

## Source reconciliation

- Previous file: `c269caa0-e456-818c-8008-85a77340be64`
- New canonical and connected file: `c269caa0-e456-818c-8008-89e5136d6851`
- Live pages: Product UI Components `c48311ed-e700-80f8-8008-881e9771c583`;
  Identity `c48311ed-e700-80f8-8008-881f0352eb6a`; Inbox
  `c48311ed-e700-80f8-8008-88200ec40bf3`; Billing
  `c48311ed-e700-80f8-8008-8820a6cf5187`
- Human page opening: **NO**; programmatic activation: **YES**

The auth, inbox and billing stable IDs survived in the designated file. Landing,
standalone navigation, Instagram and automation pages do not exist there; their old
mappings moved to `supersededSources`. The registry now contains only nine live mappings,
all implemented and validated. No active mapping references the old file.

## Shell implementation

`DashboardShell.tsx`, `Sidebar.tsx`, `UserMenu.tsx` and their CSS modules provide a single
RTL shell for every dashboard route. Active state is path-aware; the features group opens
for nested routes; its source caret and outlined/active dot are retained. The exact source
collapse icon replaces the earlier invented toggle. Desktop supports an 80 px collapsed
rail. Below 1024 px the rail becomes a modal drawer with overlay, Escape handling and
route-close behavior. The reusable user menu shows the real account email and has no
invented name, chevron, `IB Side Note`, or `Instance / Global User Menu` label.

Visual evidence covers dashboard, user menu, collapsed rail and mobile drawer. Automated
checks at 1440, 1280, 1024, 768, 390 and 360 px found no horizontal overflow; final
runtime console evidence is empty. The dashboard content itself has no standalone board
in the new file, so it uses approved primitives with real Identity, Workspace and Inbox
data rather than claiming an unmapped Penpot screen.
