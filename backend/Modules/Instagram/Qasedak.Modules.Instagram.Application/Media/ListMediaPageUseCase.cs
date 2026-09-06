using Qasedak.Modules.Instagram.Application.Accounts;
using Qasedak.Modules.Instagram.Domain.Accounts;

namespace Qasedak.Modules.Instagram.Application.Media;

/// <summary>Outcome of an exact-account media page request.</summary>
public readonly record struct MediaPageUseCaseResult(bool Success, MediaCatalogPage? Page, string? FailureCode)
{
    public static MediaPageUseCaseResult Ok(MediaCatalogPage page) => new(true, page, null);

    public static MediaPageUseCaseResult Refused(string failureCode) => new(false, null, failureCode);
}

/// <summary>
/// M13-006 media page use case. Executes the exact-account contract before any
/// provider interaction:
/// find account by id → verify <c>WorkspaceId</c> ownership → reject disconnected →
/// read only THIS account's protected token → decode/validate the client cursor
/// (account-bound, bounded, never a URL) → call the account's media edge.
/// A foreign-workspace or unknown account fails with zero token reads and zero
/// provider calls. Subscription health is deliberately NOT consulted: webhook
/// subscription state and media-read capability are independent.
/// </summary>
public sealed class ListMediaPageUseCase(
    IConnectedAccountRepository accounts,
    IProtectedTokenStore tokens,
    IMediaCursorCodec cursors,
    IMediaCatalogClient media)
{
    public async Task<MediaPageUseCaseResult> ExecuteAsync(
        Guid workspaceId,
        Guid accountId,
        int limit,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        var account = await accounts.FindByIdAsync(accountId, cancellationToken);
        if (account is null || account.WorkspaceId != workspaceId)
        {
            return MediaPageUseCaseResult.Refused(AccountFailures.NotFound);
        }

        if (account.IsDisconnected)
        {
            return MediaPageUseCaseResult.Refused(AccountFailures.AlreadyDisconnected);
        }

        var decode = cursors.Decode(cursor, account.Id);
        if (!decode.Valid)
        {
            return MediaPageUseCaseResult.Refused(MediaCatalogFailures.InvalidCursor);
        }

        var accessToken = await tokens.GetAsync(account.Id, cancellationToken);
        if (string.IsNullOrEmpty(accessToken))
        {
            // Connected account without token material: actionable inconsistency;
            // never fall back to another account's token.
            return MediaPageUseCaseResult.Refused(AccountFailures.TokenMissing);
        }

        var result = await media.GetPageAsync(
            accessToken,
            account.ProviderUserId,
            account.Id,
            limit,
            decode.ProviderAfterCursor,
            cancellationToken);

        return result switch
        {
            MediaCatalogResult.Ok ok => MediaPageUseCaseResult.Ok(ok.Page),
            MediaCatalogResult.Failed failed => MediaPageUseCaseResult.Refused(failed.FailureCode),
            _ => MediaPageUseCaseResult.Refused(MediaCatalogFailures.Unavailable),
        };
    }
}
