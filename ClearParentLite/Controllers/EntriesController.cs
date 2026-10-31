using ClearParentLite.Data;
using ClearParentLite.Models;
using ClearParentLite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ClearParentLite.Controllers;
[Authorize(Policy="Parent")]
public class EntriesController(AppDbContext db, AccessService access, ContentService content) : Controller
{
    [HttpGet("/timeline")] public async Task<IActionResult> Index(string? q, DateOnly? from, DateOnly? to, int page = 1) {
        if(!ModelState.IsValid || (from.HasValue && to.HasValue && from>to)) return BadRequest("Enter a valid date range.");
        page = Math.Clamp(page,1,100000); if (q?.Length > 200) return BadRequest();
        var query = db.Entries.AsNoTracking().Where(e => e.OwnerUserId == access.UserId);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(e => e.Title.Contains(q) || e.SearchText.Contains(q) || e.Updates.Any(u => !u.IsDeleted && u.SearchText.Contains(q)));
        if (from.HasValue) query = query.Where(e => e.EventDate >= from); if (to.HasValue) query = query.Where(e => e.EventDate <= to);
        var entries = await query.OrderByDescending(e=>e.EventDate).ThenBy(e=>e.Id).Skip((page-1)*25).Take(26).Select(e=>new TimelineRow(e.Id,e.Title,e.EventDate,e.CreatedUtc,e.UpdatedUtc,e.Updates.Count(u=>!u.IsDeleted),e.Attachments.Count(a=>!a.IsDeleted))).ToListAsync();
        return View(new TimelinePage(entries.Take(25).ToList(), page, entries.Count > 25, q, from, to));
    }
    [HttpGet("/entries/new")] public IActionResult New() => View("Edit", new EntryInput());
    [HttpPost("/entries/new")] public async Task<IActionResult> New(EntryInput input) {
        input.Id = null; input.DescriptionHtml = content.Sanitize(input.DescriptionHtml);
        if (!content.HasContent(input.DescriptionHtml)) ModelState.AddModelError("DescriptionHtml", "Add a description.");
        if (!ModelState.IsValid) return View("Edit", input);
        if(input.RequestId==Guid.Empty) return BadRequest("Invalid draft identifier.");
        var existing=await db.Entries.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(e=>e.Id==input.RequestId);
        if(existing!=null) return CreationReplay(existing,input);
        var entry = new TimelineEntry { Id=input.RequestId,OwnerUserId = access.UserId!, Title = input.Title.Trim(), DescriptionHtml = input.DescriptionHtml, SearchText = content.Text(input.DescriptionHtml), EventDate = input.EventDate!.Value };
        entry.Revisions.Add(Snapshot(entry, 1, null)); db.Entries.Add(entry);
        try { await db.SaveChangesAsync(); }
        catch(DbUpdateException ex) when(ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number:2627 or 2601 }) {
            db.ChangeTracker.Clear();existing=await db.Entries.IgnoreQueryFilters().AsNoTracking().SingleAsync(e=>e.Id==input.RequestId);
            return CreationReplay(existing,input);
        }
        if(Request.Headers.Accept.ToString().Contains("application/json")) return Json(new { id=entry.Id,rowVersion=Convert.ToBase64String(entry.RowVersion) });
        return Redirect($"/entries/{entry.Id}");
    }
    IActionResult CreationReplay(TimelineEntry entry,EntryInput input) {
        if(entry.OwnerUserId!=access.UserId || entry.IsDeleted || entry.Title!=input.Title.Trim() || entry.EventDate!=input.EventDate || entry.DescriptionHtml!=input.DescriptionHtml)
            return Conflict("This draft identifier has already been used. Review your timeline before submitting a new entry.");
        if(Request.Headers.Accept.ToString().Contains("application/json"))return Json(new { id=entry.Id,rowVersion=Convert.ToBase64String(entry.RowVersion) });
        return Redirect($"/entries/{entry.Id}");
    }
    EntryRevision Snapshot(TimelineEntry e, int number, string? reason) => new() { EntryId = e.Id, RevisionNumber = number, Title = e.Title, DescriptionHtml = e.DescriptionHtml, EventDate = e.EventDate, EditedByUserId = access.UserId!, EditedUtc = e.UpdatedUtc, ChangeReason = reason };
    [AllowAnonymous, HttpGet("/entries/{id:guid}")] public async Task<IActionResult> Detail(Guid id) {
        var entry = await db.Entries.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==id);
        if (entry is null || !await access.CanRead(entry)) return NotFound();
        var detail=db.Entries.AsNoTracking().Include(e=>e.Attachments).Include(e=>e.Updates).AsSplitQuery();
        if(entry.OwnerUserId==access.UserId)detail=detail.Include(e=>e.Revisions);
        entry=await detail.SingleAsync(e=>e.Id==id);
        var shareId=entry.OwnerUserId==access.UserId?null:await access.ActiveGrants().Where(s=>s.OwnerUserId==entry.OwnerUserId && (s.Scope==ShareScope.Timeline || s.Entries.Any(se=>se.EntryId==id))).Select(s=>(Guid?)s.Id).FirstOrDefaultAsync();
        return View(new EntryDetail(entry, entry.OwnerUserId == access.UserId, shareId));
    }
    [HttpGet("/entries/{id:guid}/edit")] public async Task<IActionResult> Edit(Guid id) {
        var entry = await db.Entries.SingleOrDefaultAsync(e=>e.Id==id && e.OwnerUserId==access.UserId); if (entry is null) return NotFound();
        ViewBag.EntryId = id;
        return View(new EntryInput { Id = id, Title = entry.Title, DescriptionHtml = entry.DescriptionHtml, EventDate = entry.EventDate, RowVersion = Convert.ToBase64String(entry.RowVersion) });
    }
    [HttpPost("/entries/{id:guid}/edit")] public async Task<IActionResult> Edit(Guid id, EntryInput input) {
        var entry = await db.Entries.Include(e=>e.Attachments).SingleOrDefaultAsync(e=>e.Id==id && e.OwnerUserId==access.UserId); if (entry is null) return NotFound();
        input.Id = id; ViewBag.EntryId = id;
        input.DescriptionHtml = content.Sanitize(input.DescriptionHtml, entry.Attachments.Where(a=>!a.IsDeleted).Select(a=>a.Id));
        if (!content.HasContent(input.DescriptionHtml)) ModelState.AddModelError("DescriptionHtml", "Add a description.");
        byte[] version; try { version = Convert.FromBase64String(input.RowVersion ?? ""); } catch (FormatException) { return BadRequest(); }
        if(version.Length!=8) return BadRequest("A valid edit version is required.");
        if (!ModelState.IsValid) return View(input);
        if(version.SequenceEqual(entry.RowVersion) && entry.Title==input.Title.Trim() && entry.DescriptionHtml==input.DescriptionHtml && entry.EventDate==input.EventDate && string.IsNullOrWhiteSpace(input.ChangeReason)) {
            if(Request.Headers.Accept.ToString().Contains("application/json"))return Json(new { id });
            return Redirect($"/entries/{id}");
        }
        db.Entry(entry).Property(e=>e.RowVersion).OriginalValue = version;
        entry.Title = input.Title.Trim(); entry.DescriptionHtml = input.DescriptionHtml; entry.EventDate = input.EventDate!.Value; entry.SearchText = content.Text(input.DescriptionHtml); entry.UpdatedUtc = DateTime.UtcNow;
        try {
            await using var tx=await db.Database.BeginTransactionAsync();
            // Acquire the entry's write lock and verify its version before allocating a revision number.
            await db.SaveChangesAsync();
            var revision=await db.Revisions.Where(r=>r.EntryId==id).MaxAsync(r=>r.RevisionNumber)+1;
            db.Revisions.Add(Snapshot(entry,revision,input.ChangeReason));
            await db.SaveChangesAsync(); await tx.CommitAsync();
        } catch (DbUpdateConcurrencyException) { ModelState.AddModelError("", "This entry changed in another window. Your draft is preserved below. Open the latest entry in a new tab and reconcile before saving."); Response.StatusCode = 409; return View(input); }
        if(Request.Headers.Accept.ToString().Contains("application/json"))return Json(new { id });
        return Redirect($"/entries/{id}");
    }
    [HttpPost("/entries/{id:guid}/updates")] public async Task<IActionResult> Update(Guid id, string bodyHtml,Guid? requestId) {
        var entry = await db.Entries.Include(e=>e.Attachments).SingleOrDefaultAsync(e=>e.Id==id && e.OwnerUserId==access.UserId); if (entry is null) return NotFound();
        if (bodyHtml?.Length > 100000) return BadRequest(); var clean = content.Sanitize(bodyHtml ?? "",entry.Attachments.Where(a=>!a.IsDeleted).Select(a=>a.Id));
        if (!content.HasContent(clean)) return BadRequest("Write an update before saving.");
        if(!ModelState.IsValid || requestId==Guid.Empty)return BadRequest("Invalid update.");
        await using var tx=await db.Database.BeginTransactionAsync();
        // An append does not overwrite description/title, so it need not fail on a concurrent append.
        var changed=await db.Entries.Where(e=>e.Id==id && e.OwnerUserId==access.UserId).ExecuteUpdateAsync(s=>s.SetProperty(e=>e.UpdatedUtc,DateTime.UtcNow));
        if(changed==0) return NotFound();
        var updateId=requestId ?? Guid.NewGuid();
        var existing=await db.Updates.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==updateId);
        if(existing!=null) {
            if(existing.EntryId!=id || existing.AuthorUserId!=access.UserId || existing.BodyHtml!=clean || existing.IsDeleted)return Conflict("This update identifier has already been used.");
            if(Request.Headers.Accept.ToString().Contains("application/json"))return Json(new { id });
            return Redirect($"/entries/{id}");
        }
        db.Updates.Add(new EntryUpdate { Id=updateId,EntryId=id,AuthorUserId=access.UserId!,BodyHtml=clean,SearchText=content.Text(clean) });
        try { await db.SaveChangesAsync();await tx.CommitAsync(); }
        catch(DbUpdateException ex) when(ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number:2627 or 2601 }) { return Conflict("This update identifier has already been used."); }
        if(Request.Headers.Accept.ToString().Contains("application/json"))return Json(new { id });
        return Redirect($"/entries/{id}");
    }
    [HttpPost("/entries/{id:guid}/delete")] public async Task<IActionResult> Delete(Guid id) {
        var entry = await db.Entries.SingleOrDefaultAsync(e=>e.Id==id && e.OwnerUserId==access.UserId); if (entry is null) return NotFound();
        entry.IsDeleted = true; access.Audit(entry.OwnerUserId,"EntryDeleted","Entry",id.ToString());
        try { await db.SaveChangesAsync(); } catch(DbUpdateConcurrencyException) {return Conflict("This entry changed. Reload it before removing it.");}
        return Redirect("/timeline");
    }
}
