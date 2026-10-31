using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using AngleSharp.Html.Parser;
using ClearParentLite.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace ClearParentLite.Tests;
[Collection("SQL integration")]
public class SharingExportTests(AppFactory app) : IClassFixture<AppFactory>
{
    static async Task<string> Entry(HttpClient c,string title,string date="2026-10-12") {
        var r=await AppFactory.Post(c,"/entries/new",new(){["Title"]=title,["DescriptionHtml"]="<p>A factual description with <strong>context</strong>.</p>",["EventDate"]=date});Assert.Equal(HttpStatusCode.Redirect,r.StatusCode);return r.Headers.Location!.ToString();
    }
    static async Task<HttpResponseMessage> Upload(HttpClient c,string path,byte[] bytes,string name) {
        using var data=new MultipartFormDataContent();data.Add(new StringContent(await AppFactory.Token(c,path)),"__RequestVerificationToken");data.Add(new ByteArrayContent(bytes),"file",name);return await c.PostAsync(path+"/attachments",data);
    }
    [Fact] public async Task PrivateFilesSelectedLinksInvitationsRevocationAndScopedExports() {
        using var alice=await app.Login("alice@example.test");using var bob=await app.Login("bob@example.test");using var guest=await app.Login("guest@example.test");using var anonymous=app.Client();
        var selected=await Entry(alice,"Shared appointment");var hidden=await Entry(alice,"Hidden private event","2026-09-01");var foreign=await Entry(bob,"Bob private event");
        var selectedId=Guid.Parse(selected.Split('/').Last());var hiddenId=Guid.Parse(hidden.Split('/').Last());var foreignId=Guid.Parse(foreign.Split('/').Last());
        byte[] png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2S8AAAAASUVORK5CYII=");
        Assert.Equal(HttpStatusCode.Redirect,(await Upload(alice,selected,png,"../../screenshot.png")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Upload(alice,selected,Encoding.UTF8.GetBytes("MZ"),"fake.pdf")).StatusCode);
        using var dbScope=app.Services.CreateScope();var db=dbScope.ServiceProvider.GetRequiredService<AppDbContext>();var file=await db.Attachments.SingleAsync(a=>a.EntryId==selectedId);var filePath="/attachments/"+file.Id;
        Assert.Equal("screenshot.png",file.OriginalFileName);Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync(filePath)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync(filePath)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync("/App_Data/uploads/"+file.StorageKey)).StatusCode);
        var token=await AppFactory.Token(alice,"/shares");var share=await alice.PostAsync("/shares",new FormUrlEncodedContent(new Dictionary<string,string>{["__RequestVerificationToken"]=token,["Scope"]="SelectedEntries",["AccessMethod"]="Link",["EntryIds"]=selectedId.ToString(),["Days"]="7"}));share.EnsureSuccessStatusCode();
        var page=new HtmlParser().ParseDocument(await share.Content.ReadAsStringAsync());var url=page.QuerySelector("#share-url")!.GetAttribute("value")!;
        var opened=await anonymous.GetAsync(url);Assert.Equal(HttpStatusCode.Redirect,opened.StatusCode);var sharedPath=opened.Headers.Location!.ToString();
        var sharedHtml=await anonymous.GetStringAsync(sharedPath);Assert.Contains("Shared appointment",sharedHtml);Assert.DoesNotContain("Hidden private event",sharedHtml);
        var read=await anonymous.GetStringAsync(selected);Assert.DoesNotContain("Edit entry",read);Assert.DoesNotContain("Add update",read);
        Assert.Equal(png,await anonymous.GetByteArrayAsync(filePath));Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync(hidden)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await anonymous.PostAsync(selected+"/edit",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await guest.PostAsync("/exports",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await AppFactory.Post(alice,"/exports",new(){["scope"]="selected",["format"]="zip",["entryIds"]=foreignId.ToString()})).StatusCode);
        var zipResponse=await AppFactory.Post(alice,"/exports",new(){["scope"]="selected",["format"]="zip",["entryIds"]=selectedId.ToString()});zipResponse.EnsureSuccessStatusCode();
        var bytes=await zipResponse.Content.ReadAsByteArrayAsync();using var zip=new ZipArchive(new MemoryStream(bytes));Assert.Equal(3,zip.Entries.Count);
        using var manifest=JsonDocument.Parse(await new StreamReader(zip.GetEntry("manifest.json")!.Open()).ReadToEndAsync());Assert.Equal(selectedId,manifest.RootElement.GetProperty("entryIds")[0].GetGuid());
        var a=manifest.RootElement.GetProperty("attachments")[0];Assert.Equal(file.Sha256,a.GetProperty("sha256").GetString());using var attachmentMemory=new MemoryStream();await zip.GetEntry(a.GetProperty("path").GetString()!)!.Open().CopyToAsync(attachmentMemory);Assert.Equal(file.Sha256,Convert.ToHexString(SHA256.HashData(attachmentMemory.ToArray())));
        using var reportMemory=new MemoryStream();await zip.GetEntry("report.pdf")!.Open().CopyToAsync(reportMemory);Assert.StartsWith("%PDF",Encoding.ASCII.GetString(reportMemory.ToArray(),0,4));
        var output=Path.Combine(Directory.GetCurrentDirectory(),"App_Data","qa");Directory.CreateDirectory(output);await File.WriteAllBytesAsync(Path.Combine(output,"report.pdf"),reportMemory.ToArray());
        Assert.Equal(HttpStatusCode.OK,(await AppFactory.Post(alice,"/exports",new(){["scope"]="dates",["format"]="pdf",["from"]="2026-10-01",["to"]="2026-10-31"})).StatusCode);
        var grantId=Guid.Parse(sharedPath.Split('/').Last());Assert.Equal(HttpStatusCode.Redirect,(await AppFactory.Post(alice,$"/shares/{grantId}/revoke",new(),"/shares")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync(selected)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync(filePath)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync(url)).StatusCode);
        var invitation=await AppFactory.Post(alice,"/shares",new(){["Scope"]="Timeline",["AccessMethod"]="Invitation",["Email"]="guest@example.test",["Days"]="1"});Assert.Equal(HttpStatusCode.Redirect,invitation.StatusCode);
        var invite=await db.Shares.SingleAsync(s=>s.InvitedEmailNormalized=="GUEST@EXAMPLE.TEST");var invitePath="/invitations/"+invite.Id;
        Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync(invitePath)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await AppFactory.Post(guest,invitePath,new())).StatusCode);
        Assert.Contains("Hidden private event",await guest.GetStringAsync("/shared/"+invite.Id));Assert.Equal(HttpStatusCode.OK,(await guest.GetAsync(hidden)).StatusCode);
        var future=await Entry(alice,"Future entry");Assert.Equal(HttpStatusCode.OK,(await guest.GetAsync(future)).StatusCode);
        invite.ExpiresUtc=DateTime.UtcNow.AddSeconds(-1);await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await guest.GetAsync(hidden)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await guest.GetAsync(filePath)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await bob.GetAsync(foreign)).StatusCode);
    }
}
