using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Qasedak.BuildingBlocks.Domain;
using Qasedak.Modules.Automations.Application;
using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;

namespace Qasedak.Modules.Automations.Infrastructure.Endpoints;

/// <summary>
/// Workspace-scoped automation CRUD + lifecycle surface for the M08-005 builder UI.
/// Thin composition over tested domain/application behavior; every route is guarded by
/// the workspace-member policy and foreign workspaces are indistinguishable from absent.
/// </summary>
public static class AutomationEndpoints
{
    public static IEndpointRouteBuilder MapAutomationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var automations = endpoints.MapGroup("/api/v1/workspaces/{workspaceId:guid}/automations")
            .WithTags("Automations")
            .RequireAuthorization("workspace-member");

        automations.MapGet(string.Empty, async (
            Guid workspaceId,
            IAutomationRepository repository,
            CancellationToken cancellationToken) =>
        {
            var items = await repository.ListByWorkspaceAsync(workspaceId, cancellationToken);
            return Results.Ok(new
            {
                items = items.Select(a => new
                {
                    id = a.Id,
                    name = a.Name,
                    channelAccountId = a.ChannelAccountId?.Value,
                    status = a.Status.ToString(),
                    currentVersionNumber = a.CurrentVersionNumber,
                    triggerKind = a.CurrentDefinition.Trigger.Kind.ToString(),
                    keywordFilters = a.CurrentDefinition.Trigger.KeywordFilters,
                    actionCount = a.CurrentDefinition.Actions.Count,
                    createdAtUtc = a.CreatedAtUtc,
                    activatedAtUtc = a.ActivatedAtUtc,
                }),
            });
        });

        automations.MapGet("/{automationId:guid}", async (
            Guid workspaceId,
            Guid automationId,
            IAutomationRepository repository,
            CancellationToken cancellationToken) =>
        {
            var automation = await repository.FindByIdAsync(automationId, cancellationToken);
            return automation is null || automation.WorkspaceId != workspaceId
                ? Results.NotFound(new { code = AutomationFailures.NotFound })
                : Results.Ok(AutomationResponse.From(automation));
        });

        automations.MapPost(string.Empty, async (
            Guid workspaceId,
            SaveAutomationRequest request,
            IAutomationRepository repository,
            CancellationToken cancellationToken) =>
        {
            if (!DefinitionMapper.TryMap(request.Definition, out var definition, out var error))
            {
                return Results.Json(new { code = error }, statusCode: StatusCodes.Status400BadRequest);
            }

            ChannelAccountId? channelAccountId = null;
            if (request.ChannelAccountId is not null)
            {
                if (request.ChannelAccountId == Guid.Empty)
                {
                    return Results.Json(new { code = "automation.accountInvalid" }, statusCode: StatusCodes.Status400BadRequest);
                }

                channelAccountId = new ChannelAccountId(request.ChannelAccountId.Value);
            }

            var automation = Automation.Create(
                Guid.CreateVersion7(), workspaceId, request.Name ?? string.Empty, definition!, DateTimeOffset.UtcNow, channelAccountId);
            await repository.SaveChangesAsync(automation, cancellationToken);
            return Results.Created(
                $"/api/v1/workspaces/{workspaceId}/automations/{automation.Id}",
                AutomationResponse.From(automation));
        });

        // Draft-only revision of the definition (frozen/terminal states are 409s below).
        automations.MapPut("/{automationId:guid}", async (
            Guid workspaceId,
            Guid automationId,
            SaveAutomationRequest request,
            IAutomationRepository repository,
            CancellationToken cancellationToken) =>
        {
            var automation = await repository.FindByIdAsync(automationId, cancellationToken);
            if (automation is null || automation.WorkspaceId != workspaceId)
            {
                return Results.NotFound(new { code = AutomationFailures.NotFound });
            }

            // Account binding is create-time immutable: accepting a different binding
            // here would silently change what historical versions mean. Rebind by
            // creating a new automation (M13-014 surfaces that flow).
            if (request.ChannelAccountId is not null
                && request.ChannelAccountId != automation.ChannelAccountId?.Value)
            {
                return Results.Json(new { code = "automation.bindingImmutable" }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (!DefinitionMapper.TryMap(request.Definition, out var definition, out var error))
            {
                return Results.Json(new { code = error }, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                automation.ReviseDraftDefinition(definition!, DateTimeOffset.UtcNow);
                await repository.SaveChangesAsync(automation, cancellationToken);
                return Results.Ok(AutomationResponse.From(automation));
            }
            catch (AutomationsDomainException exception)
            {
                return MapDomainFailure(exception);
            }
        });

        // Policy-checked activation: billing denials surface as their own stable codes.
        automations.MapPost("/{automationId:guid}/activate", async (
            Guid workspaceId,
            Guid automationId,
            ActivateAutomationUseCase useCase,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var automation = await useCase.ExecuteAsync(workspaceId, automationId, DateTimeOffset.UtcNow, cancellationToken);
                return Results.Ok(AutomationResponse.From(automation));
            }
            catch (AutomationsDomainException exception)
            {
                return MapDomainFailure(exception);
            }
        });

        // Pause: Active → Draft (history intact; execution refuses while paused).
        automations.MapPost("/{automationId:guid}/deactivate", async (
            Guid workspaceId,
            Guid automationId,
            IAutomationRepository repository,
            CancellationToken cancellationToken) =>
        {
            var automation = await repository.FindByIdAsync(automationId, cancellationToken);
            if (automation is null || automation.WorkspaceId != workspaceId)
            {
                return Results.NotFound(new { code = AutomationFailures.NotFound });
            }

            try
            {
                automation.Unpublish(DateTimeOffset.UtcNow);
                await repository.SaveChangesAsync(automation, cancellationToken);
                return Results.Ok(AutomationResponse.From(automation));
            }
            catch (AutomationsDomainException exception)
            {
                return MapDomainFailure(exception);
            }
        });

        // Terminal retirement.
        automations.MapDelete("/{automationId:guid}", async (
            Guid workspaceId,
            Guid automationId,
            IAutomationRepository repository,
            CancellationToken cancellationToken) =>
        {
            var automation = await repository.FindByIdAsync(automationId, cancellationToken);
            if (automation is null || automation.WorkspaceId != workspaceId)
            {
                return Results.NotFound(new { code = AutomationFailures.NotFound });
            }

            try
            {
                automation.Disable(DateTimeOffset.UtcNow);
                await repository.SaveChangesAsync(automation, cancellationToken);
                return Results.NoContent();
            }
            catch (AutomationsDomainException exception)
            {
                return MapDomainFailure(exception);
            }
        });

        return endpoints;
    }

    private static IResult MapDomainFailure(AutomationsDomainException exception) => exception.RuleCode switch
    {
        AutomationFailures.NotFound => Results.Json(new { code = exception.RuleCode }, statusCode: StatusCodes.Status404NotFound),
        "automation.alreadyActive" or "automation.notActive" or "automation.alreadyDisabled" or
            "automation.disabled" or "automation.versionFrozen" or
            "billing.subscriptionRequired" or "billing.limitExceeded" =>
            Results.Json(new { code = exception.RuleCode }, statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(new { code = exception.RuleCode }, statusCode: StatusCodes.Status400BadRequest),
    };

    public sealed record SaveAutomationRequest(string? Name, DefinitionRequest? Definition, Guid? ChannelAccountId);

    public sealed record DefinitionRequest(
        string? TriggerKind,
        IReadOnlyList<string>? KeywordFilters,
        IReadOnlyList<ConditionRequest>? Conditions,
        IReadOnlyList<ActionRequest>? Actions);

    public sealed record ConditionRequest(string? Field, string? Operator, string? ExpectedValue);

    public sealed record ActionRequest(string? Kind, string? MessageText);

    public sealed record ConditionResponse(string Field, string Operator, string ExpectedValue);

    public sealed record ActionResponse(string Kind, string MessageText);

    public sealed record DefinitionResponse(
        string TriggerKind,
        IReadOnlyList<string> KeywordFilters,
        IEnumerable<ConditionResponse> Conditions,
        IEnumerable<ActionResponse> Actions);

    public sealed record AutomationResponse(
        Guid Id,
        string Name,
        Guid? ChannelAccountId,
        string Status,
        int CurrentVersionNumber,
        bool CurrentVersionFrozen,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ActivatedAtUtc,
        DateTimeOffset? DisabledAtUtc,
        DefinitionResponse Definition)
    {
        public static AutomationResponse From(Automation automation) => new(
            automation.Id,
            automation.Name,
            automation.ChannelAccountId?.Value,
            automation.Status.ToString(),
            automation.CurrentVersionNumber,
            automation.CurrentVersionFrozen,
            automation.CreatedAtUtc,
            automation.ActivatedAtUtc,
            automation.DisabledAtUtc,
            new DefinitionResponse(
                automation.CurrentDefinition.Trigger.Kind.ToString(),
                automation.CurrentDefinition.Trigger.KeywordFilters,
                automation.CurrentDefinition.Conditions.Select(c => new ConditionResponse(
                    c.Field.ToString(), c.Operator.ToString(), c.ExpectedValue)),
                automation.CurrentDefinition.Actions.Select(a => new ActionResponse(
                    a.Kind.ToString(), a.MessageText))));
    }

    /// <summary>Maps wire payloads onto domain value objects; unknown enum names fail closed.</summary>
    public static class DefinitionMapper
    {
        public static bool TryMap(DefinitionRequest? request, out AutomationDefinition? definition, out string? errorCode)
        {
            definition = null;
            errorCode = null;
            if (request is null)
            {
                errorCode = "automation.definitionRequired";
                return false;
            }

            if (!Enum.TryParse<TriggerKind>(request.TriggerKind, ignoreCase: true, out var triggerKind) ||
                !Enum.IsDefined(triggerKind))
            {
                errorCode = "automation.triggerKindInvalid";
                return false;
            }

            var keywords = (request.KeywordFilters ?? []).Select(k => k.Trim()).Where(k => k.Length > 0).ToArray();
            if (keywords.Length > AutomationDefinition.MaxKeywordFilters)
            {
                errorCode = "automation.tooManyKeywordFilters";
                return false;
            }

            var conditions = new List<AutomationCondition>();
            foreach (var condition in request.Conditions ?? [])
            {
                if (!Enum.TryParse<ConditionField>(condition?.Field, ignoreCase: true, out var field) ||
                    !Enum.IsDefined(field) ||
                    !Enum.TryParse<ConditionOperator>(condition?.Operator, ignoreCase: true, out var op) ||
                    !Enum.IsDefined(op))
                {
                    errorCode = "automation.conditionInvalid";
                    return false;
                }

                conditions.Add(new AutomationCondition(field, op, condition!.ExpectedValue ?? string.Empty));
            }

            var actions = new List<AutomationAction>();
            foreach (var action in request.Actions ?? [])
            {
                if (!Enum.TryParse<ActionKind>(action?.Kind, ignoreCase: true, out var actionKind) ||
                    !Enum.IsDefined(actionKind))
                {
                    errorCode = "automation.actionKindInvalid";
                    return false;
                }

                actions.Add(new AutomationAction(actionKind, action!.MessageText ?? string.Empty));
            }

            try
            {
                definition = AutomationDefinition.Create(
                    new AutomationTrigger(triggerKind, keywords), conditions, actions);
                return true;
            }
            catch (AutomationsDomainException exception)
            {
                errorCode = exception.RuleCode;
                return false;
            }
        }
    }
}
