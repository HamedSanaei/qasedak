# Multi-agent protocol

Qasedak is expected to be developed by multiple AI agents across many sessions. Repository state is therefore authoritative and chat history is disposable.

## Task ownership

- One active task per agent unless the task packet explicitly allows a bounded group.
- Use `.agent-state/locks/<TASK_ID>.lock` for human/agent coordination when concurrent agents operate on the same checkout.
- Never edit another agent's active task scope without explicit coordination.
- Prefer small task packets with explicit files, acceptance criteria, tests and a suggested commit.

## Handoff discipline

A handoff is incomplete until code, tests, task status, `PROJECT_STATE.json`, Graphify evidence, `HANDOFF.md`, and the manifest agree. The next agent must be able to continue using repository files only.

## No hidden state

Important decisions, incomplete work, temporary compromises, external prerequisites and verification gaps must be written to the repository before ending an instruction.
