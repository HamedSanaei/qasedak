# M08-000 — Qasedak authentication and workspace design

- Canonical file UUID: `c269caa0-e456-818c-8008-85a77340be64`
- Connected file UUID: `c269caa0-e456-818c-8008-85a77340be64`
- File verification: PASS
- Requested mappings: `identity.auth`, `identity.register`,
  `identity.workspace-states`
- Was target page manually opened by human? **NO**
- Was page activated programmatically? **YES**
- Penpot revision: `null` (not exposed)

## Stable targets

Page `Qasedak · Identity & Workspace`:
`c48311ed-e700-80f8-8008-881f0352eb6a`

| Board | Stable UUID |
|---|---|
| Identity / Login / Desktop | `c48311ed-e700-80f8-8008-881f0372388a` |
| Identity / Register / Desktop | `c48311ed-e700-80f8-8008-881f075bc2f7` |
| Identity / Login / Mobile | `c48311ed-e700-80f8-8008-881f0b6a5a33` |
| Identity / Register / Mobile | `c48311ed-e700-80f8-8008-881f0cbbe326` |
| Identity / Validation, Session & Workspace States | `c48311ed-e700-80f8-8008-881f0ea618ba` |

Mapped reusable components:

- Text Field: `c48311ed-e700-80f8-8008-881eaa0bba2d`
- Primary Button: `c48311ed-e700-80f8-8008-881ea9ca0747`
- Status Alert: `c48311ed-e700-80f8-8008-881eaa60589f`

## Contract represented

The temporary GetCode screen
`324404a7-ad1e-8048-8008-8776b27352cb` remains in Penpot as historical evidence but
is no longer the Qasedak mapping. It assumed mobile/OTP. The approved Qasedak flow uses
the current backend contract:

- login: email + password;
- register: display name + email + password;
- password: 10–128 characters and not only letters/digits;
- identical invalid-credential treatment for unknown email and wrong password;
- workspace creation after registration, with the creator becoming Owner;
- validation, loading, server error, expired session/401, forbidden/403, no-workspace
  and workspace-ready states.

No OTP, password-reset API or unsupported account-recovery flow was introduced.

## Visual system

RTL/Vazirmatn; desktop 1440 and mobile 390; Qasedak accent `#BE0183`, deep brand
`#670048`, canvas `#F6F7F9`, product border `#E3E5E8`; 10px inputs and 16px panels.
Focus/error states include text and visible outlines rather than color-only meaning.
The desktop login and mobile login boards were exported to PNG and visually inspected.

## Implementation expectation

- `/login` → `src/features/identity/ui/LoginScreen.tsx`
- `/register` → `src/features/identity/ui/RegisterScreen.tsx`
- `/onboarding/workspace` →
  `src/features/identity/ui/WorkspaceOnboardingScreen.tsx`

These paths are intentionally `planned`; M08-002 owns implementation.

## Validation

- live page/board/component resolution: PASS
- offline registry resolution for `identity.auth`: PASS
- `penpotRevision`: `null`, not fabricated
