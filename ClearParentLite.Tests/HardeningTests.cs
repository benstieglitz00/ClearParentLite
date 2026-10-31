using ClearParentLite.Data;
using ClearParentLite.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using System.IO.Compression;
using System.Text;
namespace ClearParentLite.Tests;
public class HardeningTests
{
    [Fact] public async Task LongValidFilenamesKeepTheirExtension() {
        var env=new EnvironmentStub { EnvironmentName="Development" };var root=Path.Combine(env.ContentRootPath,Guid.NewGuid().ToString("N"));
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Storage:Root"]=root}).Build();var service=new PrivateStorage(env,config);
        var attachment=await service.Store(File(Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"),new string('a',240)+".pdf"),Guid.NewGuid(),"owner",default);
        Assert.EndsWith(".pdf",attachment.OriginalFileName);Assert.True(attachment.OriginalFileName.Length<=180);service.Delete(attachment.StorageKey);Directory.Delete(root);
    }
    [Theory] [InlineData("<broken")] [InlineData("<!DOCTYPE Types [<!ENTITY a 'payload'>]><Types>&a;</Types>")]
    public void MalformedOrEntityBearingOfficeMetadataIsRejected(string xml) {
        using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)) {
            using(var writer=new StreamWriter(zip.CreateEntry("[Content_Types].xml").Open()))writer.Write(xml);
            using(var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open()))writer.Write("<document/>");
        }
        Assert.Throws<InvalidDataException>(()=>PrivateStorage.Validate("document.docx",memory.ToArray()));
    }
    sealed class EnvironmentStub : IWebHostEnvironment {
        public string ApplicationName {get;set;}="Tests";public string EnvironmentName {get;set;}="Production";
        public string ContentRootPath {get;set;}=Path.Combine(Directory.GetCurrentDirectory(),"App_Data","storage-tests");
        public string WebRootPath {get;set;}=Path.Combine(Directory.GetCurrentDirectory(),"wwwroot");
        public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
    }
    static IFormFile File(byte[] data,string name) => new FormFile(new MemoryStream(data),0,data.Length,"file",name);
    [Fact] public async Task ProductionUploadsFailClosedWithoutScannerAndCleanUp() {
        var env=new EnvironmentStub();var root=Path.Combine(env.ContentRootPath,Guid.NewGuid().ToString("N"));
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Storage:Root"]=root}).Build();
        var service=new PrivateStorage(env,config);
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>service.Store(File(Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"),"a.pdf"),Guid.NewGuid(),"owner",default));
        Assert.Contains("scanner",error.Message);Assert.Empty(Directory.GetFiles(root));Directory.Delete(root);
    }
    [Fact] public async Task OversizeAndPublicStorageAreRejected() {
        var env=new EnvironmentStub();var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Storage:MaxFileBytes"]="4"}).Build();
        await Assert.ThrowsAsync<InvalidDataException>(()=>new PrivateStorage(env,config).Store(File(new byte[5],"a.png"),Guid.NewGuid(),"owner",default));
        config["Storage:MaxFileBytes"]="100";config["Storage:Root"]=env.WebRootPath;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new PrivateStorage(env,config).Store(File(Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"),"a.pdf"),Guid.NewGuid(),"owner",default));
    }
    [Fact] public void MacroEnabledOfficeContainerIsRejected() {
        using var memory=new MemoryStream();using(var zip=new ZipArchive(memory,ZipArchiveMode.Create,true)) {
            foreach(var name in new[]{"[Content_Types].xml","word/document.xml","word/vbaProject.bin"}) {using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write("<document/>");}
        }
        Assert.Throws<InvalidDataException>(()=>PrivateStorage.Validate("document.docx",memory.ToArray()));
    }
    [Fact] public void RemoteImagesAndIndirectFileReferencesAreRemovedFromRichText() {
        var id=Guid.NewGuid();var content=new ContentService();
        var html=content.Sanitize($"<img src='//tracker.test/x'><img src='/attachments/{id}?x=1'><iframe src='/attachments/{id}'></iframe><p style='background:url(https://tracker.test)'>Safe</p>",[id]);
        Assert.DoesNotContain("<img",html);Assert.DoesNotContain("<iframe",html);Assert.DoesNotContain("style",html);Assert.Contains("Safe",html);
    }
}
