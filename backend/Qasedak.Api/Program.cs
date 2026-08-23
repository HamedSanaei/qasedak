using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Qasedak.BuildingBlocks.Infrastructure;
using Qasedak.Modules.Automations.Infrastructure;
using Qasedak.Modules.Billing.Infrastructure;
using Qasedak.Modules.Contacts.Infrastructure;
using Qasedak.Modules.Conversations.Infrastructure;
using Qasedak.Modules.Conversations.Infrastructure.Endpoints;
using Qasedak.Modules.Identity.Infrastructure;
using Qasedak.Modules.Identity.Infrastructure.Endpoints;
using Qasedak.Modules.Instagram.Infrastructure;
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

// Composition-root bridges: normalized Instagram events feed the Conversations inbox
// (explicit cross-module contracts; neither module references the other).
builder.Services.AddScoped<Qasedak.Modules.Instagram.Application.Webhooks.IIntegrationEventDispatcher,
    Qasedak.Api.CrossModule.InstagramConversationBridge>();
builder.Services.AddScoped<Qasedak.Modules.Instagram.Application.Webhooks.IWebhookPostIngestProcessor,
    Qasedak.Api.CrossModule.ConversationsPostIngestAdapter>();
// Outbound replies: Conversations' channel gateway is filled by Instagram's messaging client.
builder.Services.AddScoped<Qasedak.Modules.Conversations.Application.Conversations.IConversationChannelGateway,
    Qasedak.Api.CrossModule.InstagramReplyGateway>();

var app = builder.Build();

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

app.Run();

public partial class Program;
