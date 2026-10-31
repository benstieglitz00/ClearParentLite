using ClearParentLite.Data;
using ClearParentLite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ClearParentLite.Controllers;
[Authorize]
public class SettingsController(AppDbContext db,AccessService access,UserManager<ApplicationUser> users) : Controller
{
    [HttpGet("/settings")] public async Task<IActionResult> Index() {
        ViewBag.Received=await access.ActiveGrants().Where(s=>s.AccessMethod==ShareMethod.Invitation).ToListAsync();
        return View(await users.GetUserAsync(User));
    }
    [HttpPost("/settings")] public async Task<IActionResult> Update(string displayName) {
        var user=await users.GetUserAsync(User); if(user==null) return NotFound();
        if(string.IsNullOrWhiteSpace(displayName)||displayName.Length>100) { TempData["Notice"]="Enter a name of 1-100 characters.";return Redirect("/settings"); }
        user.DisplayName=displayName.Trim(); await users.UpdateAsync(user); TempData["Notice"]="Profile updated.";return Redirect("/settings");
    }
    [Authorize(Roles="Administrator"),HttpGet("/admin/users")] public async Task<IActionResult> Users() => View(await users.Users.OrderBy(u=>u.Email).Take(200).ToListAsync());
    [Authorize(Roles="Administrator"),HttpPost("/admin/users/{id}/toggle")] public async Task<IActionResult> Toggle(string id) {
        if(id==access.UserId) return BadRequest("You cannot disable your own account.");
        var user=await users.FindByIdAsync(id); if(user==null) return NotFound();
        user.DisabledUtc=user.DisabledUtc==null?DateTime.UtcNow:null; await users.UpdateAsync(user); await users.UpdateSecurityStampAsync(user);
        access.Audit(user.Id,"AccountStatusChanged","Account",user.Id);await db.SaveChangesAsync();return Redirect("/admin/users");
    }
}
