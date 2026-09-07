using System.Diagnostics.Metrics;

namespace Qasedak.Modules.Instagram.Application.Reconciliation;

/// <summary>
/// Low-cardinality comment-reconciliation observability (M13-013 §83). Labels never
/// contain WorkspaceId, ConnectedAccountId, media/comment ids, text or tokens.
/// </summary>
public sealed class CommentReconciliationMetrics
{
    private readonly Counter<long> _sweeps = Meter.CreateCounter<long>("qasedak.instagram.comment_reconciliation.sweeps", "sweeps");

    private readonly Counter<long> _comments = Meter.CreateCounter<long>("qasedak.instagram.comment_reconciliation.comments", "comments");

    private static readonly Meter Meter = new("Qasedak.Instagram.CommentReconciliation", "1.0.0");

    public void SweepCompleted(string outcome, int mediaScanned, int pages) =>
        _sweeps.Add(1, new KeyValuePair<string, object?>("outcome", outcome),
            new KeyValuePair<string, object?>("media", mediaScanned),
            new KeyValuePair<string, object?>("pages", pages));

    public void Comments(int dispatched, int selfSkipped, int unknownAuthorSkipped)
    {
        if (dispatched > 0)
        {
            _comments.Add(dispatched, new KeyValuePair<string, object?>("kind", "dispatched"));
        }

        if (selfSkipped > 0)
        {
            _comments.Add(selfSkipped, new KeyValuePair<string, object?>("kind", "selfSkipped"));
        }

        if (unknownAuthorSkipped > 0)
        {
            _comments.Add(unknownAuthorSkipped, new KeyValuePair<string, object?>("kind", "unknownAuthorSkipped"));
        }
    }
}
