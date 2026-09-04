# Ad-hoc features sync — Directam-reference feature screens

No task ID was assigned for this instruction; the human directly supplied the
Penpot link and asked for a local-only implementation without pushing.

## Source (live, official Penpot plugin)

- File `c828d3cf-7d4e-8145-8008-95c04412c3f4`, 17 pages (matches the link host).
- Boards read structurally plus PNG-exported for visual fidelity:
  - `Smart Answering — Directam Reference` (`…8744692773fc`, 1440×1050)
  - `New Smart Answer — Directam Reference` (`…8745805086fa`, 1440×1480)
  - `Cards — List` (`…874de5e08228`) and `Cards — New Showcase` (`…874ded0a1711`)
  - `Comment Automation — List` (`…874ebb85c7c2`) and `… — New` (`…874ec2cb62fb`)
  - `Follow-up — List` (`…874e4a0e0215`)
  - `Form Maker — List` (`…874f2735ec6d`)
  - `Ice Breakers — Welcome Message` (`…874f9d060d9d`)
  - `Smart SMS — Desktop` (`…874a0a676492`)
- `penpotRevision` stays `null`: the API exposes none.

## What was built (visual layer only)

- Shared layer: `src/features/product/FeatureScreens.tsx` +
  `FeatureScreens.module.css` (breadcrumb, education banner, search/add row,
  card grids, phone preview, switch, chips, type buttons). Token-driven, RTL.
- Routes: `/dashboard/features` hub plus smart-answer (+new), cards (+new),
  comment-automation (+new), follow-up (+new), form-maker (+new), ice-breakers,
  and `/dashboard/smart-sms`. Navigation contract extended with the six
  Penpot sidebar destinations.
- Application behavior untouched: comment-automation list/new reuse the real
  automations API and `AutomationBuilderForm`; every other builder validates
  locally and keeps a browser-local draft, never claiming server persistence.
  Disabled response types show an honest soon label; the interactive-SMS card
  stays disabled per its design badge.

## Divergences (honest, deliberate)

- Brand stays قاصدک (Directam wordmark, demo user name and trial footer were
  not copied); the design header user chip is owned by the dashboard shell.
- Comment-automation stats show real active/draft/total counts instead of the
  design live/post-scoped numbers, which have no v1 domain equivalent.
- The `Comment Automation — List` board is already claimed by the approved
  `automations.comment` mapping, so the features list reuses it without a
  duplicate mapping; only the features `new` composition claims its board.
- Follow-up-new and form-maker-new have no inspected New board in this pass;
  they follow the same visual language as documented derivations.

## Gates

- `npm run verify` (lint, typecheck, tests incl. `features-penpot.test.mjs`,
  production build) — see final report.
- `python scripts/check_architecture.py` — see final report.
- `python scripts/validate_penpot_sync.py` — see final report.
- No commit, push or deployment (explicitly requested).
