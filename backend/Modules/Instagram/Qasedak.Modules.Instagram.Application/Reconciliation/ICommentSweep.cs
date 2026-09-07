namespace Qasedak.Modules.Instagram.Application.Reconciliation;

/// <summary>
/// One bounded comment-reconciliation sweep for one exact connected account. The
/// scheduled handler depends on this seam (implemented by
/// <see cref="CommentReconciliationUseCase"/>) so chain mechanics are testable
/// without provider fakes.
/// </summary>
public interface ICommentSweep
{
    Task<CommentSweepOutcome> ExecuteAsync(Guid accountId, CancellationToken cancellationToken = default);
}
