using System.Net;
using System.Text;
using AngleSharp.Html.Parser;
using ClearParentLite.Data;
using ClearParentLite.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity;

namespace ClearParentLite.Tests;
[CollectionDefinition("SQL integration",DisableParallelization=true)]
public class SqlIntegrationCollection { }

[Collection("SQL integration")]
public class QualityReviewTests : IAsyncLifetime
{
    readonly AppFactory app=new();
    public Task InitializeAsync()=>app.InitializeAsync();
    public Task DisposeAsync()=>((IAsyncLifetime)app).DisposeAsync();
    static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j2S8AAAAASUVORK5CYII=");
    static Dictionary<string,string> Draft(Guid? id=null)=>new(){["RequestId"]=(id??Guid.NewGuid()).ToString(),["Title"]="Review entry",["DescriptionHtml"]="<p>Line one</p><p>Line two</p>",["EventDate"]="2026-10-15"};
    static async Task<string> Create(HttpClient client,Guid? requestId=null) {
        var result=await AppFactory.Post(client,"/entries/new",Draft(requestId));Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);return result.Headers.Location!.ToString();
    }
    static async Task<HttpResponseMessage> Upload(HttpClient client,string entry,Guid id,byte[] bytes,string token) {
        using var body=new MultipartFormDataContent();body.Add(new StringContent(token),"__RequestVerificationToken");body.Add(new StringContent(id.ToString()),"requestId");body.Add(new ByteArrayContent(bytes),"file","screenshot.png");return await client.PostAsync(entry+"/attachments",body);
    }
    [Fact] public async Task RetriesDoNotDuplicateEntriesCommentsOrFiles() {
        using var alice=await app.Login("alice@example.test");var requestId=Guid.NewGuid();var data=Draft(requestId);data["__RequestVerificationToken"]=await AppFactory.Token(alice,"/entries/new");
        var created=await Task.WhenAll(alice.PostAsync("/entries/new",new FormUrlEncodedContent(data)),alice.PostAsync("/entries/new",new FormUrlEncodedContent(data)));
        Assert.All(created,r=>Assert.Equal(HttpStatusCode.Redirect,r.StatusCode));var path=$"/entries/{requestId}";
        Assert.Equal(HttpStatusCode.Redirect,(await alice.PostAsync("/entries/new",new FormUrlEncodedContent(data))).StatusCode);
        data["Title"]="Changed replay";Assert.Equal(HttpStatusCode.Conflict,(await alice.PostAsync("/entries/new",new FormUrlEncodedContent(data))).StatusCode);
        var update=new Dictionary<string,string>{["requestId"]=Guid.NewGuid().ToString(),["bodyHtml"]="<p>One comment, even after retry</p>",["__RequestVerificationToken"]=await AppFactory.Token(alice,path)};
        var comments=await Task.WhenAll(alice.PostAsync(path+"/updates",new FormUrlEncodedContent(update)),alice.PostAsync(path+"/updates",new FormUrlEncodedContent(update)));
        Assert.All(comments,r=>Assert.Equal(HttpStatusCode.Redirect,r.StatusCode));
        update["bodyHtml"]="Different comment";Assert.Equal(HttpStatusCode.Conflict,(await alice.PostAsync(path+"/updates",new FormUrlEncodedContent(update))).StatusCode);
        var token=await AppFactory.Token(alice,path);var uploadId=Guid.NewGuid();
        var files=await Task.WhenAll(Upload(alice,path,uploadId,Png,token),Upload(alice,path,uploadId,Png,token));Assert.All(files,r=>Assert.Equal(HttpStatusCode.Redirect,r.StatusCode));
        var altered=Png.ToArray();altered[^1]^=1;Assert.Equal(HttpStatusCode.Conflict,(await Upload(alice,path,uploadId,altered,token)).StatusCode);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1,await db.Entries.CountAsync());Assert.Equal(1,await db.Revisions.CountAsync());Assert.Equal(1,await db.Updates.CountAsync());Assert.Equal(1,await db.Attachments.CountAsync());
        using var bob=await app.Login("bob@example.test");Assert.Equal(HttpStatusCode.Conflict,(await AppFactory.Post(bob,"/entries/new",Draft(requestId))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await AppFactory.Post(bob,path+"/updates",update,"/entries/new")).StatusCode);
    }
    [Fact] public async Task SelectedLinkDoesNotGrantMutationOrAdministrativeBypass() {
        using var alice=await app.Login("alice@example.test");var path=await Create(alice);var id=Guid.Parse(path.Split('/').Last());
        using var scope=app.Services.CreateScope();var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();await users.AddToRoleAsync((await users.FindByEmailAsync("bob@example.test"))!,"Administrator");
        using var bob=await app.Login("bob@example.test");Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync(path)).StatusCode);
        var response=await AppFactory.Post(alice,"/shares",new(){["Scope"]="SelectedEntries",["AccessMethod"]="Link",["EntryIds"]=id.ToString(),["Days"]="1"});response.EnsureSuccessStatusCode();
        var url=new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync()).QuerySelector("#share-url")!.GetAttribute("value")!;
        var open=await bob.GetAsync(url);Assert.Equal(HttpStatusCode.Redirect,open.StatusCode);var sharedPath=open.Headers.Location!.ToString();
        var detail=await bob.GetStringAsync(path);Assert.Contains(sharedPath,detail);Assert.DoesNotContain("Version 1",detail);Assert.DoesNotContain("Edit entry",detail);
        foreach(var action in new[]{"edit","updates","delete","attachments"})Assert.Equal(HttpStatusCode.NotFound,(await AppFactory.Post(bob,path+"/"+action,Draft(),"/entries/new")).StatusCode);
        var grantId=sharedPath.Split('/').Last();Assert.Equal(HttpStatusCode.NotFound,(await AppFactory.Post(bob,$"/shares/{grantId}/revoke",new(),"/shares")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await AppFactory.Post(bob,"/exports",new(){["scope"]="selected",["format"]="pdf",["entryIds"]=id.ToString()})).StatusCode);
        var forged=await AppFactory.Post(bob,"/shares",new(){["Scope"]="SelectedEntries",["AccessMethod"]="Link",["EntryIds"]=id.ToString(),["Days"]="1"});Assert.Contains("Choose entries from your own timeline",await forged.Content.ReadAsStringAsync());
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();Assert.Equal(1,await db.Shares.CountAsync());
        var owner=await db.Users.SingleAsync(u=>u.Email=="alice@example.test");owner.DisabledUtc=DateTime.UtcNow;await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync(path)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync(url)).StatusCode);
    }
    [Fact] public async Task InvalidInputsAndCookiesFailWithoutSavingOrLeaking() {
        using var alice=await app.Login("alice@example.test");
        foreach(var field in new[]{"Title","DescriptionHtml","EventDate"}) {var draft=Draft();draft[field]="";var r=await AppFactory.Post(alice,"/entries/new",draft);Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Contains("validation",await r.Content.ReadAsStringAsync());}
        var htmlOnly=Draft();htmlOnly["DescriptionHtml"]="<img src='https://tracker.test/pixel'><script>alert(1)</script>";var invalid=await AppFactory.Post(alice,"/entries/new",htmlOnly);Assert.Contains("Add a description",await invalid.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest,(await alice.GetAsync("/timeline?from=2026-10-20&to=2026-10-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await alice.GetAsync("/timeline?from=invalid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await AppFactory.Post(alice,"/exports",new(){["scope"]="dates",["format"]="pdf",["from"]="invalid"})).StatusCode);
        var shares=new HtmlParser().ParseDocument(await alice.GetStringAsync("/shares"));Assert.Equal("SelectedEntries",shares.QuerySelector("#Scope option[selected]")!.GetAttribute("value"));
        var omitted=await AppFactory.Post(alice,"/shares",new(){["AccessMethod"]="Link",["Days"]="7"});Assert.Contains("Choose entries",await omitted.Content.ReadAsStringAsync());
        var invalidShare=await AppFactory.Post(alice,"/shares",new(){["Scope"]="Timeline",["AccessMethod"]="Link",["Days"]="0"});var invalidForm=new HtmlParser().ParseDocument(await invalidShare.Content.ReadAsStringAsync());Assert.Equal("Timeline",invalidForm.QuerySelector("#Scope option[selected]")!.GetAttribute("value"));Assert.Equal("Link",invalidForm.QuerySelector("#AccessMethod option[selected]")!.GetAttribute("value"));
        using var anonymous=app.Client();anonymous.DefaultRequestHeaders.Add("Cookie","cp.share=%%invalid-cookie");Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync("/entries/"+Guid.NewGuid())).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync("/shared/"+Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await anonymous.GetAsync("/account/confirm")).StatusCode);Assert.Equal(HttpStatusCode.BadRequest,(await anonymous.GetAsync("/account/reset-password")).StatusCode);
        foreach(var route in new[]{"/timeline","/shares","/exports","/settings"}) {var r=await alice.GetAsync(route);r.EnsureSuccessStatusCode();Assert.Contains("no-store",r.Headers.CacheControl!.ToString());Assert.Contains("noindex",r.Headers.GetValues("X-Robots-Tag").Single());}
        using var scope=app.Services.CreateScope();Assert.Equal(0,await scope.ServiceProvider.GetRequiredService<AppDbContext>().Entries.CountAsync());
    }
    [Fact] public async Task AcceptedInvitationRequiresCurrentMatchingVerifiedEmail() {
        using var alice=await app.Login("alice@example.test");using var guest=await app.Login("guest@example.test");var path=await Create(alice);
        Assert.Equal(HttpStatusCode.Redirect,(await AppFactory.Post(alice,"/shares",new(){["Scope"]="Timeline",["AccessMethod"]="Invitation",["Email"]="guest@example.test",["Days"]="7"})).StatusCode);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var grant=await db.Shares.SingleAsync();
        await AppFactory.Post(guest,"/invitations/"+grant.Id,new());Assert.Equal(HttpStatusCode.OK,(await guest.GetAsync(path)).StatusCode);
        var user=await db.Users.SingleAsync(u=>u.Email=="guest@example.test");user.EmailConfirmed=false;await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await guest.GetAsync(path)).StatusCode);
        user.EmailConfirmed=true;user.NormalizedEmail="CHANGED@EXAMPLE.TEST";await db.SaveChangesAsync();Assert.Equal(HttpStatusCode.NotFound,(await guest.GetAsync(path)).StatusCode);
    }
    [Fact] public async Task AccountEndpointsAreRateLimited() {
        using var c=app.Client();
        for(var i=0;i<20;i++)Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/account/login")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,(await c.GetAsync("/account/login")).StatusCode);
    }
    [Fact] public async Task TimelinePaginationIsStableAndDatesAreInclusive() {
        using var alice=await app.Login("alice@example.test");using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var owner=await db.Users.SingleAsync(u=>u.Email=="alice@example.test");
        for(var i=0;i<27;i++)db.Entries.Add(new TimelineEntry { OwnerUserId=owner.Id,Title=$"Same-day event {i}",EventDate=new(2026,10,15),DescriptionHtml="<p>Notes</p>",SearchText="Notes" });await db.SaveChangesAsync();
        var expected=await db.Entries.OrderByDescending(e=>e.EventDate).ThenBy(e=>e.Id).Select(e=>e.Id).ToListAsync();
        async Task<List<Guid>> Rows(string query) {var doc=new HtmlParser().ParseDocument(await alice.GetStringAsync("/timeline"+query));return doc.QuerySelectorAll(".timeline-row").Select(e=>Guid.Parse(e.GetAttribute("href")!.Split('/').Last())).ToList();}
        Assert.Equal(expected.Take(25),await Rows("?page=1&from=2026-10-15&to=2026-10-15"));Assert.Equal(expected.Skip(25),await Rows("?page=2"));Assert.Empty(await Rows("?from=2026-10-16"));
        var entry=await Create(alice);Assert.Contains("Review entry",await alice.GetStringAsync("/timeline?q=one%20Line%20two"));
        Assert.Equal(HttpStatusCode.BadRequest,(await alice.GetAsync("/timeline?q="+new string('x',201))).StatusCode);
    }
    [Fact] public async Task QuotaIsAtomicAndScannerFailureLeavesNoMetadata() {
        using var alice=await app.Login("alice@example.test");var path=await Create(alice);var token=await AppFactory.Token(alice,path);
        var config=app.Services.GetRequiredService<IConfiguration>();config["Storage:QuotaBytes"]="100";
        var uploads=await Task.WhenAll(Upload(alice,path,Guid.NewGuid(),Png,token),Upload(alice,path,Guid.NewGuid(),Png,token));
        Assert.Single(uploads,r=>r.StatusCode==HttpStatusCode.Redirect);Assert.Single(uploads,r=>r.StatusCode==HttpStatusCode.BadRequest);
        config["Storage:QuotaBytes"]="1000";config["Storage:RequireMalwareScan"]="true";
        Assert.Equal(HttpStatusCode.BadRequest,(await Upload(alice,path,Guid.NewGuid(),Png,token)).StatusCode);
        using var scope=app.Services.CreateScope();Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<AppDbContext>().Attachments.CountAsync());Assert.Single(Directory.GetFiles(app.StorageRoot));
    }
    [Fact] public async Task CorruptAndMissingAttachmentsPreventZipAndCleanTemporaryFiles() {
        using var alice=await app.Login("alice@example.test");var path=await Create(alice);var token=await AppFactory.Token(alice,path);var id=Guid.NewGuid();Assert.Equal(HttpStatusCode.Redirect,(await Upload(alice,path,id,Png,token)).StatusCode);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var file=await db.Attachments.SingleAsync();
        await System.IO.File.WriteAllBytesAsync(Path.Combine(app.StorageRoot,file.StorageKey),[1,2,3]);
        var options=new Dictionary<string,string>{["scope"]="selected",["format"]="zip",["entryIds"]=file.EntryId.ToString()};
        Assert.Equal(HttpStatusCode.Conflict,(await AppFactory.Post(alice,"/exports",new(options))).StatusCode);
        scope.ServiceProvider.GetRequiredService<IPrivateStorage>().Delete(file.StorageKey);
        Assert.Equal(HttpStatusCode.Conflict,(await AppFactory.Post(alice,"/exports",new(options))).StatusCode);
        var temp=Path.Combine(app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath,"App_Data","exports");Assert.Empty(Directory.GetFiles(temp));
    }
    [Fact] public async Task ExportsRejectOversizedHistoryBeforeRendering() {
        using var alice=await app.Login("alice@example.test");var path=await Create(alice);var id=Guid.Parse(path.Split('/').Last());
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var owner=await db.Users.SingleAsync(u=>u.Email=="alice@example.test");
        db.Updates.Add(new EntryUpdate { EntryId=id,AuthorUserId=owner.Id,BodyHtml="x"+new string(' ',2_000_000) });await db.SaveChangesAsync();
        var response=await AppFactory.Post(alice,"/exports",new(){["scope"]="selected",["format"]="pdf",["entryIds"]=id.ToString()});Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.Contains("Export limit",await response.Content.ReadAsStringAsync());
    }
    [Fact] public async Task RemovedEntriesDisappearFromSharesFilesSearchAndExportButKeepHistory() {
        using var alice=await app.Login("alice@example.test");using var anonymous=app.Client();var path=await Create(alice);var id=Guid.Parse(path.Split('/').Last());
        var token=await AppFactory.Token(alice,path);var fileId=Guid.NewGuid();Assert.Equal(HttpStatusCode.Redirect,(await Upload(alice,path,fileId,Png,token)).StatusCode);
        var share=await AppFactory.Post(alice,"/shares",new(){["Scope"]="Timeline",["AccessMethod"]="Link",["Days"]="7"});var url=new HtmlParser().ParseDocument(await share.Content.ReadAsStringAsync()).QuerySelector("#share-url")!.GetAttribute("value")!;await anonymous.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK,(await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await AppFactory.Post(alice,path+"/delete",new(),path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await alice.GetAsync(path)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync(path)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync("/attachments/"+fileId)).StatusCode);
        Assert.DoesNotContain("Review entry",await alice.GetStringAsync("/timeline?q=Review"));Assert.Equal(HttpStatusCode.NotFound,(await AppFactory.Post(alice,"/exports",new(){["scope"]="selected",["format"]="zip",["entryIds"]=id.ToString()})).StatusCode);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();Assert.True((await db.Entries.IgnoreQueryFilters().SingleAsync()).IsDeleted);Assert.Equal(1,await db.Revisions.CountAsync());Assert.Equal(1,await db.Attachments.CountAsync());
    }
    [Fact] public async Task ConfirmationAndLockoutAreEnforcedAndLoginRejectsExternalRedirects() {
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var alice=await db.Users.SingleAsync(u=>u.Email=="alice@example.test");alice.EmailConfirmed=false;await db.SaveChangesAsync();
        using var c=app.Client();Assert.Equal(HttpStatusCode.OK,(await AppFactory.Post(c,"/account/login",new(){["Email"]="alice@example.test",["Password"]="Testing-Password123!"})).StatusCode);Assert.Equal(HttpStatusCode.Redirect,(await c.GetAsync("/timeline")).StatusCode);
        for(var i=0;i<5;i++)Assert.Equal(HttpStatusCode.OK,(await AppFactory.Post(c,"/account/login",new(){["Email"]="bob@example.test",["Password"]="Wrong-Password123!"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await AppFactory.Post(c,"/account/login",new(){["Email"]="bob@example.test",["Password"]="Testing-Password123!"})).StatusCode);
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();var bob=(await users.FindByEmailAsync("bob@example.test"))!;await users.SetLockoutEndDateAsync(bob,null);
        var success=await AppFactory.Post(c,"/account/login",new(){["Email"]="bob@example.test",["Password"]="Testing-Password123!",["ReturnUrl"]="https://attacker.test"});Assert.Equal("/timeline",success.Headers.Location!.ToString());
    }
}
