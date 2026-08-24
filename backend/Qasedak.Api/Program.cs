using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Qasedak.BuildingBlocks.Infrastructure;
using Qasedak.Modules.Automations.Infrastructure;
using Qasedak.Modules.Automations.Infrastructure.Endpoints;
using Qasedak.Modules.Billing.Infrastructure;
using Qasedak.Modules.Contacts.Infrastructure;
using Qasedak.Modules.Contacts.Infrastructure.Endpoints;
using Qasedak.Modules.Conversations.Infrastructure;
using Qasedak.Modules.Conversations.Infrastructure.Endpoints;
using Qasedak.Modules.Identity.Infrastructure;
using Qasedak.Modules.Identity.Infrastructure.Endpoints;
using Qasedak.Modules.Instagram.Infrastructure;
using Qasedak.Modules.Instagram.Infrastructure.Endpoints;
using Qasedak.Modules.Instagram.Infrastructure.Webhooks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

builder.Services
    .AddQasedakBuildingBlocks()
    .AddIdentityModule(builder.Configuration)
    .AddInstagramModule(builder.Configuration)
    .AddAutomationsModule(builder.Configuration)
    .AddConversationsModule(builder.Configuration)
    .AddContactsModule(builder.Configuration)
    .AddBillingModule(builder.Configuration);

// Append-only audit trail for sensitive actions (auth/billing/automation). Bound only when
// the composition root configures an "Audit" connection string; module code depends on the
// BuildingBlocks port alone.
var auditConnectionString = builder.Configuration.GetConnectionString("Audit");
if (!string.IsNullOrWhiteSpace(auditConnectionString))
{
    builder.Services.AddQasedakAuditTrail(auditConnectionString);
}

// Workspace-membership policy: every /workspaces/{workspaceId}/... endpoint group requires
// the caller to be a member of the addressed workspace (tenant isolation, uniform 403).
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler,
    Qasedak.Api.CrossModule.WorkspaceMembershipAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("workspace-member", policy => policy.RequireAuthenticatedUser()
        .AddRequirements(new Qasedak.Api.CrossModule.WorkspaceMemberRequirement()));
});

// Composition-root bridges: normalized Instagram events feed the Conversations inbox
// (explicit cross-module contracts; neither module references the other). The module
// resolves one dispatcher, so the composition root fans out to all consumers.
builder.Services.AddScoped<Qasedak.Api.CrossModule.InstagramConversationBridge>();
builder.Services.AddScoped<Qasedak.Api.CrossModule.AutomationCommentBridge>();
builder.Services.AddScoped<Qasedak.Api.CrossModule.ContactsInteractionBridge>();
builder.Services.AddScoped<Qasedak.Modules.Instagram.Application.Webhooks.IIntegrationEventDispatcher>(sp =>
    new Qasedak.Api.CrossModule.FanOutIntegrationEventDispatcher(
    [
        sp.GetRequiredService<Qasedak.Api.CrossModule.InstagramConversationBridge>(),
        sp.GetRequiredService<Qasedak.Api.CrossModule.AutomationCommentBridge>(),
        sp.GetRequiredService<Qasedak.Api.CrossModule.ContactsInteractionBridge>(),
    ]));
builder.Services.AddScoped<Qasedak.Modules.Instagram.Application.Webhooks.IWebhookPostIngestProcessor,
    Qasedak.Api.CrossModule.ConversationsPostIngestAdapter>();
// Outbound replies: Conversations' channel gateway is filled by Instagram's messaging client.
builder.Services.AddScoped<Qasedak.Modules.Conversations.Application.Conversations.IConversationChannelGateway,
    Qasedak.Api.CrossModule.InstagramReplyGateway>();
// Automations: comment events drive the idempotent execution engine; the channel-neutral
// dispatcher port is filled by the same outbound gateway (24h window enforced there).
builder.Services.AddScoped<Qasedak.Modules.Automations.Application.IAutomationActionDispatcher,
    Qasedak.Api.CrossModule.AutomationChannelDispatcher>();
// Entitlement enforcement: automation activation is gated by the workspace's plan limits
// (server-owned, fail-closed) through the composition-root policy adapter.
builder.Services.AddScoped<Qasedak.Modules.Billing.Application.EntitlementGate>();
builder.Services.AddScoped<Qasedak.Modules.Automations.Application.IAutomationActivationPolicy,
    Qasedak.Api.CrossModule.BillingActivationPolicyAdapter>();
builder.Services.AddScoped<Qasedak.Modules.Automations.Application.ExecuteAutomationUseCase>();

// Risk-class rate limiting: public/authenticated/webhook/sensitive budgets, 429+Retry-After.
builder.Services.AddRateLimiter(options => Qasedak.BuildingBlocks.Infrastructure.RateLimiting.RateLimitPolicies.Configure(options, builder.Configuration));

var app = builder.Build();

// Correlation first: every downstream log line and response carries X-Correlation-Id.
app.UseQasedakCorrelation();
app.UseRateLimiter();
app.UseExceptionHandler();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

app.MapGet("/api/v1/system", () => Results.Ok(new
{
    name = "Qasedak API",
    architecture = "Modular Monolith",
    status = "scaffold"
}));

app.MapIdentityEndpoints();
app.MapMetaWebhookEndpoints();
app.MapConversationEndpoints();
app.MapContactEndpoints();
app.MapConnectionEndpoints();
app.MapAutomationEndpoints();

app.Run();

public partial class Program;
