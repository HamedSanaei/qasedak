# M08-005 — Automation builder v1 (live-synced)

**Date:** 2026-08-24 · **Canonical file:** `c269caa0-e456-818c-8008-85a77340be64`

## Live Penpot inspection (stable UUIDs, no human navigation)

- **`Comment Automation — List`** `f5bf3c2c-b970-8002-8008-874ebb85c7c2` on page
  `Comment Automation` (`f5bf3c2c-b970-8002-8008-874eb9e5a3b1`): breadcrumb, title
  24/800, help banner (accentSoft ؟ chip + «آموزش کامنت و لایو هوشمند» 14/700 +
  «مشاهده آموزش» accent link), primary «＋ اضافه کردن دستور», search «دستورات خود را
  جستجو کنید», cards: ▧ thumbnail on accentSoft r-chip, name 15/700 («قیمت محصول»),
  «کامنت ← دایرکت» 12/500, chip «محدود به پست» 10/600, ویرایش/حذف 12/700.
- **`Comment Automation — New`** `f5bf3c2c-b970-8002-8008-874ec2cb62fb`: breadcrumb
  …/ایجاد, title 23/800 «ایجاد کامنت و لایو هوشمند», preview panel («پیش‌نمایش» 16/700 +
  chat bubble «سلام 👋 اطلاعات کامل برات ارسال شد.» / «پاسخ دایرکت»), keyword chips
  «مثال: قیمت، خرید، لینک», action row «دایرکت ⌄», submit «ثبت».
- **`Smart Answering — Component States`** `f5bf3c2c-b970-8002-8008-8747843b4ad6`:
  match-type dropdown states (برابر/شامل/هر ریپلایی with hint panels «تطبیق کامل
  عبارت»/«وجود عبارت در پیام»/«پاسخ به تمام ریپلای‌ها») — mapped onto the backend's
  Equals/Contains operators and the empty-filter anyReply semantics.

## Documented divergences (backend-authoritative)

1. Design shows a ۰/۲۰۰۰ reply counter; `AutomationAction.MaxMessageLength = 1000`
   wins — UI counter uses 1000.
2. Design's per-post scoping dropdown has no v1 domain equivalent (`AutomationDefinition`
   carries keyword filters only) → rendered disabled as «همه پست‌ها» with a tooltip.
3. Quick-reply buttons/audio upload from the States board are out of backend v1 scope.

## Backend surface added (minimal HTTP glue over tested use cases)

`AutomationEndpoints` under `/api/v1/workspaces/{id}/automations`, workspace-member policy,
mirroring the established endpoint pattern:

| Route | Behavior |
| --- | --- |
| `GET ""` | list summaries via `IAutomationRepository.ListByWorkspaceAsync` |
| `POST ""` | `Automation.Create` + wire→domain mapping (`DefinitionMapper`, fail-closed enums) |
| `GET/PUT "/{id}"` | detail; draft-only revision (`ReviseDraftDefinition`) |
| `POST "/{id}/activate"` | `ActivateAutomationUseCase`; billing denials surface verbatim (`billing.subscriptionRequired` / `billing.limitExceeded` → 409) |
| `POST "/{id}/deactivate"` | `Unpublish` (Active → Draft) |
| `DELETE "/{id}"` | terminal `Disable` |

Domain failure codes map: notFound → 404; lifecycle conflicts (alreadyActive/notActive/
alreadyDisabled/disabled/versionFrozen/billing.*) → 409; validation → 400.
Contract pinned by `AutomationEndpointContractTests` (+4 → Automations suite 44/44);
Release build clean; architecture check passed with the new test-project reference.

## Frontend

- List `/dashboard/automations`: search filter, status pills (پیش‌نویس/فعال/غیرفعال),
  keyword chips, lifecycle actions (فعال‌سازی/توقف/حذف) with Persian error copy incl.
  entitlement-denial messaging linking to billing.
- Builder `AutomationBuilderForm` (shared): name field, match-mode select with hint
  labels, keyword chips input, reply composer with live preview bubble and counter,
  submit «ثبت»; used by both `/dashboard/automations/new` (POST) and
  `/dashboard/automations/[automationId]` (PUT, frozen-state notice).
- Client-side validation mirrors backend rules (`automation.nameRequired/nameTooLong`,
  `actionTextRequired/actionTextTooLong`, keyword requirement per match mode).

## Tests/gates

Frontend 30/30 (+4); typecheck pass; manifest validator pass; architecture pass;
backend Release build clean; Automations unit tests 44/44.
