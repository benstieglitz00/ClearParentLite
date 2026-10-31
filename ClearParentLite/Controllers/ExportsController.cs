using ClearParentLite.Data;
using ClearParentLite.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
namespace ClearParentLite.Controllers;
[Authorize(Policy="Parent")]
public class ExportsController(AppDbContext db,AccessService access,IExportService exports,IPrivateStorage storage,IWebHostEnvironment env) : Controller
{
    static readonly SemaphoreSlim Gate=new(1,1);
    [HttpGet("/exports")] public async Task<IActionResult> Index(Guid? selected) {ViewBag.Selected=selected;return View(await db.Entries.AsNoTracking().Where(e=>e.OwnerUserId==access.UserId).OrderByDescending(e=>e.EventDate).Take(500).ToListAsync());}
    [HttpPost("/exports")] public async Task<IActionResult> Create(string format,string scope,Guid[] entryIds,DateOnly? from,DateOnly? to,CancellationToken ct) {
        if(!ModelState.IsValid) return BadRequest("Invalid export selection or date range.");
        if(format is not ("pdf" or "zip") || scope is not ("all" or "selected" or "dates") || (from.HasValue && to.HasValue && from>to))return BadRequest("Invalid export options.");
        var query=db.Entries.AsNoTracking().Where(e=>e.OwnerUserId==access.UserId);
        if(scope=="selected") {
            var ids=entryIds.Distinct().ToArray();if(ids.Length==0||ids.Length>500||await query.CountAsync(e=>ids.Contains(e.Id),ct)!=ids.Length)return NotFound();
            query=query.Where(e=>ids.Contains(e.Id));
        }
        if(scope=="dates") { if(!from.HasValue&&!to.HasValue)return BadRequest("Choose a date range.");if(from.HasValue)query=query.Where(e=>e.EventDate>=from);if(to.HasValue)query=query.Where(e=>e.EventDate<=to); }
        var entries=await query.Include(e=>e.Updates).Include(e=>e.Attachments).Include(e=>e.Revisions).AsSplitQuery().OrderByDescending(e=>e.EventDate).ThenBy(e=>e.Id).Take(501).ToListAsync(ct);
        if(entries.Count==0)return BadRequest("No entries match this export.");
        if(entries.Count>500 || entries.Sum(e=>(long)e.DescriptionHtml.Length+e.Updates.Sum(u=>(long)u.BodyHtml.Length)+e.Revisions.Sum(r=>(long)r.DescriptionHtml.Length))>2_000_000 || entries.Sum(e=>e.Attachments.Where(a=>!a.IsDeleted).Sum(a=>a.ByteLength))>250L*1024*1024)return BadRequest("Export limit exceeded. Select fewer entries (maximum 500 entries, 2 million text characters, 250 MB of files).");
        if(!await Gate.WaitAsync(0,ct))return StatusCode(429,"An export is already being generated. Try again shortly.");
        FileStream? output=null;
        try {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(60));var token=timeout.Token;
            var now=DateTime.UtcNow;var pdf=await Task.Run(()=>exports.Pdf(entries,now,token),token);token.ThrowIfCancellationRequested();
            var root=Path.Combine(env.ContentRootPath,"App_Data","exports");Directory.CreateDirectory(root);
            output=new FileStream(Path.Combine(root,Guid.NewGuid().ToString("N")),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,81920,FileOptions.DeleteOnClose|FileOptions.Asynchronous);
            if(format=="pdf")await output.WriteAsync(pdf,token);
            else {
                using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)) {
                    await using(var report=zip.CreateEntry("report.pdf").Open())await report.WriteAsync(pdf,token);
                    var manifest=new List<object>();
                    foreach(var entry in entries)foreach(var a in entry.Attachments.Where(a=>!a.IsDeleted)) {
                        token.ThrowIfCancellationRequested();var path=$"attachments/{entry.Id:N}/{a.Id:N}_{a.OriginalFileName}";
                        await using var input=storage.Open(a.StorageKey);await using var target=zip.CreateEntry(path,CompressionLevel.Fastest).Open();
                        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[81920];int count;long copied=0;
                        while((count=await input.ReadAsync(buffer,token))>0){hash.AppendData(buffer,0,count);await target.WriteAsync(buffer.AsMemory(0,count),token);copied+=count;}
                        if(copied!=a.ByteLength||Convert.ToHexString(hash.GetHashAndReset())!=a.Sha256)throw new InvalidDataException("An attachment failed its integrity check.");
                        manifest.Add(new { entryId=entry.Id,attachmentId=a.Id,originalName=a.OriginalFileName,path,sha256=a.Sha256,byteLength=a.ByteLength });
                    }
                    await using var metadata=zip.CreateEntry("manifest.json").Open();await JsonSerializer.SerializeAsync(metadata,new { exportedUtc=now,entryIds=entries.Select(e=>e.Id),attachments=manifest },cancellationToken:token);
                }
            }
            access.Audit(access.UserId!,"Export"+format.ToUpperInvariant(),"Timeline",access.UserId!);await db.SaveChangesAsync(ct);output.Position=0;
            return File(output,format=="pdf"?"application/pdf":"application/zip",$"clearparent-{now:yyyyMMdd-HHmmss}.{format}");
        } catch(FileNotFoundException) {if(output!=null)await output.DisposeAsync();return StatusCode(409,"An attachment is unavailable. Contact the operator before exporting.");}
        catch(OperationCanceledException) {if(output!=null)await output.DisposeAsync();return StatusCode(408,"Export timed out. Select fewer records and retry.");}
        catch {if(output!=null)await output.DisposeAsync();throw;}
        finally {Gate.Release();}
    }
}
