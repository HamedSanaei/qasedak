# Graphify rule

Graphify is mandatory for AI agents because Qasedak is intended for long-running multi-agent development where repeated whole-repository reads waste tokens and increase inconsistency.

## Required lifecycle

1. Verify CLI: `graphify --version`.
2. Build graph once with `graphify . --no-viz`; later use `graphify . --update --no-viz`.
3. Ask at least one bounded task-specific question with `graphify query ... --budget 1200` before broad repository search.
4. Use graph paths/explanations for architectural navigation.
5. Record exact evidence with `scripts/record_graphify_evidence.py`.
6. Refresh graph after material structural changes before handoff.

## Failure policy

If Graphify is unavailable or unhealthy, stop before feature edits and report it. Only an explicit human override permits a bypass. Record the override and reason. Never invent a successful graph run.

## Versioned outputs

After M00-003, keep the useful text/JSON graph outputs versioned when produced by the installed Graphify version, while excluding heavy visualization/cache artifacts according to `.gitignore`.
