using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ClearParentLite.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace ClearParentLite.Services;
public class AccessService(AppDbContext db, IHttpContextAccessor accessor, IDataProtectionProvider protection)
{
    public string? UserId => accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public void SetLinkCookie(ShareGrant grant) {
        var value = protection.CreateProtector("share-access-v1").Protect($"{grant.Id}:{grant.TokenHash}");
        accessor.HttpContext!.Response.Cookies.Append("cp.share", value, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromDays(1), IsEssential = true });
    }
    public (Guid? Id, string? Hash) CookieGrant() {
        try {
            var value = accessor.HttpContext?.Request.Cookies["cp.share"];
            if (value is null) return (null, null);
            var parts = protection.CreateProtector("share-access-v1").Unprotect(value).Split(':');
            return parts.Length == 2 && Guid.TryParse(parts[0], out var id) ? (id, parts[1]) : (null, null);
        } catch (CryptographicException) { return (null, null); }
    }
    public IQueryable<ShareGrant> ActiveGrants() {
        var now = DateTime.UtcNow; var (id, hash) = CookieGrant(); var userId = UserId;
        return db.Shares.Where(s => s.RevokedUtc == null && (s.ExpiresUtc == null || s.ExpiresUtc > now) &&
            ((s.AccessMethod == ShareMethod.Link && s.Id == id && s.TokenHash == hash) ||
             (s.AccessMethod == ShareMethod.Invitation && s.InvitedUserId != null && s.InvitedUserId == userId)));
    }
    public async Task<bool> CanRead(TimelineEntry entry) => entry.OwnerUserId == UserId || await ActiveGrants().AnyAsync(s => s.OwnerUserId == entry.OwnerUserId && (s.Scope == ShareScope.Timeline || s.Entries.Any(e => e.EntryId == entry.Id)));
    public async Task<bool> InGrant(Guid shareId, TimelineEntry entry) => await ActiveGrants().AnyAsync(s => s.Id == shareId && s.OwnerUserId == entry.OwnerUserId && (s.Scope == ShareScope.Timeline || s.Entries.Any(e => e.EntryId == entry.Id)));
    public void Audit(string owner, string action, string type, string id) => db.AuditEvents.Add(new AuditEvent { OwnerUserId = owner, ActorUserId = UserId, Action = action, TargetType = type, TargetId = id });
}
