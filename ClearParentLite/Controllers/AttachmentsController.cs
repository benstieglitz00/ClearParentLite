using ClearParentLite.Data;
using ClearParentLite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace ClearParentLite.Controllers;
public class AttachmentsController(AppDbContext db, AccessService access, IPrivateStorage storage, IConfiguration config) : Controller
{
    [Authorize(Policy="Parent"),HttpPost("/entries/{id:guid}/attachments"),RequestSizeLimit(22*1024*1024),RequestFormLimits(MultipartBodyLengthLimit=22*1024*1024)]
    public async Task<IActionResult> Upload(Guid id,IFormFile file,Guid? requestId,CancellationToken ct) {
        var entry=await db.Entries.SingleOrDefaultAsync(e=>e.Id==id && e.OwnerUserId==access.UserId,ct); if(entry is null) return NotFound();
        if(file is null) return BadRequest("Choose a file.");
        if(!ModelState.IsValid || requestId==Guid.Empty)return BadRequest("Invalid upload identifier.");
        // Serialize uploads per owner without taking range locks across other parents' files.
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var resource=$"ClearParentLite.Upload:{access.UserId}";
        try {
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @result < 0 THROW 51000, 'Upload lock unavailable', 1;",ct);
        } catch(Microsoft.Data.SqlClient.SqlException ex) when(ex.Number==51000) { return Conflict("Another upload is finishing. Please retry shortly."); }
        if(requestId.HasValue) {
            var existing=await db.Attachments.AsNoTracking().SingleOrDefaultAsync(a=>a.Id==requestId.Value,ct);
            if(existing!=null) {
                if(existing.EntryId!=id || existing.UploadedByUserId!=access.UserId || existing.IsDeleted || existing.ByteLength!=file.Length)return Conflict("This upload identifier has already been used.");
                await using var stream=file.OpenReadStream();
                var hash=Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream,ct));
                if(hash!=existing.Sha256)return Conflict("This upload identifier has already been used for different content.");
                return Uploaded(existing);
            }
        }
        var used=await db.Attachments.Where(a=>a.UploadedByUserId==access.UserId && !a.IsDeleted).SumAsync(a=>(long?)a.ByteLength,ct) ?? 0;
        if(used+file.Length>config.GetValue("Storage:QuotaBytes",1024L*1024*1024)) return BadRequest("Your storage quota has been reached.");
        Attachment? attachment=null;
        try {
            attachment=await storage.Store(file,id,access.UserId!,ct);
            if(requestId.HasValue)attachment.Id=requestId.Value;
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Uploaded(attachment);
        } catch(InvalidDataException e) { if(attachment!=null) storage.Delete(attachment.StorageKey); return BadRequest(e.Message); }
        catch { if(attachment!=null) storage.Delete(attachment.StorageKey); throw; }
    }
    IActionResult Uploaded(Attachment attachment) {
        if(Request.Headers.Accept.ToString().Contains("application/json"))return Json(new { id=attachment.Id,url=$"/attachments/{attachment.Id}",name=attachment.OriginalFileName });
        return Redirect($"/entries/{attachment.EntryId}");
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
