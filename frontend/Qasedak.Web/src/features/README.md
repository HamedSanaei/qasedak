# Frontend feature organization

Product behavior is organized by feature (`automations`, `inbox`, `instagram-accounts`, `contacts`, `billing`, `settings`) rather than by global component type. Add a feature only when its milestone begins. Shared generic UI belongs in `src/shared`; business-specific components stay local to their feature.
