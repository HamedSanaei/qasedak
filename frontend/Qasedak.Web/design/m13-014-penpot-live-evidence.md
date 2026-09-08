# M13-014 — live Penpot evidence

Completion pass: `2026-09-08` — direct official Penpot MCP write + read-back from the local machine.
File: `c828d3cf-7d4e-8145-8008-95c04412c3f4` — **Qasedak**.
Page: `61f0cbbe-bb06-8055-8008-9b00059479be` — **M13-014 · Instagram — Design additions**.
Penpot revision: `null` (not exposed by MCP).

## Canonical M13-014 screen sources

| Surface | Desktop | Tablet | Mobile |
|---|---|---|---|
| Connections | `61f0cbbe-bb06-8055-8008-9b01559f23a3` | `b9316594-6a5f-804f-8008-9b0bc1d91bed` | `61f0cbbe-bb06-8055-8008-9b016018480a` |
| Instagram profile/account | `b9316594-6a5f-804f-8008-9b0cdb1e2253` | `b9316594-6a5f-804f-8008-9b0cff23d572` | `b9316594-6a5f-804f-8008-9b0d211198e4` |
| Insights / analytics | `61f0cbbe-bb06-8055-8008-9b004ffa6c79` | `61f0cbbe-bb06-8055-8008-9b00560e6135` | `61f0cbbe-bb06-8055-8008-9b0053c501cf` |
| Media picker | `61f0cbbe-bb06-8055-8008-9b00833150eb` | `b9316594-6a5f-804f-8008-9b0bd4a3e1bb` | `61f0cbbe-bb06-8055-8008-9b008964ba0d` |
| Conversation history sync | `61f0cbbe-bb06-8055-8008-9b011f213920` | `b9316594-6a5f-804f-8008-9b0be71c1a55` | `61f0cbbe-bb06-8055-8008-9b012c434433` |
| Comment automation editor | `61f0cbbe-bb06-8055-8008-9b0191daf06e` | `b9316594-6a5f-804f-8008-9b0c2bfca52e` | `61f0cbbe-bb06-8055-8008-9b0198edbde4` |
| Reveal configuration | `61f0cbbe-bb06-8055-8008-9b00bf5eede6` | `b9316594-6a5f-804f-8008-9b0c0683282f` | `61f0cbbe-bb06-8055-8008-9b00ca7c794d` |
| Follow-up editor | `61f0cbbe-bb06-8055-8008-9b019cbcce5a` | `b9316594-6a5f-804f-8008-9b0c56fbb4bc` | `b9316594-6a5f-804f-8008-9b0e5240e00e` |

## State / supporting boards
- Connections states: `61f0cbbe-bb06-8055-8008-9b015a68ea4e`.
- Insights availability states: `61f0cbbe-bb06-8055-8008-9b007f9b67ea`.
- Media picker states: `61f0cbbe-bb06-8055-8008-9b008d5e4374`.
- History sync running/result states: `61f0cbbe-bb06-8055-8008-9b01234d3d11`, `61f0cbbe-bb06-8055-8008-9b0126915ef1`.
- Automation validation/DM states: `61f0cbbe-bb06-8055-8008-9b01a1fcd3cd`.
- Reveal flow states: `61f0cbbe-bb06-8055-8008-9b00c3bfbe5e` plus six recipient 390×844 boards for gate/check/not-following/unknown/revealed/failed.
- Shared additions gallery: `61f0cbbe-bb06-8055-8008-9b0292f0aa17`.
- Responsive implementation contract: `b9316594-6a5f-804f-8008-9b0d47e96e21` (1440×980).

## Responsive contract

- Mobile: `≤ 599px`, reference frame `390px`, 20px gutter, single-column, no persistent sidebar.
- Tablet: `600–1023px`, reference frame `834px`, 32px gutter, no persistent sidebar; stacked forms, tablet-specific multi-panel composition where designed.
- Desktop: `≥ 1024px`, reference frame `1440px`, persistent Sidebar/User Menu and desktop content composition.
- Breakpoint changes affect composition/navigation only; product state and behavior remain equivalent.
- Shared spacing base: 8px.

## Reusable components

| Component | UUID |
|---|---|
| Primary Button | `c48311ed-e700-80f8-8008-881ea9ca0747` |
| Text Field | `c48311ed-e700-80f8-8008-881eaa0bba2d` |
| Conversation Item | `c48311ed-e700-80f8-8008-881eaafa74fb` |
| Status Alert | `c48311ed-e700-80f8-8008-881eaa60589f` |
| State card | `61f0cbbe-bb06-8055-8008-9b0297c3f851` |
| Media option | `61f0cbbe-bb06-8055-8008-9b0296ea9c39` |
## Design system evidence

- Typography: Vazirmatn. Product scale commonly uses 28/700, 22/600, 16/400, 14/400, 14/600 and 12/500 with line-height 1.45; legacy shell/navigation also contains line-height 1.2.
- Palette: Brand `#BE0183`, Brand 50 `#FCEBF6`, Brand 800 `#8E0062`, Canvas `#F6F7F9`, Card `#FFFFFF`, Border `#E3E5E8`, Primary text `#2E2938`, Secondary `#7D7887`, Muted `#A09BA8`, Success `#168B5B`, Warning `#A8640A`, Danger `#C93C54`, Info `#2F6FED`.

## M13-014 mapping

All required implementation surfaces now have direct Penpot sources. Connections, profile/account, Insights, Media picker, History sync, Comment automation, Reveal and Follow-up all have 1440 / 834 / 390 reference frames. Supporting state galleries remain direct Penpot references for loading, empty, permission, validation, provider and recipient-flow states.

## QA

Live MCP geometry QA on every board created or modified in this completion pass reports:

- descendant containment violations: `0`
- text overflow violations: `0`

No blocking M13-014 design-source gap remains for the required frontend surfaces. The boards are implementation-ready design sources; final product/design approval remains a human review decision.
