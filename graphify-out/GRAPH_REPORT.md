# Graph Report - C:\Users\Hamed\Documents\Qasedak  (2026-08-24)

## Corpus Check
- cluster-only mode — file stats not available

## Summary
- 2678 nodes · 5329 edges · 188 communities (158 shown, 30 thin omitted)
- Extraction: 96% EXTRACTED · 4% INFERRED · 0% AMBIGUOUS · INFERRED: 203 edges (avg confidence: 0.8)
- Token cost: 0 input · 0 output

## Graph Freshness
- Built from commit: `b9b86678`
- Run `git rev-parse HEAD` and compare to check if the graph is stale.
- Run `graphify update .` after code changes (no API cost).

## Community Hubs (Navigation)
- .ExecuteAsync
- WebhookToConversationProjectionTests
- Task
- .SendTextAsync
- GraphInstagramOAuthClient
- WebhookMetrics
- .DispatchAsync
- MetaWebhookVerificationTests
- typography
- Task
- scripts
- Subscription
- IdentityAuthorizationTests
- Plan
- .Evaluate
- .CheckCountLimitAsync
- BillingDbContext
- .ExecuteAsync
- ConnectedAccount
- ExecuteAutomationTests
- Contact
- Task
- compilerOptions
- WorkspaceTests
- .ExecuteAsync
- IdentityDbContext
- .GetDetailAsync
- Qasedak.Modules.Conversations.Domain.Conversations
- Automation
- AutomationRun
- Qasedak.Modules.Billing.Domain
- Qasedak.Modules.Identity.Application.Authentication
- .UserAndCredentialRoundTripThroughRepository
- .ExecuteAsync
- CommentToDmAutomationFlowTests
- Qasedak.Modules.Automations.Application
- .HandleAsync
- AutomationAggregateTests
- Qasedak.Modules.Contacts.Application
- .Create
- .MapContactEndpoints
- EfContactRepository
- .DeliverAsync
- .Create
- .ProcessPendingAsync
- Qasedak.Modules.Instagram.Application.Webhooks
- .Create
- Conversation
- IIntegrationEvent
- .NewSut
- Pbkdf2PasswordHasher
- Qasedak.Modules.Automations.Infrastructure.Persistence.Migrations
- Qasedak.Modules.Contacts.Infrastructure.Persistence.Migrations
- Qasedak.Modules.Contacts.Infrastructure.Persistence
- .ExecuteAsync
- .ExecuteAsync
- ContactEndpointTests
- ConversationInboxEndpointTests
- ContactAggregateTests
- AutomationRunContracts.cs
- .ExecuteAsync
- .InspectAsync
- ContactsDbContext
- Qasedak.Modules.Contacts.Infrastructure
- Pbkdf2PasswordHasherTests
- IConnectedAccountRepository
- IWebhookPostIngestProcessor
- PostgreSqlFixture
- .NewStack
- .CheckActivationAllowedAsync
- Qasedak.Modules.Billing.Infrastructure
- .Create
- Qasedak.Modules.Instagram.Infrastructure.Persistence.Migrations
- Qasedak.slnx
- WebhookToContactProjectionTests
- Exception
- .ListByWorkspaceAsync
- .ActivateAsync
- InitialBillingCreation
- Migration
- .DispatchAsync
- WebhookInboxTests
- .NewClient
- CorrelationIds
- AutomationsDbContext
- User
- Workspace
- MetaWebhookEndpointTests
- .CollectChanges
- ModelSnapshot
- .InvokeAsync
- IContactRepository
- HmacSecurityTokenIssuer
- .GetAsync
- IWebhookInboxStore
- .IngestAsync
- Qasedak.Modules.Instagram.Infrastructure.Persistence
- EfConnectedAccountRepository
- .NewScope
- ConversationTests
- rehearse_deployment.py
- Qasedak.BuildingBlocks.Infrastructure
- .IsMemberAsync
- .Classify
- InstagramDbContext
- ApiPostgreSqlFixture
- AutomationRunLedgerTests
- RecordingInstagramMessagingClient
- .MapIdentityEndpoints
- Fact
- 20260823204008_InitialConversationsCreation.Designer.cs
- 20260823110059_InitialIdentityCreation.Designer.cs
- .Normalize
- EfWebhookInboxStore
- Qasedak.Api
- http
- .TryRecordAsync
- Qasedak.Modules.Identity.Application.Workspaces
- AuthorizationUrlBuilderPort.cs
- ContactTagNoteTests
- InMemoryUserRepository
- Sidebar.tsx
- rehearse_backup_restore.py
- .HandleRequirementAsync
- Qasedak.BuildingBlocks.Infrastructure/DependencyInjection.cs
- ConversationEndpoints
- EfConversationRepository
- .FindByIdAsync
- Qasedak.Modules.Instagram.Application.OAuth
- PostgreSqlFixture
- FixedClock
- PostgreSqlFixture
- penpot-sync.test.mjs
- IClock
- ContactEndpoints
- .TryRecordAsync
- MetaWebhookLogs
- .ReadAuditEntriesAsync
- DiagnosticsTests.cs
- Qasedak.Modules.Billing.UnitTests
- Entity.cs
- WebhookInboxEntry
- ApiSmokeTests
- CorrelationEndpointTests
- InstagramAuthorizationUrlBuilderTests
- AddContactInteractions
- AesGcmTokenProtector
- Qasedak.Api.CrossModule
- Qasedak.Modules.Contacts.UnitTests
- ScriptedHttpHandler
- check_architecture.py
- 20260823231137_InitialContactsCreation.Designer.cs
- .AddInstagramModule
- app/layout.tsx
- run
- check_environment_contract.py
- Qasedak.Modules.Conversations.Application/AssemblyMarker.cs
- Qasedak.Modules.Conversations.Domain/AssemblyMarker.cs
- Qasedak.Modules.Identity.Application/AssemblyMarker.cs
- Qasedak.Modules.Instagram.Application/AssemblyMarker.cs
- agent_finalize.py
- generate_manifest.py
- Qasedak.Modules.Automations.Domain/AssemblyMarker.cs
- next.config.ts
- next-env.d.ts
- repository-contract.test.mjs
- dev.sh script
- verify.sh script
- Guid
- AccessToken
- RecipientId
- Text
- Connect
- Disconnect
- HashSet
- IConfiguration
- IServiceCollection
- Path

## God Nodes (most connected - your core abstractions)
1. `Subscription` - 43 edges
2. `Contact` - 42 edges
3. `Automation` - 34 edges
4. `WorkspaceTests` - 27 edges
5. `Conversation` - 26 edges
6. `Plan` - 25 edges
7. `Workspace` - 24 edges
8. `Qasedak.Modules.Instagram.Application.Webhooks` - 23 edges
9. `AutomationRun` - 23 edges
10. `Qasedak.BuildingBlocks.Application` - 20 edges

## Surprising Connections (you probably didn't know these)
- `ApiPostgreSqlFixture` --references--> `Program`  [EXTRACTED]
  backend/tests/Qasedak.Api.IntegrationTests/ApiPostgreSqlFixture.cs → backend/Qasedak.Api/Program.cs
- `EfUserRepository` --implements--> `IUserRepository`  [EXTRACTED]
  backend/Modules/Identity/Qasedak.Modules.Identity.Infrastructure/Persistence/EfRepositories.cs → backend/Modules/Identity/Qasedak.Modules.Identity.Application/Authentication/AuthenticationContracts.cs
- `InMemoryUserRepository` --implements--> `IUserRepository`  [EXTRACTED]
  backend/tests/Qasedak.Modules.Identity.UnitTests/TestSupport/InMemoryUserRepository.cs → backend/Modules/Identity/Qasedak.Modules.Identity.Application/Authentication/AuthenticationContracts.cs
- `HmacSecurityTokenIssuer` --implements--> `ISecurityTokenIssuer`  [EXTRACTED]
  backend/Modules/Identity/Qasedak.Modules.Identity.Infrastructure/Authentication/AuthenticationAdapters.cs → backend/Modules/Identity/Qasedak.Modules.Identity.Application/Authentication/SecurityPorts.cs
- `EfWorkspaceRepository` --implements--> `IWorkspaceRepository`  [EXTRACTED]
  backend/Modules/Identity/Qasedak.Modules.Identity.Infrastructure/Persistence/EfRepositories.cs → backend/Modules/Identity/Qasedak.Modules.Identity.Application/Workspaces/IWorkspaceRepository.cs

## Import Cycles
- None detected.

## Communities (188 total, 30 thin omitted)

### Community 0 - ".ExecuteAsync"
Cohesion: 0.06
Nodes (38): AuditRedaction, CancellationToken, DateTimeOffset, Guid, Task, AuditEntry, IAuditTrail, CancellationToken (+30 more)

### Community 1 - "WebhookToConversationProjectionTests"
Cohesion: 0.06
Nodes (29): AuditDbContextFactory, DateTimeOffset, DbSet, Guid, ModelBuilder, string, AuditDbContext, AuditEntryRow (+21 more)

### Community 2 - "Task"
Cohesion: 0.10
Nodes (28): CancellationToken, ConnectedAccount, DateTimeOffset, Dictionary, Fact, Guid, IReadOnlyList, List (+20 more)

### Community 3 - ".SendTextAsync"
Cohesion: 0.08
Nodes (30): CancellationToken, Task, IInstagramMessagingClient, MessagingFailure, MessagingFailureReason, MessagingSendResult, CancellationToken, HttpClient (+22 more)

### Community 4 - "GraphInstagramOAuthClient"
Cohesion: 0.12
Nodes (18): CancellationToken, Task, CodeExchangeRequest, CodeExchangeResult, CodeExchangeSuccess, IMetaOAuthClient, LongLivedToken, LongLivedTokenResult (+10 more)

### Community 5 - "WebhookMetrics"
Cohesion: 0.06
Nodes (27): Action, HttpContext, IConfiguration, RateLimitPolicies, RiskClass, CancellationToken, string, Task (+19 more)

### Community 6 - ".DispatchAsync"
Cohesion: 0.08
Nodes (23): ExecutionStatus, CancellationToken, Guid, Task, AutomationCommentBridge, CancellationToken, DateTimeOffset, Guid (+15 more)

### Community 7 - "MetaWebhookVerificationTests"
Cohesion: 0.10
Nodes (16): WebhookSignatureFailure, WebhookSignatureResult, IWebhookSignatureVerifier, IWebhookSubscriptionValidator, WebhookSubscriptionFailure, WebhookSubscriptionResult, string, HmacWebhookSignatureVerifier (+8 more)

### Community 8 - "typography"
Cohesion: 0.05
Nodes (38): border.default, brand.accent, surface.page, surface.subtle, text.disabled, text.muted, text.nav, text.primary (+30 more)

### Community 9 - "Task"
Cohesion: 0.15
Nodes (16): CancellationToken, ConnectedAccount, DateTimeOffset, Dictionary, Fact, Guid, IReadOnlyList, Task (+8 more)

### Community 10 - "scripts"
Cohesion: 0.06
Nodes (33): eslint, eslint-config-next, dependencies, next, react, react-dom, devDependencies, eslint (+25 more)

### Community 11 - "Subscription"
Cohesion: 0.14
Nodes (11): DateTimeOffset, Guid, int, IReadOnlyList, List, Subscription, SubscriptionPeriod, SubscriptionStatus (+3 more)

### Community 12 - "IdentityAuthorizationTests"
Cohesion: 0.12
Nodes (17): Fact, Task, CreatedWorkspace, IdentityAuthorizationTests, LoginResponse, MemberDto, MembersResponse, MeResponse (+9 more)

### Community 13 - "Plan"
Cohesion: 0.13
Nodes (13): Guid, int, IReadOnlyList, List, Entitlement, Plan, DateTimeOffset, Fact (+5 more)

### Community 14 - ".Evaluate"
Cohesion: 0.20
Nodes (10): IReadOnlyList, AutomationEvaluator, RuleEvaluation, TriggerContext, AutomationCondition, DateTimeOffset, Fact, InlineData (+2 more)

### Community 15 - ".CheckCountLimitAsync"
Cohesion: 0.13
Nodes (16): CancellationToken, DateTimeOffset, Guid, string, Task, EntitlementDecision, EntitlementGate, CancellationToken (+8 more)

### Community 16 - "BillingDbContext"
Cohesion: 0.17
Nodes (16): CancellationToken, DateTimeOffset, DbSet, Guid, IReadOnlyList, List, ModelBuilder, string (+8 more)

### Community 17 - ".ExecuteAsync"
Cohesion: 0.20
Nodes (13): CancellationToken, Guid, Task, SendReplyCommand, SendReplyResult, CancellationToken, DateTimeOffset, Fact (+5 more)

### Community 18 - "ConnectedAccount"
Cohesion: 0.14
Nodes (13): CancellationToken, Guid, Task, AccountHealthEvaluation, EvaluateAccountHealthUseCase, AccountHealth, ConnectionPath, DateTimeOffset (+5 more)

### Community 19 - "ExecuteAutomationTests"
Cohesion: 0.18
Nodes (13): AutomationId, CancellationToken, DateTimeOffset, Fact, Guid, IReadOnlyList, List, string (+5 more)

### Community 20 - "Contact"
Cohesion: 0.14
Nodes (11): DateTimeOffset, Guid, int, IReadOnlyList, List, Contact, ContactNote, DateTimeOffset (+3 more)

### Community 21 - "Task"
Cohesion: 0.17
Nodes (11): CancellationToken, DateTimeOffset, Fact, Guid, IReadOnlyList, Task, EntitlementGateTests, FakePlans (+3 more)

### Community 22 - "compilerOptions"
Cohesion: 0.07
Nodes (27): compilerOptions, allowJs, esModuleInterop, incremental, isolatedModules, jsx, lib, module (+19 more)

### Community 23 - "WorkspaceTests"
Cohesion: 0.24
Nodes (3): Fact, Guid, WorkspaceTests

### Community 24 - ".ExecuteAsync"
Cohesion: 0.16
Nodes (14): CancellationToken, Task, ContactInteractionOutcome, ContactInteractionProjection, CancellationToken, DateTimeOffset, Fact, Guid (+6 more)

### Community 25 - "IdentityDbContext"
Cohesion: 0.12
Nodes (13): UserCredentials, string, IdentityDbContext, IdentityDbContextFactory, PostgreSqlContainer, string, Task, PostgreSqlFixture (+5 more)

### Community 26 - ".GetDetailAsync"
Cohesion: 0.12
Nodes (18): CancellationToken, Guid, IReadOnlyList, Messages, Row, Task, IConversationQueries, int (+10 more)

### Community 27 - "Qasedak.Modules.Conversations.Domain.Conversations"
Cohesion: 0.11
Nodes (13): string, TimeSpan, ReplyFailures, SendReplyUseCase, IConfiguration, IServiceCollection, DependencyInjection, EfConversationQueries (+5 more)

### Community 28 - "Automation"
Cohesion: 0.12
Nodes (16): DateTimeOffset, Guid, int, IReadOnlyList, List, Automation, AutomationStatus, AutomationVersion (+8 more)

### Community 29 - "AutomationRun"
Cohesion: 0.17
Nodes (12): IReadOnlyList, DateTimeOffset, Guid, IReadOnlyList, List, AutomationActionExecution, AutomationRun, AutomationRunStatus (+4 more)

### Community 30 - "Qasedak.Modules.Billing.Domain"
Cohesion: 0.11
Nodes (12): AssemblyMarker, AssemblyMarker, IConfiguration, IServiceCollection, DependencyInjection, BillingDbContextFactory, Qasedak.Modules.Billing.Infrastructure, Qasedak.Modules.Billing.Application (+4 more)

### Community 31 - "Qasedak.Modules.Identity.Application.Authentication"
Cohesion: 0.11
Nodes (14): RegisterUserUseCase, AssemblyMarker, string, IdentityAuthOptions, CreateWorkspaceRequest, LoginRequest, RegisterUserRequest, Qasedak.Modules.Identity.Infrastructure.Authentication (+6 more)

### Community 32 - ".UserAndCredentialRoundTripThroughRepository"
Cohesion: 0.25
Nodes (8): CancellationToken, Guid, Task, EfUserRepository, EfWorkspaceRepository, Fact, Task, IdentityPersistenceTests

### Community 33 - ".ExecuteAsync"
Cohesion: 0.13
Nodes (14): string, AccountFailures, ConnectionStateRecord, CancellationToken, Guid, IReadOnlyList, Task, ConnectAccountResult (+6 more)

### Community 34 - "CommentToDmAutomationFlowTests"
Cohesion: 0.20
Nodes (13): AccessToken, AutomationId, DateTimeOffset, Fact, Guid, List, RecipientId, string (+5 more)

### Community 35 - "Qasedak.Modules.Automations.Application"
Cohesion: 0.19
Nodes (9): AssemblyMarker, string, AutomationFailures, Qasedak.Modules.Automations.Domain, Qasedak.Modules.Automations.IntegrationTests, Qasedak.Modules.Automations.Infrastructure.Persistence, Qasedak.Modules.Automations.UnitTests, Qasedak.Modules.Automations.Domain.Definitions (+1 more)

### Community 36 - ".HandleAsync"
Cohesion: 0.15
Nodes (13): CancellationToken, Guid, Task, IUserRepository, Guid, int, string, AuthenticationFailures (+5 more)

### Community 37 - "AutomationAggregateTests"
Cohesion: 0.14
Nodes (14): int, ActionKind, AutomationAction, AutomationDefinition, AutomationTrigger, ConditionField, ConditionOperator, TriggerKind (+6 more)

### Community 38 - "Qasedak.Modules.Contacts.Application"
Cohesion: 0.13
Nodes (9): AssemblyMarker, AssemblyMarker, DateTimeOffset, ContactProjectionPersistenceTests, Qasedak.Modules.Contacts.Infrastructure.Endpoints, Qasedak.Modules.Contacts.Application, Qasedak.Modules.Contacts.IntegrationTests, Qasedak.Modules.Contacts.UnitTests (+1 more)

### Community 39 - ".Create"
Cohesion: 0.47
Nodes (4): DateTimeOffset, Fact, Task, AutomationPersistenceTests

### Community 40 - ".MapContactEndpoints"
Cohesion: 0.16
Nodes (14): CancellationToken, Guid, int, Task, ContactDetailRow, ContactFilter, ContactListRow, ContactPage (+6 more)

### Community 41 - "EfContactRepository"
Cohesion: 0.18
Nodes (10): Exception, ProjectContactInteractionUseCase, CancellationToken, Guid, IReadOnlyList, Task, EfContactRepository, Fact (+2 more)

### Community 42 - ".DeliverAsync"
Cohesion: 0.16
Nodes (12): CancellationToken, Task, ChannelDeliveryRequest, ChannelDeliveryResult, IConversationChannelGateway, CancellationToken, LoggerMessage, string (+4 more)

### Community 43 - ".Create"
Cohesion: 0.11
Nodes (11): int, WorkspaceName, ModelBuilder, Fact, InlineData, Theory, WorkspaceNameTests, IEnumerable (+3 more)

### Community 44 - ".ProcessPendingAsync"
Cohesion: 0.18
Nodes (11): CancellationToken, Task, ProcessPendingWebhookEventsUseCase, WebhookProcessingSummary, CancellationToken, DateTimeOffset, Fact, IReadOnlyList (+3 more)

### Community 45 - "Qasedak.Modules.Instagram.Application.Webhooks"
Cohesion: 0.24
Nodes (4): Qasedak.BuildingBlocks.Application, Qasedak.Modules.Instagram.Application.Webhooks, Qasedak.Modules.Instagram.Infrastructure.Webhooks, Qasedak.Modules.Instagram.UnitTests

### Community 46 - ".Create"
Cohesion: 0.23
Nodes (9): Fact, Task, ContactCrmPersistenceTests, DateTimeOffset, Fact, Task, ContactPersistenceTests, Queries (+1 more)

### Community 47 - "Conversation"
Cohesion: 0.21
Nodes (12): DateTimeOffset, Guid, int, IReadOnlyList, List, Conversation, MessageState, ConversationStatus (+4 more)

### Community 48 - "IIntegrationEvent"
Cohesion: 0.20
Nodes (10): CancellationToken, Task, IIntegrationEventDispatcher, IIntegrationEvent, InstagramCommentCreated, InstagramMentionCreated, InstagramMessageReceived, NormalizationOutcome (+2 more)

### Community 49 - ".NewSut"
Cohesion: 0.26
Nodes (8): Authenticate, Fact, InlineData, string, Task, Theory, AuthenticationUseCaseTests, Register

### Community 50 - "Pbkdf2PasswordHasher"
Cohesion: 0.10
Nodes (13): AuthenticateResult, AuthenticationHandler, AuthenticationSchemeOptions, Guid, IPasswordHasher, ISecurityTokenIssuer, SecurityToken, TokenValidationResult (+5 more)

### Community 51 - "Qasedak.Modules.Automations.Infrastructure.Persistence.Migrations"
Cohesion: 0.12
Nodes (9): MigrationBuilder, ModelBuilder, InitialAutomationsCreation, InitialAutomationsCreation, MigrationBuilder, ModelBuilder, AddAutomationRuns, AddAutomationRuns (+1 more)

### Community 52 - "Qasedak.Modules.Contacts.Infrastructure.Persistence.Migrations"
Cohesion: 0.12
Nodes (9): MigrationBuilder, InitialContactsCreation, ModelBuilder, AddContactInteractions, MigrationBuilder, ModelBuilder, AddContactTagsAndNotes, AddContactTagsAndNotes (+1 more)

### Community 53 - "Qasedak.Modules.Contacts.Infrastructure.Persistence"
Cohesion: 0.13
Nodes (9): IConfiguration, IServiceCollection, DependencyInjection, Fact, Task, RateLimitEndpointTests, Qasedak.Api.IntegrationTests, Qasedak.Modules.Contacts.Infrastructure.Persistence (+1 more)

### Community 54 - ".ExecuteAsync"
Cohesion: 0.20
Nodes (10): CancellationToken, Guid, Task, IConversationRepository, CancellationToken, Guid, Task, InboundMessageProjection (+2 more)

### Community 55 - ".ExecuteAsync"
Cohesion: 0.18
Nodes (11): CancellationToken, Guid, string, Task, CreateWorkspaceResult, CreateWorkspaceUseCase, ListWorkspaceMembersUseCase, WorkspaceFailures (+3 more)

### Community 56 - "ContactEndpointTests"
Cohesion: 0.20
Nodes (11): DateTimeOffset, Fact, Guid, HttpClient, HttpResponseMessage, string, Task, ContactEndpointTests (+3 more)

### Community 57 - "ConversationInboxEndpointTests"
Cohesion: 0.20
Nodes (11): Fact, Guid, HttpClient, Task, ConversationInboxEndpointTests, InboxDetailResponse, InboxItem, InboxMessage (+3 more)

### Community 58 - "ContactAggregateTests"
Cohesion: 0.25
Nodes (4): DateTimeOffset, Fact, Guid, ContactAggregateTests

### Community 59 - "AutomationRunContracts.cs"
Cohesion: 0.21
Nodes (11): string, ActionDispatch, ActionResult, ExecutionFailures, IAutomationActionDispatcher, CancellationToken, Task, AutomationChannelDispatcher (+3 more)

### Community 60 - ".ExecuteAsync"
Cohesion: 0.21
Nodes (10): CancellationToken, Guid, Task, IAutomationRunRepository, CancellationToken, Exception, Task, ExecuteAutomationUseCase (+2 more)

### Community 61 - ".InspectAsync"
Cohesion: 0.17
Nodes (10): CancellationToken, Task, IMetaTokenInspector, TokenInspection, TokenInspectionKind, CancellationToken, HttpClient, string (+2 more)

### Community 62 - "ContactsDbContext"
Cohesion: 0.26
Nodes (13): DateTimeOffset, DbSet, Guid, List, ModelBuilder, string, ContactIdentityRow, ContactInteractionRow (+5 more)

### Community 63 - "Qasedak.Modules.Contacts.Infrastructure"
Cohesion: 0.14
Nodes (16): Qasedak.Modules.Contacts.Infrastructure, Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Design, Microsoft.EntityFrameworkCore.Relational, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk, Qasedak.Modules.Contacts.IntegrationTests, Microsoft.EntityFrameworkCore (+8 more)

### Community 64 - "Pbkdf2PasswordHasherTests"
Cohesion: 0.39
Nodes (4): Fact, InlineData, Theory, Pbkdf2PasswordHasherTests

### Community 65 - "IConnectedAccountRepository"
Cohesion: 0.33
Nodes (7): CancellationToken, ConnectedAccount, Guid, IReadOnlyList, Task, IConnectedAccountRepository, IProtectedTokenStore

### Community 66 - "IWebhookPostIngestProcessor"
Cohesion: 0.15
Nodes (10): CancellationToken, Task, IWebhookPostIngestProcessor, NullWebhookPostIngestProcessor, IEndpointRouteBuilder, int, MetaWebhookEndpoints, CancellationToken (+2 more)

### Community 67 - "PostgreSqlFixture"
Cohesion: 0.15
Nodes (12): PostgreSqlContainer, string, Task, PostgreSqlFixture, PostgresTestEnvironment, PostgreSqlContainer, string, Task (+4 more)

### Community 68 - ".NewStack"
Cohesion: 0.28
Nodes (9): Connect, DateTimeOffset, Disconnect, Fact, List, Task, FixedClock, InstagramPersistenceTests (+1 more)

### Community 69 - ".CheckActivationAllowedAsync"
Cohesion: 0.13
Nodes (9): IConfiguration, IServiceCollection, DependencyInjection, CancellationToken, Guid, Task, PermissiveActivationPolicy, AutomationsDbContextFactory (+1 more)

### Community 70 - "Qasedak.Modules.Billing.Infrastructure"
Cohesion: 0.14
Nodes (15): Qasedak.Modules.Billing.Infrastructure, Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Design, Microsoft.EntityFrameworkCore.Relational, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk, Qasedak.Modules.Billing.IntegrationTests, Microsoft.EntityFrameworkCore (+7 more)

### Community 71 - ".Create"
Cohesion: 0.16
Nodes (9): Fact, InlineData, Theory, EmailAddressTests, Fact, InlineData, string, Theory (+1 more)

### Community 72 - "Qasedak.Modules.Instagram.Infrastructure.Persistence.Migrations"
Cohesion: 0.13
Nodes (7): ModelBuilder, InitialInstagramCreation, ModelBuilder, AddWebhookInbox, ModelBuilder, InstagramDbContextModelSnapshot, Qasedak.Modules.Instagram.Infrastructure.Persistence.Migrations

### Community 73 - "Qasedak.slnx"
Cohesion: 0.13
Nodes (14): Qasedak.Modules.Conversations.Application, Qasedak.Modules.Conversations.Domain, Qasedak.Modules.Identity.Application, Qasedak.Modules.Identity.Domain, Qasedak.Modules.Instagram.Application, Qasedak.Modules.Instagram.Domain, Qasedak.Api.IntegrationTests, Qasedak.Modules.Automations.IntegrationTests (+6 more)

### Community 74 - "WebhookToContactProjectionTests"
Cohesion: 0.26
Nodes (7): DateTimeOffset, Fact, Guid, HttpResponseMessage, string, Task, WebhookToContactProjectionTests

### Community 75 - "Exception"
Cohesion: 0.14
Nodes (8): AutomationsDomainException, BillingDomainException, ConversationsDomainException, DomainRuleViolationException, AssemblyMarker, InstagramDomainException, Qasedak.Modules.Instagram.Domain, Exception

### Community 76 - ".ListByWorkspaceAsync"
Cohesion: 0.43
Nodes (5): CancellationToken, Guid, IReadOnlyList, Task, IAutomationRepository

### Community 77 - ".ActivateAsync"
Cohesion: 0.36
Nodes (8): CancellationToken, DateTimeOffset, Guid, Task, ResolveWorkspaceEntitlementsUseCase, StartSubscriptionUseCase, WorkspaceEntitlements, TimeSpan

### Community 78 - "InitialBillingCreation"
Cohesion: 0.15
Nodes (7): MigrationBuilder, ModelBuilder, InitialBillingCreation, InitialBillingCreation, ModelBuilder, BillingDbContextModelSnapshot, Qasedak.Modules.Billing.Infrastructure.Persistence.Migrations

### Community 79 - "Migration"
Cohesion: 0.22
Nodes (6): InitialConversationsCreation, InitialIdentityCreation, InitialInstagramCreation, AddWebhookInbox, Migration, MigrationBuilder

### Community 80 - ".DispatchAsync"
Cohesion: 0.40
Nodes (4): CancellationToken, LoggerMessage, Task, LoggingIntegrationEventDispatcher

### Community 81 - "WebhookInboxTests"
Cohesion: 0.30
Nodes (8): int, InboxWebhookIngester, DateTimeOffset, Fact, MetaWebhookNotification, Task, FixedClock, WebhookInboxTests

### Community 82 - ".NewClient"
Cohesion: 0.41
Nodes (5): Fact, HttpStatusCode, string, Task, GraphInstagramOAuthClientTests

### Community 83 - "CorrelationIds"
Cohesion: 0.23
Nodes (6): string, CorrelationIds, InlineData, Theory, CorrelationIdTests, Regex

### Community 84 - "AutomationsDbContext"
Cohesion: 0.31
Nodes (11): AutomationActionStatus, DateTimeOffset, Guid, List, ModelBuilder, string, AutomationRow, AutomationRunActionRow (+3 more)

### Community 85 - "User"
Cohesion: 0.21
Nodes (8): int, EmailAddress, Guid, User, Fact, InlineData, Theory, UserTests

### Community 86 - "Workspace"
Cohesion: 0.36
Nodes (8): Guid, Membership, MembershipRole, Guid, Workspace, Entity, IReadOnlyCollection, List

### Community 87 - "MetaWebhookEndpointTests"
Cohesion: 0.36
Nodes (4): Fact, string, Task, MetaWebhookEndpointTests

### Community 88 - ".CollectChanges"
Cohesion: 0.27
Nodes (7): ArrayEnumerator, UnrecognizedWebhookFragment, DateTimeOffset, List, JsonElementExtensions, MetaPayloadNormalizer, JsonElement

### Community 89 - "ModelSnapshot"
Cohesion: 0.17
Nodes (7): ModelBuilder, AuditDbContextModelSnapshot, ModelBuilder, AutomationsDbContextModelSnapshot, ModelBuilder, ContactsDbContextModelSnapshot, ModelSnapshot

### Community 90 - ".InvokeAsync"
Cohesion: 0.29
Nodes (7): CorrelationContext, ICorrelationContext, HttpContext, Task, CorrelationContextAccessor, CorrelationMiddleware, ICorrelationContextAccessor

### Community 91 - "IContactRepository"
Cohesion: 0.29
Nodes (7): CancellationToken, Guid, IReadOnlyList, string, Task, ContactFailures, IContactRepository

### Community 92 - "HmacSecurityTokenIssuer"
Cohesion: 0.26
Nodes (6): Guid, IClock, HmacSecurityTokenIssuer, TokenPayload, Lifetime, SigningKey

### Community 93 - ".GetAsync"
Cohesion: 0.33
Nodes (5): ITokenProtector, CancellationToken, Guid, Task, ProtectedTokenStore

### Community 94 - "IWebhookInboxStore"
Cohesion: 0.33
Nodes (6): CancellationToken, DateTimeOffset, IReadOnlyList, Task, InboxEntryRecord, IWebhookInboxStore

### Community 95 - ".IngestAsync"
Cohesion: 0.23
Nodes (7): CancellationToken, Task, IMetaWebhookIngester, MetaWebhookNotification, WebhookIngestionResult, CancellationToken, Task

### Community 96 - "Qasedak.Modules.Instagram.Infrastructure.Persistence"
Cohesion: 0.16
Nodes (9): string, TokenProtectionOptions, Qasedak.Modules.Instagram.Infrastructure.Protection, Qasedak.Modules.Instagram.IntegrationTests, Qasedak.Modules.Instagram.Infrastructure.Messaging, Qasedak.Modules.Instagram.Application.Messaging, Qasedak.Modules.Instagram.Infrastructure, Qasedak.Modules.Instagram.Application.Accounts (+1 more)

### Community 97 - "EfConnectedAccountRepository"
Cohesion: 0.41
Nodes (6): CancellationToken, ConnectedAccount, Guid, IReadOnlyList, Task, EfConnectedAccountRepository

### Community 98 - ".NewScope"
Cohesion: 0.32
Nodes (7): DateTimeOffset, Fact, Task, BillingPersistenceTests, Context, Plans, Subscriptions

### Community 99 - "ConversationTests"
Cohesion: 0.38
Nodes (3): DateTimeOffset, Fact, ConversationTests

### Community 100 - "rehearse_deployment.py"
Cohesion: 0.41
Nodes (11): docker(), http_get(), main(), migrate_via_image(), CompletedProcess, Apply all module migrations through the host toolchain (design-time factories)., run(), smoke() (+3 more)

### Community 101 - "Qasedak.BuildingBlocks.Infrastructure"
Cohesion: 0.18
Nodes (11): Qasedak.BuildingBlocks.Infrastructure, Microsoft.EntityFrameworkCore, Microsoft.EntityFrameworkCore.Design, Microsoft.EntityFrameworkCore.Relational, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.NET.Sdk, Qasedak.BuildingBlocks.UnitTests, Microsoft.NET.Test.Sdk (+3 more)

### Community 102 - ".IsMemberAsync"
Cohesion: 0.18
Nodes (8): CancellationToken, Guid, Task, IWorkspaceAccessChecker, CancellationToken, Guid, Task, EfWorkspaceAccessChecker

### Community 103 - ".Classify"
Cohesion: 0.31
Nodes (5): Fact, MetaErrorTaxonomyTests, InlineData, Task, Theory

### Community 104 - "InstagramDbContext"
Cohesion: 0.22
Nodes (6): ModelBuilder, string, InstagramDbContext, StoredAccountToken, InstagramDbContextFactory, ConnectedAccount

### Community 105 - "ApiPostgreSqlFixture"
Cohesion: 0.25
Nodes (7): Guid, HttpClient, PostgreSqlContainer, string, Task, ApiPostgreSqlFixture, ApiTestEnvironment

### Community 106 - "AutomationRunLedgerTests"
Cohesion: 0.38
Nodes (5): DateTimeOffset, Fact, string, Task, AutomationRunLedgerTests

### Community 107 - "RecordingInstagramMessagingClient"
Cohesion: 0.20
Nodes (9): AccessToken, CancellationToken, HashSet, List, RecordingInstagramMessagingClient, IInstagramMessagingClient, MessagingSendResult, RecipientId (+1 more)

### Community 108 - ".MapIdentityEndpoints"
Cohesion: 0.27
Nodes (6): AuthorizeAttribute, Guid, IdentityEndpoints, ClaimsPrincipal, IEndpointRouteBuilder, IResult

### Community 109 - "Fact"
Cohesion: 0.31
Nodes (4): string, Sensitive, Fact, SensitiveTests

### Community 110 - "20260823204008_InitialConversationsCreation.Designer.cs"
Cohesion: 0.20
Nodes (5): ModelBuilder, InitialConversationsCreation, ModelBuilder, ConversationsDbContextModelSnapshot, Qasedak.Modules.Conversations.Infrastructure.Persistence.Migrations

### Community 111 - "20260823110059_InitialIdentityCreation.Designer.cs"
Cohesion: 0.20
Nodes (5): ModelBuilder, InitialIdentityCreation, ModelBuilder, IdentityDbContextModelSnapshot, Qasedak.Modules.Identity.Infrastructure.Persistence.Migrations

### Community 113 - "EfWebhookInboxStore"
Cohesion: 0.40
Nodes (5): CancellationToken, DateTimeOffset, IReadOnlyList, Task, EfWebhookInboxStore

### Community 114 - "Qasedak.Api"
Cohesion: 0.22
Nodes (9): Program, Qasedak.Api, Microsoft.EntityFrameworkCore.Design, Qasedak.Modules.Automations.Infrastructure, Qasedak.Modules.Conversations.Infrastructure, Qasedak.Modules.Identity.Infrastructure, Qasedak.Modules.Instagram.Infrastructure, Microsoft.AspNetCore.OpenApi (+1 more)

### Community 115 - "http"
Cohesion: 0.20
Nodes (9): ASPNETCORE_ENVIRONMENT, applicationUrl, commandName, dotnetRunMessages, environmentVariables, launchBrowser, profiles, http (+1 more)

### Community 116 - ".TryRecordAsync"
Cohesion: 0.28
Nodes (6): CancellationToken, Task, ContactInteractionEntry, IContactInteractionLedger, HashSet, FakeLedger

### Community 117 - "Qasedak.Modules.Identity.Application.Workspaces"
Cohesion: 0.25
Nodes (6): IConfiguration, IServiceCollection, DependencyInjection, Qasedak.Modules.Identity.Application.Workspaces, Qasedak.Modules.Identity.Infrastructure.Security, Qasedak.Modules.Identity.Infrastructure

### Community 118 - "AuthorizationUrlBuilderPort.cs"
Cohesion: 0.33
Nodes (6): string, AuthorizationUrl, AuthorizationUrlRequest, IAuthorizationUrlBuilder, InstagramAuthorizationScopes, InstagramAuthorizationUrlBuilder

### Community 119 - "ContactTagNoteTests"
Cohesion: 0.47
Nodes (3): DateTimeOffset, Fact, ContactTagNoteTests

### Community 120 - "InMemoryUserRepository"
Cohesion: 0.47
Nodes (4): CancellationToken, Guid, Task, InMemoryUserRepository

### Community 121 - "Sidebar.tsx"
Cohesion: 0.22
Nodes (5): navItems, subItems, SidebarNavItem, SidebarProps, SidebarSubItem

### Community 122 - "rehearse_backup_restore.py"
Cohesion: 0.44
Nodes (8): docker(), main(), psql(), psql_stdin(), CompletedProcess, Run SQL via stdin — immune to Windows arg-quoting mangling of double quotes., run(), wait_healthy()

### Community 123 - ".HandleRequirementAsync"
Cohesion: 0.32
Nodes (6): AuthorizationHandler, AuthorizationHandlerContext, Task, WorkspaceMemberRequirement, WorkspaceMembershipAuthorizationHandler, IAuthorizationRequirement

### Community 124 - "Qasedak.BuildingBlocks.Infrastructure/DependencyInjection.cs"
Cohesion: 0.22
Nodes (5): IServiceCollection, DependencyInjection, Qasedak.BuildingBlocks.Infrastructure, Qasedak.BuildingBlocks.Infrastructure.Diagnostics, IApplicationBuilder

### Community 125 - "ConversationEndpoints"
Cohesion: 0.29
Nodes (5): IEndpointRouteBuilder, IResult, ConversationEndpoints, ReplyRequest, Qasedak.Modules.Conversations.Infrastructure.Endpoints

### Community 126 - "EfConversationRepository"
Cohesion: 0.50
Nodes (4): CancellationToken, Guid, Task, EfConversationRepository

### Community 127 - ".FindByIdAsync"
Cohesion: 0.39
Nodes (4): CancellationToken, Guid, Task, IWorkspaceRepository

### Community 128 - "Qasedak.Modules.Instagram.Application.OAuth"
Cohesion: 0.36
Nodes (4): string, MetaOAuthOptions, Qasedak.Modules.Instagram.Application.OAuth, Qasedak.Modules.Instagram.Infrastructure.OAuth

### Community 129 - "PostgreSqlFixture"
Cohesion: 0.32
Nodes (5): PostgreSqlContainer, string, Task, PostgreSqlFixture, PostgresTestEnvironment

### Community 130 - "FixedClock"
Cohesion: 0.29
Nodes (6): FixedClock, DateTimeOffset, FixedClock, Qasedak.Modules.Instagram.UnitTests.TestSupport, DateTimeOffset, IClock

### Community 131 - "PostgreSqlFixture"
Cohesion: 0.32
Nodes (5): string, Task, PostgreSqlFixture, PostgresTestEnvironment, PostgreSqlContainer

### Community 132 - "penpot-sync.test.mjs"
Cohesion: 0.25
Nodes (5): APPROVAL_STATUSES, manifest, manifestPath, root, SYNC_STATUSES

### Community 133 - "IClock"
Cohesion: 0.33
Nodes (5): DateTimeOffset, IClock, DateTimeOffset, SystemClock, Qasedak.BuildingBlocks.Application

### Community 134 - "ContactEndpoints"
Cohesion: 0.29
Nodes (5): ContactsDomainException, IResult, ContactEndpoints, NoteRequest, TagRequest

### Community 135 - ".TryRecordAsync"
Cohesion: 0.33
Nodes (4): CancellationToken, Exception, Task, EfContactInteractionLedger

### Community 137 - ".ReadAuditEntriesAsync"
Cohesion: 0.57
Nodes (3): Fact, Task, AuditTrailEndpointTests

### Community 138 - "DiagnosticsTests.cs"
Cohesion: 0.25
Nodes (5): EntityTests, TestEntity, Qasedak.BuildingBlocks.UnitTests, Fact, Guid

### Community 139 - "Qasedak.Modules.Billing.UnitTests"
Cohesion: 0.29
Nodes (7): Qasedak.Modules.Billing.UnitTests, Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk, Qasedak.Modules.Automations.Application, Qasedak.Modules.Billing.Application

### Community 140 - "Entity.cs"
Cohesion: 0.33
Nodes (4): Entity, DateTimeOffset, IDomainEvent, Qasedak.BuildingBlocks.Domain

### Community 142 - "ApiSmokeTests"
Cohesion: 0.33
Nodes (5): ApiSmokeTests, HttpClient, IClassFixture, Program, WebApplicationFactory

### Community 143 - "CorrelationEndpointTests"
Cohesion: 0.47
Nodes (3): Fact, Task, CorrelationEndpointTests

### Community 147 - "Qasedak.Api.CrossModule"
Cohesion: 0.40
Nodes (3): string, BillingActivationPolicyAdapter, Qasedak.Api.CrossModule

### Community 148 - "Qasedak.Modules.Contacts.UnitTests"
Cohesion: 0.40
Nodes (5): Qasedak.Modules.Contacts.UnitTests, Microsoft.NET.Test.Sdk, xunit, xunit.runner.visualstudio, Microsoft.NET.Sdk

### Community 149 - "ScriptedHttpHandler"
Cohesion: 0.50
Nodes (4): CancellationToken, HttpResponseMessage, ScriptedHttpHandler, HttpRequestMessage

### Community 150 - "check_architecture.py"
Cohesion: 0.70
Nodes (4): main(), project_kind(), Path, resolve_reference()

### Community 152 - ".AddInstagramModule"
Cohesion: 0.50
Nodes (3): IConfiguration, IServiceCollection, DependencyInjection

### Community 154 - "run"
Cohesion: 0.67
Nodes (3): Path, main(), run()

### Community 155 - "check_environment_contract.py"
Cohesion: 0.83
Nodes (3): collect_code_keys(), doc_covers(), main()

## Knowledge Gaps
- **204 isolated node(s):** `Entity`, `AssemblyMarker`, `AssemblyMarker`, `AssemblyMarker`, `AssemblyMarker` (+199 more)
  These have ≤1 connection - possible missing edges or undocumented components.
- **30 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `Qasedak.BuildingBlocks.Application` connect `Qasedak.Modules.Instagram.Application.Webhooks` to `.ExecuteAsync`, `.ExecuteAsync`, `FixedClock`, `Qasedak.Modules.Automations.Application`, `Qasedak.Modules.Instagram.Infrastructure.Persistence`, `Qasedak.BuildingBlocks.Infrastructure`, `.DispatchAsync`, `Qasedak.slnx`, `.ProcessPendingAsync`, `ConnectedAccount`, `.ExecuteAsync`, `Qasedak.Modules.Conversations.Domain.Conversations`, `Qasedak.BuildingBlocks.Infrastructure/DependencyInjection.cs`, `Qasedak.Modules.Identity.Application.Authentication`?**
  _High betweenness centrality (0.248) - this node is a cross-community bridge._
- **Why does `Qasedak.Modules.Instagram.Application.Webhooks` connect `Qasedak.Modules.Instagram.Application.Webhooks` to `Qasedak.Modules.Instagram.Infrastructure.Persistence`, `IWebhookPostIngestProcessor`, `Qasedak.Modules.Automations.Application`, `.DispatchAsync`, `MetaWebhookVerificationTests`, `.ProcessPendingAsync`, `IIntegrationEvent`, `.CollectChanges`, `IWebhookInboxStore`, `.IngestAsync`?**
  _High betweenness centrality (0.102) - this node is a cross-community bridge._
- **Why does `Qasedak.Api.IntegrationTests` connect `Qasedak.Modules.Contacts.Infrastructure.Persistence` to `.ExecuteAsync`, `Qasedak.Modules.Automations.Application`, `IdentityAuthorizationTests`, `ApiSmokeTests`, `CorrelationEndpointTests`, `ConversationInboxEndpointTests`, `Qasedak.Modules.Conversations.Domain.Conversations`?**
  _High betweenness centrality (0.078) - this node is a cross-community bridge._
- **What connects `Entity`, `AssemblyMarker`, `AssemblyMarker` to the rest of the system?**
  _204 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `.ExecuteAsync` be split into smaller, more focused modules?**
  _Cohesion score 0.05632360471070148 - nodes in this community are weakly interconnected._
- **Should `WebhookToConversationProjectionTests` be split into smaller, more focused modules?**
  _Cohesion score 0.05697278911564626 - nodes in this community are weakly interconnected._
- **Should `Task` be split into smaller, more focused modules?**
  _Cohesion score 0.09523809523809523 - nodes in this community are weakly interconnected._