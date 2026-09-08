# M13-015 — Meta App Review / Advanced Access checklist

**First-party contract retrieval:** 2026-09-08
**Repository approval evidence:** none. Do not mark an item Approved from code/CI/deploy status alone.

## 1. Access model

Qasedak uses **Instagram API with Instagram Login**, Business Login for Instagram, Instagram User access tokens and configured Graph version `v26.0` on `graph.instagram.com`.

Standard Access is the development/test route for professional Instagram accounts owned/managed by the app operator and added as app test assets. Qasedak is a multi-customer SaaS; serving professional accounts not owned/managed by the app operator requires **Advanced Access / App Review** under the freshly verified current Meta rules.

Current external status:

| External gate | Status | Gate type |
|---|---|---|
| Meta App Review | Unknown | External Meta approval |
| Advanced Access for requested permissions | Unknown | External Meta approval |
| Business Verification, if required by the current app/review state | Unknown | External Meta approval |
| production app Live mode / production subscriptions | Unknown until operator verifies App Dashboard | Production-only |
| designated production Meta test account | Not established in repository | Manual / Production-only |

## 2. Permission review packet

| Permission | Feature requiring it | Why Qasedak needs it | Standard Access test path | Advanced Access production requirement | Review status | Review evidence / demo route | Test-account / reviewer notes | External blocker |
|---|---|---|---|---|---|---|---|---|
| `instagram_business_basic` | connection, profile, media and common account reads | establish exact professional identity and read account/media data used by shipped surfaces | connect an operator-owned/managed professional account through `/dashboard/instagram/connections`, verify profile and media catalog | required for third-party customer professional accounts | Unknown | screen recording: connect → exact profile → media list; explain token is server-side only | professional test account must be assigned appropriate app/account role and controllable by reviewer | App Review / Advanced Access |
| `instagram_business_manage_messages` | Direct messages, inbound-DM automations, postback/reveal, delayed follow-up, conversation history | Qasedak responds only after a valid user messaging basis and projects inbound messaging into Inbox/Automations | test participant first messages the professional test account; show inbound Inbox item → automation → one Direct response; show bounded history sync | required for external-customer messaging accounts | Unknown | screen recording: user initiates DM → Qasedak receives signed webhook → automation reply; separate postback/reveal route if requested | reviewer participant must be able to initiate a real test conversation; do not rely on a comment to open the DM window | App Review / Advanced Access |
| `instagram_business_manage_comments` | comment webhooks, comment automation, Private Reply, Public Reply, reconciliation | react to comments on the connected professional account and execute the documented reply operations | create test media/comment; show webhook-normalized comment → automation → Private Reply; show Public Reply only where review needs it | required for external-customer comment accounts; current Meta access rules also govern comments/live_comments webhook delivery | Unknown | screen recording: test comment → Qasedak automation → one Private Reply within policy; optionally safe Public Reply | use disposable test media/comment; Live test only if Meta review explicitly needs `live_comments` evidence | App Review / Advanced Access |
| `instagram_business_manage_insights` | account insights, media insights, follower snapshots/history | provide shipped analytics without broadening account privileges | open Insights on operator-owned/managed professional test account; show real zero vs NoData and media/account views | required for external-customer analytics | Unknown | screen recording: account Insights → media Insights → local follower history snapshot | reviewer needs a professional account with available metrics; NoData is valid and must not be presented as zero | App Review / Advanced Access |

## 3. Least privilege

Qasedak does **not** request `instagram_business_content_publish`. Publishing is not an M13 feature. The permission constant exists only so compliance code/docs can name the provider capability; `InstagramAuthorizationScopes.Default` excludes it.

Also do not request Ads, Tagging, Human Agent or unrelated webhook fields merely because Meta exposes them. Ads and Tagging are unavailable under the current Instagram Login surface; Human Agent is not an automation bypass.

## 4. Reviewer test assets

Before submission, a human operator must verify without placing credentials in the repository:

- Meta App exists and the reviewer/test users have the roles required by the current App Dashboard.
- Instagram **professional** test account is available and is the account intentionally connected to Qasedak.
- a separate test participant can message/comment on that professional account.
- all requested permissions above are granted to the test flow.
- required webhook fields are configured: `comments`, `live_comments`, `messages`, `messaging_postbacks`, `messaging_seen`.
- test media/comment exists for comment review flows.
- a test DM conversation is initiated by the participant before demonstrating ordinary Direct messaging.
- account-side message access / Connected Tools settings required by current Meta messaging docs are enabled.
- no customer account/token is substituted for a missing test asset.

Credentials, tokens, reviewer passwords and app secret values must never be committed or pasted into the review checklist.
