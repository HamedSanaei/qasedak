# Graph Report - .  (2026-08-23)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 282 nodes · 299 edges · 44 communities (23 shown, 21 thin omitted)
- Extraction: 100% EXTRACTED · 0% INFERRED · 0% AMBIGUOUS
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `ad96b242`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- Qasedak.slnx
- Program.cs
- compilerOptions
- scripts
- devDependencies
- IClock
- ApiSmokeTests
- http
- EntityTests.cs
- Entity.cs
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
- next-env.d.ts
- repository-contract.test.mjs
- dev.sh script
- verify.sh script
- Path

## God Nodes (most connected - your core abstractions)
1. `compilerOptions` - 16 edges
2. `Qasedak.BuildingBlocks.Domain` - 11 edges
3. `Qasedak.Api` - 11 edges
4. `Qasedak.BuildingBlocks.Application` - 10 edges
5. `Qasedak.BuildingBlocks.Infrastructure` - 10 edges
6. `scripts` - 8 edges
7. `Qasedak.Api.IntegrationTests` - 7 edges
8. `Qasedak.Modules.Automations.Infrastructure` - 6 edges
9. `Qasedak.Modules.Billing.Infrastructure` - 6 edges
10. `Qasedak.Modules.Contacts.Infrastructure` - 6 edges

## Surprising Connections (you probably didn't know these)
- `SystemClock` --implements--> `IClock`  [EXTRACTED]
  backend/BuildingBlocks/Qasedak.BuildingBlocks.Infrastructure/SystemClock.cs → backend/BuildingBlocks/Qasedak.BuildingBlocks.Application/IClock.cs

## Import Cycles
- None detected.

## Communities (44 total, 21 thin omitted)

### Community 0 - "Qasedak.slnx"
Cohesion: 0.09
Nodes (45): Qasedak.BuildingBlocks.Application, Microsoft.NET.Sdk, Qasedak.BuildingBlocks.Domain, Microsoft.NET.Sdk, Qasedak.BuildingBlocks.Infrastructure, Microsoft.NET.Sdk, Qasedak.Modules.Automations.Application, Microsoft.NET.Sdk (+37 more)

### Community 1 - "Program.cs"
Cohesion: 0.06
Nodes (21): IConfiguration, IServiceCollection, DependencyInjection, IConfiguration, IServiceCollection, DependencyInjection, IConfiguration, IServiceCollection (+13 more)

### Community 2 - "compilerOptions"
Cohesion: 0.07
Nodes (27): compilerOptions, allowJs, esModuleInterop, incremental, isolatedModules, jsx, lib, module (+19 more)

### Community 3 - "scripts"
Cohesion: 0.10
Nodes (20): dependencies, next, react, react-dom, engines, node, name, private (+12 more)

### Community 4 - "devDependencies"
Cohesion: 0.15
Nodes (13): eslint, eslint-config-next, devDependencies, eslint, eslint-config-next, @types/node, @types/react, @types/react-dom (+5 more)

### Community 5 - "IClock"
Cohesion: 0.20
Nodes (8): DateTimeOffset, IClock, IServiceCollection, DependencyInjection, DateTimeOffset, SystemClock, Qasedak.BuildingBlocks.Application, Qasedak.BuildingBlocks.Infrastructure

### Community 6 - "ApiSmokeTests"
Cohesion: 0.18
Nodes (9): ApiSmokeTests, Qasedak.Api.IntegrationTests, HttpClient, IClassFixture, InlineData, Program, Task, Theory (+1 more)

### Community 7 - "http"
Cohesion: 0.20
Nodes (9): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, profiles, http (+1 more)

### Community 8 - "EntityTests.cs"
Cohesion: 0.25
Nodes (6): EntityTests, TestEntity, Qasedak.BuildingBlocks.UnitTests, Entity, Fact, Guid

### Community 9 - "Entity.cs"
Cohesion: 0.33
Nodes (4): Entity, DateTimeOffset, IDomainEvent, Qasedak.BuildingBlocks.Domain

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
Nodes (3): Path, main(), run()

## Knowledge Gaps
- **114 isolated node(s):** `Microsoft.NET.Sdk`, `Entity`, `Microsoft.NET.Sdk`, `Microsoft.NET.Sdk`, `Qasedak.Modules.Automations.Application` (+109 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **21 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Qasedak.BuildingBlocks.Domain` connect `Qasedak.slnx` to `EntityTests.cs`, `Qasedak.BuildingBlocks.UnitTests`?**
  _High betweenness centrality (0.015) - this node is a cross-community bridge._
- **Why does `Qasedak.BuildingBlocks.Infrastructure` connect `IClock` to `Program.cs`?**
  _High betweenness centrality (0.011) - this node is a cross-community bridge._
- **Why does `devDependencies` connect `devDependencies` to `scripts`?**
  _High betweenness centrality (0.008) - this node is a cross-community bridge._
- **What connects `Microsoft.NET.Sdk`, `Entity`, `Microsoft.NET.Sdk` to the rest of the system?**
  _114 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Qasedak.slnx` be split into smaller, more focused modules?**
  _Cohesion score 0.08695652173913043 - nodes in this community are weakly interconnected._
- **Should `Program.cs` be split into smaller, more focused modules?**
  _Cohesion score 0.0625 - nodes in this community are weakly interconnected._
- **Should `compilerOptions` be split into smaller, more focused modules?**
  _Cohesion score 0.07142857142857142 - nodes in this community are weakly interconnected._