# Graph Report - .  (2026-08-23)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 277 nodes · 297 edges · 43 communities (24 shown, 19 thin omitted)
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS
- Token cost: 0 input · 0 output

## Community Hubs (Navigation)
- Qasedak.slnx
- Program.cs
- compilerOptions
- scripts
- EntityTests.cs
- devDependencies
- IClock
- http
- .AddAutomationsModule
- .AddContactsModule
- .AddInstagramModule
- Qasedak.Api.IntegrationTests
- Qasedak.BuildingBlocks.UnitTests
- check_architecture.py
- run
- Qasedak.Modules.Automations.Application/AssemblyMarker.cs
- Qasedak.Modules.Automations.Domain/AssemblyMarker.cs
- Qasedak.Modules.Billing.Application/AssemblyMarker.cs
- Qasedak.Modules.Billing.Domain/AssemblyMarker.cs
- Qasedak.Modules.Contacts.Application/AssemblyMarker.cs
- Qasedak.Modules.Contacts.Domain/AssemblyMarker.cs
- Qasedak.Modules.Conversations.Application/AssemblyMarker.cs
- Qasedak.Modules.Conversations.Domain/AssemblyMarker.cs
- Qasedak.Modules.Identity.Application/AssemblyMarker.cs
- Qasedak.Modules.Identity.Domain/AssemblyMarker.cs
- Qasedak.Modules.Instagram.Application/AssemblyMarker.cs
- Qasedak.Modules.Instagram.Domain/AssemblyMarker.cs
- layout.tsx
- agent_finalize.py
- generate_manifest.py
- next.config.ts
- repository-contract.test.mjs
- dev.sh script
- verify.sh script

## God Nodes (most connected - your core abstractions)
1. `compilerOptions` - 16 edges
2. `Qasedak.Api` - 11 edges
3. `Qasedak.BuildingBlocks.Application` - 10 edges
4. `Qasedak.BuildingBlocks.Domain` - 10 edges
5. `Qasedak.BuildingBlocks.Infrastructure` - 10 edges
6. `scripts` - 8 edges
7. `Qasedak.Api.IntegrationTests` - 7 edges
8. `Qasedak.Modules.Automations.Infrastructure` - 6 edges
9. `Qasedak.Modules.Billing.Infrastructure` - 6 edges
10. `Qasedak.Modules.Contacts.Infrastructure` - 6 edges

## Surprising Connections (you probably didn't know these)
- `ApiSmokeTests` --references--> `Program`  [EXTRACTED]
  backend/tests/Qasedak.Api.IntegrationTests/ApiSmokeTests.cs → backend/Qasedak.Api/Program.cs
- `SystemClock` --implements--> `IClock`  [EXTRACTED]
  backend/BuildingBlocks/Qasedak.BuildingBlocks.Infrastructure/SystemClock.cs → backend/BuildingBlocks/Qasedak.BuildingBlocks.Application/IClock.cs
- `TestEntity` --inherits--> `Entity`  [EXTRACTED]
  backend/tests/Qasedak.BuildingBlocks.UnitTests/EntityTests.cs → backend/BuildingBlocks/Qasedak.BuildingBlocks.Domain/Entity.cs

## Import Cycles
- None detected.

## Communities (43 total, 19 thin omitted)

### Community 0 - "Qasedak.slnx"
Cohesion: 0.09
Nodes (45): Qasedak.BuildingBlocks.Application, Microsoft.NET.Sdk, Qasedak.BuildingBlocks.Domain, Microsoft.NET.Sdk, Qasedak.BuildingBlocks.Infrastructure, Microsoft.NET.Sdk, Qasedak.Modules.Automations.Application, Microsoft.NET.Sdk (+37 more)

### Community 1 - "Program.cs"
Cohesion: 0.07
Nodes (21): IConfiguration, IServiceCollection, DependencyInjection, IConfiguration, IServiceCollection, DependencyInjection, IConfiguration, IServiceCollection (+13 more)

### Community 2 - "compilerOptions"
Cohesion: 0.07
Nodes (26): compilerOptions, allowJs, esModuleInterop, incremental, isolatedModules, jsx, lib, module (+18 more)

### Community 3 - "scripts"
Cohesion: 0.10
Nodes (20): dependencies, next, react, react-dom, engines, node, name, private (+12 more)

### Community 4 - "EntityTests.cs"
Cohesion: 0.17
Nodes (9): Entity, DateTimeOffset, IDomainEvent, EntityTests, TestEntity, Qasedak.BuildingBlocks.UnitTests, Qasedak.BuildingBlocks.Domain, Fact (+1 more)

### Community 5 - "devDependencies"
Cohesion: 0.15
Nodes (13): eslint, eslint-config-next, devDependencies, eslint, eslint-config-next, @types/node, @types/react, @types/react-dom (+5 more)

### Community 6 - "IClock"
Cohesion: 0.20
Nodes (8): DateTimeOffset, IClock, IServiceCollection, DependencyInjection, DateTimeOffset, SystemClock, Qasedak.BuildingBlocks.Application, Qasedak.BuildingBlocks.Infrastructure

### Community 7 - "http"
Cohesion: 0.20
Nodes (9): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, profiles, http (+1 more)

### Community 8 - ".AddAutomationsModule"
Cohesion: 0.33
Nodes (4): IConfiguration, IServiceCollection, DependencyInjection, Qasedak.Modules.Automations.Infrastructure

### Community 9 - ".AddContactsModule"
Cohesion: 0.33
Nodes (4): IConfiguration, IServiceCollection, DependencyInjection, Qasedak.Modules.Contacts.Infrastructure

### Community 10 - ".AddInstagramModule"
Cohesion: 0.33
Nodes (4): IConfiguration, IServiceCollection, DependencyInjection, Qasedak.Modules.Instagram.Infrastructure

### Community 11 - "Qasedak.Api.IntegrationTests"
Cohesion: 0.33
Nodes (6): Qasedak.Api.IntegrationTests, Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk, Microsoft.AspNetCore.Mvc.Testing

### Community 12 - "Qasedak.BuildingBlocks.UnitTests"
Cohesion: 0.40
Nodes (5): Qasedak.BuildingBlocks.UnitTests, Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 13 - "check_architecture.py"
Cohesion: 0.70
Nodes (4): main(), project_kind(), Path, resolve_reference()

### Community 14 - "run"
Cohesion: 0.67
Nodes (3): main(), Path, run()

## Knowledge Gaps
- **111 isolated node(s):** `Microsoft.NET.Sdk`, `Microsoft.NET.Sdk`, `Microsoft.NET.Sdk`, `Qasedak.Modules.Automations.Application`, `AssemblyMarker` (+106 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **19 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Qasedak.BuildingBlocks.Infrastructure` connect `IClock` to `Program.cs`?**
  _High betweenness centrality (0.014) - this node is a cross-community bridge._
- **What connects `Microsoft.NET.Sdk`, `Microsoft.NET.Sdk`, `Microsoft.NET.Sdk` to the rest of the system?**
  _111 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Qasedak.slnx` be split into smaller, more focused modules?**
  _Cohesion score 0.08695652173913043 - nodes in this community are weakly interconnected._
- **Should `Program.cs` be split into smaller, more focused modules?**
  _Cohesion score 0.06666666666666667 - nodes in this community are weakly interconnected._
- **Should `compilerOptions` be split into smaller, more focused modules?**
  _Cohesion score 0.07407407407407407 - nodes in this community are weakly interconnected._
- **Should `scripts` be split into smaller, more focused modules?**
  _Cohesion score 0.09523809523809523 - nodes in this community are weakly interconnected._