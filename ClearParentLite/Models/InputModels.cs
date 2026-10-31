using System.ComponentModel.DataAnnotations;
using ClearParentLite.Data;

namespace ClearParentLite.Models;
public class RegisterInput
{
    [Required, MaxLength(100), Display(Name="Your name")] public string DisplayName { get; set; } = "";
    [Required, EmailAddress, MaxLength(256)] public string Email { get; set; } = "";
    [Required, MinLength(12), MaxLength(128), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Compare(nameof(Password)), DataType(DataType.Password), Display(Name="Confirm password")] public string ConfirmPassword { get; set; } = "";
}
public class LoginInput
{
    [Required, EmailAddress] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
public class EmailInput { [Required, EmailAddress] public string Email { get; set; } = ""; }
public class ResetInput : EmailInput
{
    [Required] public string Code { get; set; } = "";
    [Required, MinLength(12), MaxLength(128), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Compare(nameof(Password)), DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
}
public class EntryInput
{
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public Guid? Id { get; set; }
    [Required, MaxLength(200)] public string Title { get; set; } = "";
    [Required, MaxLength(100000), Display(Name="Description")] public string DescriptionHtml { get; set; } = "";
    [Required, DataType(DataType.Date), Display(Name="Event date")] public DateOnly? EventDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string? RowVersion { get; set; }
    [MaxLength(500), Display(Name="Reason for edit (optional)")] public string? ChangeReason { get; set; }
}
public class ShareInput
{
    public ShareScope Scope { get; set; } = ShareScope.SelectedEntries;
    public ShareMethod AccessMethod { get; set; }
    [EmailAddress, MaxLength(256)] public string? Email { get; set; }
    public Guid[] EntryIds { get; set; } = [];
    [Range(1,90)] public int Days { get; set; } = 7;
}
public record TimelineRow(Guid Id,string Title,DateOnly EventDate,DateTime CreatedUtc,DateTime UpdatedUtc,int UpdateCount,int AttachmentCount);
public record TimelinePage(List<TimelineRow> Entries, int Page, bool HasMore, string? Q, DateOnly? From, DateOnly? To);
public record EntryDetail(TimelineEntry Entry, bool IsOwner, Guid? ShareId);
public record SharesPage(List<ShareGrant> Grants, List<TimelineEntry> Entries, ShareInput Input);
