using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace ClearParentLite.Data;

public class ApplicationUser : IdentityUser
{
    [MaxLength(100)] public string DisplayName { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DisabledUtc { get; set; }
}
public class TimelineEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = "";
    [MaxLength(200)] public string Title { get; set; } = "";
    [MaxLength(100000)] public string DescriptionHtml { get; set; } = "";
    public string SearchText { get; set; } = "";
    public DateOnly EventDate { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
    public List<EntryRevision> Revisions { get; set; } = [];
    public List<EntryUpdate> Updates { get; set; } = [];
    public List<Attachment> Attachments { get; set; } = [];
}
public class EntryRevision
{
    public long Id { get; set; }
    public Guid EntryId { get; set; }
    public int RevisionNumber { get; set; }
    [MaxLength(200)] public string Title { get; set; } = "";
    public string DescriptionHtml { get; set; } = "";
    public DateOnly EventDate { get; set; }
    public string EditedByUserId { get; set; } = "";
    public DateTime EditedUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(500)] public string? ChangeReason { get; set; }
}
public class EntryUpdate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntryId { get; set; }
    public string AuthorUserId { get; set; } = "";
    public string BodyHtml { get; set; } = "";
    public string SearchText { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
public class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EntryId { get; set; }
    public Guid? EntryUpdateId { get; set; }
    public string UploadedByUserId { get; set; } = "";
    [MaxLength(200)] public string OriginalFileName { get; set; } = "";
    [MaxLength(100)] public string StorageKey { get; set; } = "";
    [MaxLength(100)] public string ContentType { get; set; } = "";
    public long ByteLength { get; set; }
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
public enum ShareScope { Timeline, SelectedEntries }
public enum ShareMethod { Invitation, Link }
public class ShareGrant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = "";
    public ShareScope Scope { get; set; }
    public ShareMethod AccessMethod { get; set; }
    [MaxLength(256)] public string? InvitedEmailNormalized { get; set; }
    public string? InvitedUserId { get; set; }
    [MaxLength(64)] public string? TokenHash { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string CreatedByUserId { get; set; } = "";
    public List<ShareGrantEntry> Entries { get; set; } = [];
}
public class ShareGrantEntry
{
    public Guid ShareGrantId { get; set; }
    public Guid EntryId { get; set; }
}
public class AuditEvent
{
    public long Id { get; set; }
    public string? ActorUserId { get; set; }
    public string OwnerUserId { get; set; } = "";
    [MaxLength(60)] public string Action { get; set; } = "";
    [MaxLength(60)] public string TargetType { get; set; } = "";
    [MaxLength(100)] public string TargetId { get; set; } = "";
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
    public string MetadataJson { get; set; } = "{}";
}
