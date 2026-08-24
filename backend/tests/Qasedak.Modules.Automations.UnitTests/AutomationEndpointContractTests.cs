using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Qasedak.Modules.Automations.Infrastructure.Endpoints;
using Xunit;

namespace Qasedak.Modules.Automations.UnitTests;

/// <summary>
/// M08-005: the automation endpoints' wire→domain mapping and failure-code → HTTP status
/// contract pinned so the builder UI's error handling cannot silently drift.
/// </summary>
public class AutomationEndpointContractTests
{
    private static AutomationEndpoints.DefinitionRequest ValidDefinition() => new(
        TriggerKind: "CommentCreated",
        KeywordFilters: ["قیمت", "خرید"],
        Conditions:
        [
            new AutomationEndpoints.ConditionRequest("CommentText", "Contains", "قیمت"),
        ],
        Actions:
        [
            new AutomationEndpoints.ActionRequest("SendDirectMessage", "سلام 👋 اطلاعات کامل برات ارسال شد."),
        ]);

    [Fact]
    public void TryMapValidDefinitionSucceedsWithOrderedContent()
    {
        var ok = AutomationEndpoints.DefinitionMapper.TryMap(ValidDefinition(), out var definition, out var error);

        Assert.True(ok, error);
        Assert.Equal(TriggerKind.CommentCreated, definition!.Trigger.Kind);
        Assert.Equal(["قیمت", "خرید"], definition.Trigger.KeywordFilters);
        Assert.Single(definition.Conditions);
        Assert.Equal(ConditionOperator.Contains, definition.Conditions[0].Operator);
        Assert.Single(definition.Actions);
    }

    [Theory]
    [InlineData("NotATrigger")]
    [InlineData("")]
    [InlineData(null)]
    public void TryMapUnknownTriggerKindFailsClosed(string? triggerKind)
    {
        var request = new AutomationEndpoints.DefinitionRequest(triggerKind, [], [], ValidDefinition().Actions);

        var ok = AutomationEndpoints.DefinitionMapper.TryMap(request, out _, out var error);

        Assert.False(ok);
        Assert.Equal("automation.triggerKindInvalid", error);
    }

    [Fact]
    public void TryMapUnknownConditionOperatorFailsClosed()
    {
        var request = new AutomationEndpoints.DefinitionRequest(
            "CommentCreated", [],
            [new AutomationEndpoints.ConditionRequest("CommentText", "StartsWith", "x")],
            ValidDefinition().Actions);

        var ok = AutomationEndpoints.DefinitionMapper.TryMap(request, out _, out var error);

        Assert.False(ok);
        Assert.Equal("automation.conditionInvalid", error);
    }

    [Fact]
    public void TryMapDefinitionWithoutActionsSurfacesDomainRuleCode()
    {
        var request = new AutomationEndpoints.DefinitionRequest("CommentCreated", [], [], []);

        var ok = AutomationEndpoints.DefinitionMapper.TryMap(request, out _, out var error);

        Assert.False(ok);
        Assert.Equal("automation.actionRequired", error);
    }
}
