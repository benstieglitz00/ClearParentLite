using ClearParentLite.Data;
using ClearParentLite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ClearParentLite.Controllers;
public class AttachmentsController(AppDbContext db, AccessService access, IPrivateStorage storage, IConfiguration config) : Controller
{
    [Authorize(Policy="Parent"),HttpPost("/entries/{id:guid}/attachments"),RequestSizeLimit(22*1024*1024),RequestFormLimits(MultipartBodyLengthLimit=22*1024*1024)]
    public async Task<IActionResult> Upload(Guid id,IFormFile file,CancellationToken ct) {
        var entry=await db.Entries.SingleOrDefaultAsync(e=>e.Id==id && e.OwnerUserId==access.UserId,ct); if(entry is null) return NotFound();
        if(file is null) return BadRequest("Choose a file.");
        // Serializable range locking prevents simultaneous uploads from bypassing the owner's quota.
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
        var used=await db.Attachments.Where(a=>a.UploadedByUserId==access.UserId && !a.IsDeleted).SumAsync(a=>(long?)a.ByteLength,ct) ?? 0;
        if(used+file.Length>config.GetValue("Storage:QuotaBytes",1024L*1024*1024)) return BadRequest("Your storage quota has been reached.");
        Attachment? attachment=null;
        try {
            attachment=await storage.Store(file,id,access.UserId!,ct); db.Attachments.Add(attachment);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            if(Request.Headers.Accept.ToString().Contains("application/json")) return Json(new { id=attachment.Id,url=$"/attachments/{attachment.Id}",name=attachment.OriginalFileName });
            return Redirect($"/entries/{id}");
        } catch(InvalidDataException e) { if(attachment!=null) storage.Delete(attachment.StorageKey); return BadRequest(e.Message); }
        catch { if(attachment!=null) storage.Delete(attachment.StorageKey); throw; }
    }
    [HttpGet("/attachments/{id:guid}")] public async Task<IActionResult> Download(Guid id) {
        var attachment=await db.Attachments.AsNoTracking().SingleOrDefaultAsync(a=>a.Id==id && !a.IsDeleted); if(attachment is null) return NotFound();
        var entry=await db.Entries.AsNoTracking().SingleOrDefaultAsync(e=>e.Id==attachment.EntryId); if(entry is null || !await access.CanRead(entry)) return NotFound();
        access.Audit(entry.OwnerUserId,"Download","Attachment",id.ToString()); await db.SaveChangesAsync();
        Stream stream; try { stream=storage.Open(attachment.StorageKey); } catch(FileNotFoundException) { return NotFound(); }
        if(attachment.ContentType.StartsWith("image/")) { Response.Headers.ContentDisposition="inline"; return File(stream,attachment.ContentType); }
        return File(stream,attachment.ContentType,attachment.OriginalFileName);
    }
}
