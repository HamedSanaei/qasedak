using Qasedak.Modules.Automations.Domain;
using Qasedak.Modules.Automations.Domain.Definitions;
using Xunit;

namespace Qasedak.Modules.Automations.UnitTests;

/// <summary>Aggregate invariants: identity, lifecycle, version immutability, edit rules.</summary>
public sealed class AutomationAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 20, 10, 0, 0, TimeSpan.Zero);

    private const string Text = "thanks for reaching out";

    private static AutomationDefinition Definition(string? message = null) =>
        AutomationDefinition.Create(AutomationTrigger.CommentCreated(), [new AutomationAction(ActionKind.SendDirectMessage, message ?? "hello!")]);

    private static Automation NewAutomation(string? name = null) =>
        Automation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), name ?? "Welcome flow", Definition(), Now);

    [Fact]
    public void CreateBuildsDraftWithFirstVersion()
    {
        var automation = NewAutomation();

        Assert.Equal(AutomationStatus.Draft, automation.Status);
        Assert.Equal(1, automation.CurrentVersionNumber);
        Assert.Single(automation.Versions);
        Assert.Null(automation.ActivatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRequiresName(string? name)
    {
        var exception = Assert.Throws<AutomationsDomainException>(
            () => Automation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), name!, Definition(), Now));
        Assert.Equal("automation.nameRequired", exception.RuleCode);
    }

    [Fact]
    public void CreateRejectsNamesBeyondLimit()
    {
        var exception = Assert.Throws<AutomationsDomainException>(
            () => Automation.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), new string('x', Automation.MaxNameLength + 1), Definition(), Now));
        Assert.Equal("automation.nameTooLong", exception.RuleCode);
    }

    [Fact]
    public void DefinitionRequiresAtLeastOneAction()
    {
        Assert.Throws<AutomationsDomainException>(() => AutomationDefinition.Create(AutomationTrigger.CommentCreated(), []));
    }

    [Fact]
    public void DefinitionRejectsEmptyAndOversizedActionText()
    {
        Assert.Throws<AutomationsDomainException>(
            () => AutomationDefinition.Create(AutomationTrigger.CommentCreated(), [new AutomationAction(ActionKind.SendDirectMessage, "  ")]));

        Assert.Throws<AutomationsDomainException>(
            () => AutomationDefinition.Create(
                AutomationTrigger.CommentCreated(),
                [new AutomationAction(ActionKind.SendDirectMessage, new string('x', AutomationAction.MaxMessageLength + 1))]));
    }

    [Fact]
    public void DraftEditsReplaceTheUnfrozenVersionInPlace()
    {
        var automation = NewAutomation();
        var revised = Definition("revised");

        automation.ReviseDraftDefinition(revised, Now.AddMinutes(5));

        Assert.Equal(1, automation.CurrentVersionNumber);
        Assert.Single(automation.Versions);
        Assert.Equal("revised", automation.CurrentDefinition.Actions[0].MessageText);
    }

    [Fact]
    public void ActivationFreezesTheCurrentVersionPermanently()
    {
        var automation = NewAutomation();
        automation.Activate(Now.AddMinutes(1));
        var frozen = automation.FrozenActiveVersion();

        Assert.Equal(AutomationStatus.Active, automation.Status);
        Assert.NotNull(automation.ActivatedAtUtc);

        // The frozen snapshot is the same instance content executions must observe.
        Assert.Equal(frozen.Definition, automation.FrozenActiveVersion().Definition);
    }

    [Fact]
    public void EditingAnActiveAutomationIsRefusedToProtectReproducibility()
    {
        var automation = NewAutomation();
        automation.Activate(Now.AddMinutes(1));

        var exception = Assert.Throws<AutomationsDomainException>(
            () => automation.ReviseDraftDefinition(Definition("nope"), Now.AddMinutes(2)));

        Assert.Equal("automation.versionFrozen", exception.RuleCode);
    }

    [Fact]
    public void UnpublishAllowsEditingWhichContinuesAsNewVersionNumber()
    {
        var automation = NewAutomation();
        automation.Activate(Now.AddMinutes(1));
        automation.Unpublish(Now.AddMinutes(2));

        Assert.Equal(AutomationStatus.Draft, automation.Status);
        Assert.Throws<AutomationsDomainException>(() => automation.FrozenActiveVersion());

        automation.ReviseDraftDefinition(Definition("v2 content"), Now.AddMinutes(3));

        // Frozen v1 stays intact; editing continued as version 2.
        Assert.Equal(2, automation.CurrentVersionNumber);
        Assert.Equal("hello!", automation.Versions[0].Definition.Actions[0].MessageText);
        Assert.Equal("v2 content", automation.Versions[1].Definition.Actions[0].MessageText);
    }

    [Fact]
    public void ReactivationFreezesTheNewVersion()
    {
        var automation = NewAutomation();
        automation.Activate(Now.AddMinutes(1));
        automation.Unpublish(Now.AddMinutes(2));
        automation.ReviseDraftDefinition(Definition("v2"), Now.AddMinutes(3));
        automation.Activate(Now.AddMinutes(4));

        Assert.Equal(2, automation.FrozenActiveVersion().Number);
        Assert.Equal("v2", automation.FrozenActiveVersion().Definition.Actions[0].MessageText);
    }

    [Fact]
    public void DoubleActivationAndActivationOfDisabledAreRefused()
    {
        var automation = NewAutomation();
        automation.Activate(Now);
        Assert.Throws<AutomationsDomainException>(() => automation.Activate(Now));

        automation.Disable(Now.AddMinutes(1));
        var reactivation = Assert.Throws<AutomationsDomainException>(() => automation.Activate(Now.AddMinutes(2)));
        Assert.Equal("automation.disabled", reactivation.RuleCode);
    }

    [Fact]
    public void DisableIsTerminalButHistoryRemainsReadable()
    {
        var automation = NewAutomation();
        automation.Activate(Now);
        automation.Unpublish(Now.AddMinutes(1));
        automation.ReviseDraftDefinition(Definition("v2"), Now.AddMinutes(2));
        automation.Activate(Now.AddMinutes(3));
        automation.Disable(Now.AddMinutes(4));

        Assert.Equal(AutomationStatus.Disabled, automation.Status);
        Assert.NotNull(automation.DisabledAtUtc);
        Assert.Equal(2, automation.Versions.Count);
        Assert.Throws<AutomationsDomainException>(() => automation.FrozenActiveVersion());
        Assert.Throws<AutomationsDomainException>(
            () => automation.ReviseDraftDefinition(Definition("v3"), Now.AddMinutes(5)));
        Assert.Throws<AutomationsDomainException>(() => automation.Disable(Now.AddMinutes(6)));
    }

    [Fact]
    public void ActionsPreserveDeclarationOrderForDeterministicExecution()
    {
        var definition = AutomationDefinition.Create(
            AutomationTrigger.CommentCreated(),
            [
                new AutomationAction(ActionKind.SendDirectMessage, "first"),
                new AutomationAction(ActionKind.SendDirectMessage, "second"),
                new AutomationAction(ActionKind.SendDirectMessage, "third"),
            ]);

        Assert.Equal(["first", "second", "third"], definition.Actions.Select(a => a.MessageText).ToArray());
    }
}
