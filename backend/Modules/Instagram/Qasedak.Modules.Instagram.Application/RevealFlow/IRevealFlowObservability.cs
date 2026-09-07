namespace Qasedak.Modules.Instagram.Application.RevealFlow;

/// <summary>
/// Observability seam for reveal-flow outcomes. Implementations MUST keep dimensions
/// low-cardinality (operation / outcome only) — never FlowId, CommentId, AccountId,
/// participant, token hash, message text or button content.
/// </summary>
public interface IRevealFlowObservability
{
    /// <summary>One provider mutation is about to be issued (opening / gate prompt / reveal).</summary>
    void Attempted(string operation);

    /// <summary>Provider confirmed delivery (opening / gate prompt / reveal).</summary>
    void Succeeded(string operation);

    /// <summary>Attempt began but the outcome is unknown — never re-attempted.</summary>
    void Uncertain(string operation);

    /// <summary>Terminal failure with a stable log-safe code.</summary>
    void Failed(string operation, string code);

    /// <summary>Safe no-op / truthful suppression (ignored event, ambiguity, correlation rejection).</summary>
    void Suppressed(string operation, string code);
}
