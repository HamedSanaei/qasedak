# Sync record — M13-014 Instagram application surface

- Date: 2026-09-08
- Task: `M13-014 — Expose complete frontend Instagram application surface`
- Penpot file: `c828d3cf-7d4e-8145-8008-95c04412c3f4` — `Qasedak`
- Penpot page: `61f0cbbe-bb06-8055-8008-9b00059479be` — `M13-014 · Instagram — Design additions`
- Penpot revision: `null` — no revision identifier was exposed.
- Evidence source: `frontend/Qasedak.Web/design/m13-014-penpot-live-evidence.md`
- Verification pass reused the recorded evidence only; Penpot MCP and Graphify were **not rerun**.

## Canonical boards consumed

| Surface | Desktop | Tablet | Mobile |
| --- | --- | --- | --- |
| Connections | `61f0cbbe-bb06-8055-8008-9b01559f23a3` | `b9316594-6a5f-804f-8008-9b0bc1d91bed` | `61f0cbbe-bb06-8055-8008-9b016018480a` |
| Instagram profile/account | `b9316594-6a5f-804f-8008-9b0cdb1e2253` | `b9316594-6a5f-804f-8008-9b0cff23d572` | `b9316594-6a5f-804f-8008-9b0d211198e4` |
| Insights | `61f0cbbe-bb06-8055-8008-9b004ffa6c79` | `61f0cbbe-bb06-8055-8008-9b00560e6135` | `61f0cbbe-bb06-8055-8008-9b0053c501cf` |
| Media picker | `61f0cbbe-bb06-8055-8008-9b00833150eb` | `b9316594-6a5f-804f-8008-9b0bd4a3e1bb` | `61f0cbbe-bb06-8055-8008-9b008964ba0d` |
| History sync | `61f0cbbe-bb06-8055-8008-9b011f213920` | `b9316594-6a5f-804f-8008-9b0be71c1a55` | `61f0cbbe-bb06-8055-8008-9b012c434433` |
| Automation editor | `61f0cbbe-bb06-8055-8008-9b0191daf06e` | `b9316594-6a5f-804f-8008-9b0c2bfca52e` | `61f0cbbe-bb06-8055-8008-9b0198edbde4` |
| Reveal | `61f0cbbe-bb06-8055-8008-9b00bf5eede6` | `b9316594-6a5f-804f-8008-9b0c0683282f` | `61f0cbbe-bb06-8055-8008-9b00ca7c794d` |
| Follow-up | `61f0cbbe-bb06-8055-8008-9b019cbcce5a` | `b9316594-6a5f-804f-8008-9b0c56fbb4bc` | `b9316594-6a5f-804f-8008-9b0e5240e00e` |

Responsive contract: `b9316594-6a5f-804f-8008-9b0d47e96e21`.

## Supporting states

- Connections: `61f0cbbe-bb06-8055-8008-9b015a68ea4e`
- Insights: `61f0cbbe-bb06-8055-8008-9b007f9b67ea`
- Media: `61f0cbbe-bb06-8055-8008-9b008d5e4374`
- History running/result: `61f0cbbe-bb06-8055-8008-9b01234d3d11`, `61f0cbbe-bb06-8055-8008-9b0126915ef1`
- Automation validation/DM: `61f0cbbe-bb06-8055-8008-9b01a1fcd3cd`
- Reveal states: `61f0cbbe-bb06-8055-8008-9b00c3bfbe5e`
- Shared additions gallery: `61f0cbbe-bb06-8055-8008-9b0292f0aa17`

## Responsive contract consumed

- Mobile `<=599px`, reference `390px`, gutter `20px`.
- Tablet `600–1023px`, reference `834px`, gutter `32px`.
- Desktop `>=1024px`, reference `1440px`, persistent desktop navigation.
- Base spacing: `8px`.
- Breakpoint changes affect composition/navigation only, not product state semantics.

## Manifest changes

`frontend/Qasedak.Web/design/penpot-sync.json` now records:

- updated `instagram.connections` mapping for M13-014 Connections + Profile;
- updated `automations.comment` mapping for Builder V2 + Reveal + Follow-up;
- new `instagram.insights` mapping;
- new `instagram.media-picker` mapping;
- new `instagram.history-sync` mapping;
- a `source.designSourceNotes.m13InstagramSurface` block containing the exact tablet/profile/responsive board IDs above.

## Honesty / behavior constraints

- Exact account selection remains application-owned; no implicit first-account fallback.
- Capability rendering is server-owned; browser code does not infer capability from raw scopes.
- `FollowGate` remains `Unsupported` and no usable toggle is exposed.
- History wording remains provider-bounded; it never claims complete inbox/history coverage.
- Existing legacy `SendDirectMessage` value is presented according to historical semantics and is not silently rewritten by merely opening the editor.
- No Penpot revision was invented.

## Recorded design QA

The reused live evidence reports zero descendant-containment violations and zero text-overflow violations on the M13-014 completion boards. Final product/design approval remains a human review decision.

## Runtime responsive and accessibility verification

Verification used the built Next.js application with a localhost-only deterministic Qasedak API stub; no Meta endpoint was contacted. Chrome Headless 152 inspected the real product DOM at `360`, `390`, `768`, `834`, `1024`, `1280`, and `1440` px.

- Connections/Profile, Insights, Media Picker, History Sync, and Automation Builder were exercised across all seven widths.
- Reveal and Follow-up editor states were additionally exercised at 390, 834, and 1440 px.
- No page-level horizontal overflow or clipped label/button/control was observed. A long user-media caption intentionally ellipsized at 360 px without causing layout overflow.
- Keyboard Tab navigation produced a visible `2px solid` focus outline in the runtime DOM.
- Runtime inspection found no unlabeled form fields or nameless interactive controls on the inspected M13-014 surfaces.
- Shared field errors are programmatically associated through `aria-describedby`; asynchronous loading states use live status regions.
- Insights keeps a visible textual follower-history fallback and provenance text, so chart meaning is not hover- or color-only.

Verification found and fixed one product defect: the Connections screen recreated its API client during render, causing its loading effect to re-enter indefinitely. The client is now stable at module scope and a regression test protects the behavior.
