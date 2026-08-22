# Deployment contract

Qasedak publishes two immutable application images and deploys them as one Compose stack with PostgreSQL.

- `qasedak-api`: ASP.NET Core API/composition root.
- `qasedak-web`: Next.js standalone runtime.
- `postgres:18-alpine`: stateful database; PostgreSQL 18 official images persist at the `/var/lib/postgresql` parent volume.

Production secrets must not be committed. Prefer a secrets manager/platform secret injection; the Compose file's local secret-file pattern is a portable baseline, not the final platform choice.

Deployment must use an explicit immutable `IMAGE_TAG`, run migration/preflight procedures defined by the owning milestone, verify readiness/smoke checks, and preserve a rollback target.
