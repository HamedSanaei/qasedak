# Automations module

This module owns the **Automations** business capability. It follows Clean Architecture internally.

```text
Infrastructure -> Application -> Domain
```

Direct project references to other business modules are forbidden. Cross-module collaboration must use an explicit contract/event and an ADR when the coupling is architectural. Persistence belongs to this module's Infrastructure project and, when introduced, owns the PostgreSQL `automations` schema.
