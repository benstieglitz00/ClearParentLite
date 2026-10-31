using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ClearParentLite.Data;

namespace ClearParentLite.Services;
public interface IPrivateStorage
{
    Task<Attachment> Store(IFormFile file, Guid entryId, string userId, CancellationToken ct);
    Stream Open(string key);
    void Delete(string key);
}
public class PrivateStorage(IWebHostEnvironment env, IConfiguration config) : IPrivateStorage
{
    string Root {
        get {
            var root = Path.GetFullPath(config["Storage:Root"] ?? Path.Combine(env.ContentRootPath, "App_Data", "uploads"));
            var web = Path.GetFullPath(env.WebRootPath ?? Path.Combine(env.ContentRootPath,"wwwroot"));
            if (root.Equals(web,StringComparison.OrdinalIgnoreCase) || root.StartsWith(web+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Storage must be outside wwwroot.");
            Directory.CreateDirectory(root); return root;
        }
    }
    string PathFor(string key) => key.Length == 64 && key.All(Uri.IsHexDigit) ? Path.Combine(Root,key) : throw new InvalidOperationException("Invalid storage key.");
    public Stream Open(string key) => new FileStream(PathFor(key),FileMode.Open,FileAccess.Read,FileShare.Read);
    public void Delete(string key) { var path=PathFor(key); if (File.Exists(path)) File.Delete(path); }
    public static string Validate(string name, byte[] bytes) {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        bool Starts(params byte[] signature) => bytes.AsSpan().StartsWith(signature);
        var type = ext switch {
            ".png" when Starts(137,80,78,71,13,10,26,10) => "image/png",
            ".jpg" or ".jpeg" when Starts(255,216,255) => "image/jpeg",
            ".gif" when bytes.Length > 6 && (Encoding.ASCII.GetString(bytes,0,6) is "GIF87a" or "GIF89a") => "image/gif",
            ".webp" when bytes.Length > 12 && Encoding.ASCII.GetString(bytes,0,4)=="RIFF" && Encoding.ASCII.GetString(bytes,8,4)=="WEBP" => "image/webp",
            ".pdf" when Starts(37,80,68,70,45) => "application/pdf",
            ".doc" when Starts(208,207,17,224,161,177,26,225) => "application/msword",
            ".docx" when Starts(80,75,3,4) => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => throw new InvalidDataException("Use a valid PNG, JPEG, GIF, WebP, PDF, DOC or DOCX file.")
        };
        if (ext == ".pdf") {
            var text = Encoding.Latin1.GetString(bytes);
            foreach (var active in new[]{"/JavaScript","/JS","/Launch","/EmbeddedFile","/OpenAction","/AA","/RichMedia"})
                if (text.Contains(active,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Active PDF content is not supported.");
        }
        if (ext == ".doc") {
            var text = Encoding.Latin1.GetString(bytes).Replace("\0","");
            if (text.Contains("VBA",StringComparison.OrdinalIgnoreCase) || text.Contains("Macros",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Macro-enabled documents are not supported.");
        }
        if (ext == ".docx") {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            if (zip.GetEntry("[Content_Types].xml") == null || zip.GetEntry("word/document.xml") == null || zip.Entries.Count > 2000 || zip.Entries.Sum(e=>e.Length) > 100*1024*1024) throw new InvalidDataException("Invalid or oversized Word document.");
            foreach (var e in zip.Entries) {
                if (e.FullName.Contains("vba",StringComparison.OrdinalIgnoreCase) || e.FullName.Contains("embeddings/",StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".bin",StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Active Office content is not supported.");
                if (e.FullName.EndsWith(".rels",StringComparison.OrdinalIgnoreCase) || e.FullName=="[Content_Types].xml") {
                    if(e.Length>1024*1024) throw new InvalidDataException("Invalid Office metadata.");
                    using var stream=e.Open();
                    XDocument doc;
                    try {
                        using var reader=System.Xml.XmlReader.Create(stream,new System.Xml.XmlReaderSettings { DtdProcessing=System.Xml.DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=1024*1024 });
                        doc=XDocument.Load(reader);
                    } catch(System.Xml.XmlException) { throw new InvalidDataException("Invalid Office metadata."); }
                    if(doc.Descendants().Attributes().Any(a=>a.Value.Equals("External",StringComparison.OrdinalIgnoreCase) || a.Value.Contains("macroEnabled",StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("External links or macros in Word files are not supported.");
                }
            }
        }
        return type;
    }
    public async Task<Attachment> Store(IFormFile file, Guid entryId, string userId, CancellationToken ct) {
        var max=config.GetValue("Storage:MaxFileBytes",20L*1024*1024);
        if(file.Length<=0 || file.Length>max) throw new InvalidDataException($"Files must be between 1 byte and {max/1024/1024} MB.");
        using var memory=new MemoryStream(); await file.CopyToAsync(memory,ct);
        if(memory.Length>max) throw new InvalidDataException("File too large.");
        var bytes=memory.ToArray();
        var name=Path.GetFileName(file.FileName.Replace('\\','/'));
        name=new string(name.Where(c=>!char.IsControl(c) && c is not '<' and not '>' and not ':' and not '"' and not '|' and not '?' and not '*').ToArray());
        if(name.Length>180) {var extension=Path.GetExtension(name);if(extension.Length>10)throw new InvalidDataException("Unsupported file extension.");name=name[..(180-extension.Length)]+extension;}
        var type=Validate(name,bytes); var key=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); var path=PathFor(key);
        try {
            await File.WriteAllBytesAsync(path,bytes,ct);
            var scanner=config["Storage:ScannerExecutable"];
            if(!string.IsNullOrWhiteSpace(scanner)) {
                var start=new System.Diagnostics.ProcessStartInfo(scanner) { UseShellExecute=false,CreateNoWindow=true };
                start.ArgumentList.Add(path);
                using var process=System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Unable to start malware scanner.");
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
                if(process.ExitCode!=0) throw new InvalidDataException("The malware scanner rejected this file or could not scan it.");
            } else if(config.GetValue("Storage:RequireMalwareScan",!env.IsDevelopment())) throw new InvalidDataException("Uploads require a configured malware scanner.");
            return new Attachment { EntryId=entryId,UploadedByUserId=userId,OriginalFileName=name,StorageKey=key,ContentType=type,ByteLength=bytes.LongLength,Sha256=Convert.ToHexString(SHA256.HashData(bytes)) };
        } catch(System.ComponentModel.Win32Exception) { File.Delete(path);throw new InvalidDataException("The malware scanner is unavailable. Contact the operator."); }
        catch { File.Delete(path); throw; }
    }
}
