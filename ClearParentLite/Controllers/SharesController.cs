using ClearParentLite.Data;
using ClearParentLite.Models;
using ClearParentLite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ClearParentLite.Controllers;
public class SharesController(AppDbContext db,AccessService access,UserManager<ApplicationUser> users,IEmailService email,IConfiguration config) : Controller
{
    async Task<SharesPage> Page(ShareInput input) => new(await db.Shares.Where(s=>s.OwnerUserId==access.UserId).Include(s=>s.Entries).OrderByDescending(s=>s.CreatedUtc).ToListAsync(),await db.Entries.Where(e=>e.OwnerUserId==access.UserId).OrderByDescending(e=>e.EventDate).ToListAsync(),input);
    [Authorize(Policy="Parent"),HttpGet("/shares")] public async Task<IActionResult> Index() => View(await Page(new()));
    [Authorize(Policy="Parent"),HttpPost("/shares")] public async Task<IActionResult> Create(ShareInput input) {
        if(!Enum.IsDefined(input.Scope) || !Enum.IsDefined(input.AccessMethod)) return BadRequest();
        var ids=input.EntryIds.Distinct().ToArray();
        if(input.Scope==ShareScope.SelectedEntries && (ids.Length==0 || ids.Length>500 || await db.Entries.CountAsync(e=>e.OwnerUserId==access.UserId && ids.Contains(e.Id))!=ids.Length)) ModelState.AddModelError("","Choose entries from your own timeline.");
        if(input.AccessMethod==ShareMethod.Invitation && string.IsNullOrWhiteSpace(input.Email)) ModelState.AddModelError("","An invitation requires an email address.");
        if(!ModelState.IsValid) return View("Index",await Page(input));
        var token=input.AccessMethod==ShareMethod.Link ? AccessService.NewToken() : null;
        var grant=new ShareGrant { OwnerUserId=access.UserId!,CreatedByUserId=access.UserId!,Scope=input.Scope,AccessMethod=input.AccessMethod,InvitedEmailNormalized=input.AccessMethod==ShareMethod.Invitation ? users.NormalizeEmail(input.Email!.Trim()) : null,TokenHash=token is null ? null : AccessService.Hash(token),ExpiresUtc=DateTime.UtcNow.AddDays(input.Days) };
        if(input.Scope==ShareScope.SelectedEntries) grant.Entries=ids.Select(id=>new ShareGrantEntry { ShareGrantId=grant.Id,EntryId=id }).ToList();
        db.Shares.Add(grant); access.Audit(access.UserId!,"ShareCreated","Share",grant.Id.ToString()); await db.SaveChangesAsync();
        var origin=config["App:PublicOrigin"]!.TrimEnd('/');
        if(token!=null) { ViewBag.ShareUrl=$"{origin}/s/{token}"; return View("Created"); }
        try { await email.Send(input.Email!,"A ClearParent Lite timeline was shared with you",$"Sign in with this email address and confirm your email, then accept your read-only invitation: {origin}/invitations/{grant.Id}"); }
        catch { grant.RevokedUtc=DateTime.UtcNow; await db.SaveChangesAsync(); TempData["Notice"]="Invitation delivery failed. The grant was revoked; check email configuration and retry."; return Redirect("/shares"); }
        TempData["Notice"]="Invitation sent. The recipient must sign in and accept it."; return Redirect("/shares");
    }
    [Authorize(Policy="Parent"),HttpPost("/shares/{id:guid}/revoke")] public async Task<IActionResult> Revoke(Guid id) {
        var grant=await db.Shares.SingleOrDefaultAsync(s=>s.Id==id && s.OwnerUserId==access.UserId); if(grant==null) return NotFound();
        grant.RevokedUtc=DateTime.UtcNow; access.Audit(access.UserId!,"ShareRevoked","Share",id.ToString()); await db.SaveChangesAsync(); return Redirect("/shares");
    }
    [AllowAnonymous,HttpGet("/s/{token}"),Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("account")]
    public async Task<IActionResult> Link(string token) {
        if(token.Length!=64) return NotFound(); var hash=AccessService.Hash(token); var now=DateTime.UtcNow;
        var grant=await db.Shares.SingleOrDefaultAsync(s=>s.TokenHash==hash && s.AccessMethod==ShareMethod.Link && s.RevokedUtc==null && s.ExpiresUtc>now); if(grant==null) return NotFound();
        access.SetLinkCookie(grant); access.Audit(grant.OwnerUserId,"ShareOpened","Share",grant.Id.ToString()); await db.SaveChangesAsync(); return Redirect($"/shared/{grant.Id}");
    }
    [Authorize,HttpGet("/invitations/{id:guid}")] public async Task<IActionResult> Invitation(Guid id) {
        var user=await users.GetUserAsync(User); var now=DateTime.UtcNow;
        var grant=await db.Shares.SingleOrDefaultAsync(s=>s.Id==id && s.AccessMethod==ShareMethod.Invitation && s.RevokedUtc==null && s.ExpiresUtc>now);
        if(user is not { EmailConfirmed:true } || grant==null || grant.InvitedEmailNormalized!=user.NormalizedEmail) return NotFound();
        return View(grant);
    }
    [Authorize,HttpPost("/invitations/{id:guid}")] public async Task<IActionResult> Accept(Guid id) {
        var user=await users.GetUserAsync(User); var now=DateTime.UtcNow;
        var grant=await db.Shares.SingleOrDefaultAsync(s=>s.Id==id && s.AccessMethod==ShareMethod.Invitation && s.RevokedUtc==null && s.ExpiresUtc>now);
        if(user is not { EmailConfirmed:true } || grant==null || grant.InvitedEmailNormalized!=user.NormalizedEmail) return NotFound();
        grant.InvitedUserId=user.Id;
        access.Audit(grant.OwnerUserId,"InvitationAccepted","Share",id.ToString()); await db.SaveChangesAsync(); return Redirect($"/shared/{id}");
    }
    [AllowAnonymous,HttpGet("/shared/{id:guid}")] public async Task<IActionResult> Shared(Guid id,int page=1) {
        var grant=await access.ActiveGrants().SingleOrDefaultAsync(s=>s.Id==id); if(grant==null) return NotFound();
        page=Math.Clamp(page,1,100000);
        var entries=await db.Entries.AsNoTracking().Where(e=>e.OwnerUserId==grant.OwnerUserId && (grant.Scope==ShareScope.Timeline || db.ShareEntries.Any(se=>se.ShareGrantId==id && se.EntryId==e.Id))).OrderByDescending(e=>e.EventDate).ThenBy(e=>e.Id).Skip((page-1)*25).Take(26).ToListAsync();
        ViewBag.ShareId=id; return View(new TimelinePage(entries.Take(25).ToList(),page,entries.Count>25,null,null,null));
    }
}
