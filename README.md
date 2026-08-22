# Qasedak

Qasedak is an Instagram automation SaaS starter built for long-lived, AI-assisted development.

## Architecture

- **Backend:** ASP.NET Core Web API on .NET 10.
- **Style:** Modular Monolith. Every business module owns its Domain, Application and Infrastructure projects.
- **Dependency rule:** Infrastructure → Application → Domain. Domain has no framework, persistence or transport dependencies.
- **Frontend:** a separate Next.js application. It communicates with the backend only through HTTP/API contracts.
- **Database:** PostgreSQL 18. One physical database initially; each module owns a logical schema.
- **Delivery:** GitHub Actions, container images, and Docker Compose.
- **Design:** Penpot is the source of truth for approved UI screens; approved designs are implemented in Next.js.

## Repository map

```text
backend/                     ASP.NET Core modular monolith
frontend/Qasedak.Web/        Next.js frontend
docs/                        engineering documents and project state
docs/fa/                     eight printable Persian RTL HTML documents
.agents/                     agent protocol, rules and templates
.agent-state/                machine-readable agent execution state
scripts/                     architecture, state and agent guardrails
deploy/                      production Compose contract
.github/                     CI, image publishing and security automation
```

## Mandatory AI-agent workflow

All coding agents must read `AGENTS.md` before touching the repository. Graphify is mandatory for code-navigation and token-efficient context. Broad repository reading before Graphify is prohibited unless a human explicitly records a bypass.

The generation environment used to create this starter did not have network access or Graphify installed, so no fake graph or evidence is included. The first task on a real workstation is `M00-003`, which initializes and verifies Graphify before feature development.

## First workstation setup

```bash
# Graphify package/CLI (official project uses package graphifyy and command graphify)
pip install graphifyy
graphify install
graphify codex install
graphify . --no-viz

# verify repository guardrails
python scripts/verify.py

# local stack
docker compose up --build
```

See `docs/project/STATUS.md`, `docs/project/TASKS.md`, and `docs/project/HANDOFF.md` before starting any task.

## Deployment model

The source repository keeps frontend and backend completely separate. Production publishes two immutable images (`qasedak-api` and `qasedak-web`) and runs them as one Compose application with PostgreSQL. This avoids placing ASP.NET and Node in one multi-process container while retaining one-command deployment.
