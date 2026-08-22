# Security policy

Do not report security vulnerabilities in public issues. Use the repository owner's private security contact/channel.

## Baseline rules

- Never commit secrets, Meta tokens, connection-string passwords, private keys or production `.env` files.
- Access tokens must be encrypted/protected at rest before M03 is considered complete.
- Authorization is enforced server-side per workspace; UI checks are never an authorization boundary.
- Webhook authenticity, idempotency and replay behavior require explicit tests.
- Dependency and CodeQL scans are CI gates.
- Log redaction is mandatory for credentials, authorization headers, tokens and user-sensitive message contents where not required.
