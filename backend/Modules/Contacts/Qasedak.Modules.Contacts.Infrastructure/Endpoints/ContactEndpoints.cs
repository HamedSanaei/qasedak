using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Qasedak.Modules.Contacts.Application;
using Qasedak.Modules.Contacts.Domain;

namespace Qasedak.Modules.Contacts.Infrastructure.Endpoints;

/// <summary>
/// Workspace contact CRM HTTP surface: paginated/searchable list, per-contact detail with
/// notes, tag management and note appending. Every route is workspace-scoped; unknown or
/// foreign contacts are 404.
/// </summary>
public static class ContactEndpoints
{
    public static IEndpointRouteBuilder MapContactEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var contacts = endpoints.MapGroup("/api/v1/workspaces/{workspaceId:guid}/contacts")
            .WithTags("Contacts")
            .RequireAuthorization("workspace-member");

        contacts.MapGet(string.Empty, async (
            Guid workspaceId,
            string? search,
            string? status,
            string? tag,
            int? page,
            int? pageSize,
            IContactQueries queries,
            CancellationToken cancellationToken) =>
        {
            var result = await queries.ListAsync(
                workspaceId,
                ContactFilter.From(search, status, tag, page ?? 1, pageSize ?? ContactFilter.DefaultPageSize),
                cancellationToken);
            return Results.Ok(new
            {
                page = result.Page,
                pageSize = result.PageSize,
                totalCount = result.TotalCount,
                items = result.Items.Select(row => new
                {
                    id = row.Id,
                    displayName = row.DisplayName,
                    status = row.Status.ToLowerInvariant(),
                    lastSeenAtUtc = row.LastSeenAtUtc,
                    interactionCount = row.InteractionCount,
                    tags = row.Tags,
                }),
            });
        });

        contacts.MapGet("/{contactId:guid}", async (
            Guid workspaceId,
            Guid contactId,
            IContactQueries queries,
            CancellationToken cancellationToken) =>
        {
            var detail = await queries.GetDetailAsync(workspaceId, contactId, cancellationToken);
            return detail is null
                ? Results.NotFound(new { code = ContactFailures.NotFound })
                : Results.Ok(new
                {
                    id = detail.Id,
                    displayName = detail.DisplayName,
                    status = detail.Status.ToLowerInvariant(),
                    createdAtUtc = detail.CreatedAtUtc,
                    lastSeenAtUtc = detail.LastSeenAtUtc,
                    interactionCount = detail.InteractionCount,
                    mergedIntoId = detail.MergedIntoId,
                    identities = detail.Identities.Select(i => new { channel = i.Channel, providerIdentity = i.ProviderIdentity }),
                    tags = detail.Tags,
                    notes = detail.Notes.Select(n => new { id = n.Item1, body = n.Body, createdAtUtc = n.CreatedAtUtc }),
                });
        });

        contacts.MapPost("/{contactId:guid}/tags", async (
            Guid workspaceId,
            Guid contactId,
            TagRequest request,
            IContactRepository repository,
            CancellationToken cancellationToken) =>
        {
            var contact = await repository.FindByIdAsync(contactId, cancellationToken);
            if (contact is null || contact.WorkspaceId != workspaceId)
            {
                return Results.NotFound(new { code = ContactFailures.NotFound });
            }

            if (string.IsNullOrWhiteSpace(request.Tag))
            {
                return Results.Json(new { code = "contact.tagRequired" }, statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                contact.AddTag(request.Tag);
                await repository.SaveChangesAsync(contact, cancellationToken);
                return Results.NoContent();
            }
            catch (ContactsDomainException exception)
            {
                return MapDomainFailure(exception);
            }
        });

        contacts.MapDelete("/{contactId:guid}/tags/{tag}", async (
            Guid workspaceId,
            Guid contactId,
            string tag,
            IContactRepository repository,
            CancellationToken cancellationToken) =>
        {
            var contact = await repository.FindByIdAsync(contactId, cancellationToken);
            if (contact is null || contact.WorkspaceId != workspaceId)
            {
                return Results.NotFound(new { code = ContactFailures.NotFound });
            }

            contact.RemoveTag(tag);
            await repository.SaveChangesAsync(contact, cancellationToken);
            return Results.NoContent();
        });

        contacts.MapPost("/{contactId:guid}/notes", async (
            Guid workspaceId,
            Guid contactId,
            NoteRequest request,
            IContactRepository repository,
            CancellationToken cancellationToken) =>
        {
            var contact = await repository.FindByIdAsync(contactId, cancellationToken);
            if (contact is null || contact.WorkspaceId != workspaceId)
            {
                return Results.NotFound(new { code = ContactFailures.NotFound });
            }

            try
            {
                var note = contact.AddNote(request.Body ?? string.Empty, DateTimeOffset.UtcNow);
                await repository.SaveChangesAsync(contact, cancellationToken);
                return Results.Created(
                    $"/api/v1/workspaces/{workspaceId}/contacts/{contactId}",
                    new { noteId = note.Id });
            }
            catch (ContactsDomainException exception)
            {
                return MapDomainFailure(exception);
            }
        });

        return endpoints;
    }

    private static IResult MapDomainFailure(ContactsDomainException exception) => exception.RuleCode switch
    {
        "contact.tagTooLong" or "contact.tooManyTags" or "contact.noteTooLong" or
            "contact.notActive" or "contact.notMergeable" =>
            Results.Json(new { code = exception.RuleCode }, statusCode: StatusCodes.Status409Conflict),
        _ => Results.Json(new { code = exception.RuleCode }, statusCode: StatusCodes.Status400BadRequest),
    };

    public sealed record TagRequest(string? Tag);

    public sealed record NoteRequest(string? Body);
}
