namespace Qasedak.Modules.Contacts.Domain;

/// <summary>An immutable note appended to a contact's history. Notes are never edited or deleted.</summary>
public sealed record ContactNote(Guid Id, Guid ContactId, Guid WorkspaceId, string Body, DateTimeOffset CreatedAtUtc);
