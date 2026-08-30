# M08-000 — Existing Instagram and Automation design audit

- Canonical/connected file UUID:
  `c269caa0-e456-818c-8008-85a77340be64` — PASS
- Was any target page manually opened by human? **NO**
- Were pages activated programmatically? **YES**

## Instagram connections — preserved

- Mapping: `instagram.connections`
- Page: `f5bf3c2c-b970-8002-8008-874ac4aa747b`
- Board: `f5bf3c2c-b970-8002-8008-874ac4b51953`

The board was exported and inspected. Its separate Instagram Login and Facebook Login
choices remain consistent with ADR-006, so no wholesale redesign or visual edit was
made. It is now registered as approved with a planned implementation source.

## Comment Automation — bounded debt correction

- List mapping/board: `automations.comment` /
  `f5bf3c2c-b970-8002-8008-874ebb85c7c2`
- Editor mapping/board: `automations.comment-editor` /
  `f5bf3c2c-b970-8002-8008-874ec2cb62fb`
- Page: `f5bf3c2c-b970-8002-8008-874ebb680e25`

The list board was exported and preserved. The editor retained its composition but was
updated in place:

- post selection now reads as a future/unavailable v1 capability;
- the formerly active-looking selection action is labelled disabled;
- image, film, audio and card replies are labelled future because the current backend
  action is text-only;
- text remains the supported reply type;
- helper text with ID `c48311ed-e700-80f8-8008-8820eedf7657` records the
  backend-authoritative 1000-character limit.

These corrections prevent the approved design from promising unsupported post scoping or
media actions while preserving the existing visual work.

## Validation

Both mapped pages/boards were resolved by stable ID after programmatic page activation.
No page names or active-page state were used as identity.
