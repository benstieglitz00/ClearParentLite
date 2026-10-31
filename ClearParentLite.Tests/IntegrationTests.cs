using System.Net;
using ClearParentLite.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AngleSharp.Html.Parser;
namespace ClearParentLite.Tests;
public class IntegrationTests(AppFactory app) : IClassFixture<AppFactory>
{
    [Fact] public async Task RegistrationConfirmationResetAndAntiforgeryWork() {
        using var c=app.Client();
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsync("/account/register",new FormUrlEncodedContent(new Dictionary<string,string>()))).StatusCode);
        var r=await AppFactory.Post(c,"/account/register",new(){["DisplayName"]="New Parent",["Email"]="new@example.test",["Password"]="Original-Password123!",["ConfirmPassword"]="Original-Password123!"});r.EnsureSuccessStatusCode();
        Assert.DoesNotContain("code=",await r.Content.ReadAsStringAsync());
        var message=app.Mail.Messages.Single(m=>m.To=="new@example.test"&&m.Subject.StartsWith("Confirm"));
        (await c.GetAsync(message.Body.Split(' ').Last())).EnsureSuccessStatusCode();
        r=await AppFactory.Post(c,"/account/login",new(){["Email"]="new@example.test",["Password"]="Original-Password123!"});Assert.Equal(HttpStatusCode.Redirect,r.StatusCode);
        await AppFactory.Post(c,"/account/forgot-password",new(){["Email"]="new@example.test"});
        var reset=app.Mail.Messages.Last(m=>m.To=="new@example.test");var uri=new Uri(reset.Body.Split(' ').Last());var query=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        r=await AppFactory.Post(c,"/account/reset-password",new(){["Email"]="new@example.test",["Code"]=query["code"]!,["Password"]="Replacement-Password123!",["ConfirmPassword"]="Replacement-Password123!"},uri.PathAndQuery);r.EnsureSuccessStatusCode();
        using var fresh=app.Client();r=await AppFactory.Post(fresh,"/account/login",new(){["Email"]="new@example.test",["Password"]="Replacement-Password123!"});Assert.Equal(HttpStatusCode.Redirect,r.StatusCode);
    }
    [Theory] [InlineData("/")] [InlineData("/resources")] [InlineData("/account/login")] [InlineData("/account/register")] [InlineData("/account/forgot-password")]
    public async Task PublicPagesRender(string path) { using var c=app.Client();var response=await c.GetAsync(path);response.EnsureSuccessStatusCode();Assert.Equal("nosniff",response.Headers.GetValues("X-Content-Type-Options").Single()); }
    [Fact] public async Task TimelineOwnershipRevisionsSearchAndConcurrency() {
        using var alice=await app.Login("alice@example.test");using var bob=await app.Login("bob@example.test");
        var created=await AppFactory.Post(alice,"/entries/new",new(){["Title"]="Private school meeting",["EventDate"]="2026-10-20",["DescriptionHtml"]="<p>Initial notes <script>alert(1)</script></p>"});Assert.Equal(HttpStatusCode.Redirect,created.StatusCode);var path=created.Headers.Location!.ToString();
        Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync(path)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await bob.GetAsync(path+"/edit")).StatusCode);
        foreach(var action in new[]{"edit","updates","delete","attachments"}) {var r=await AppFactory.Post(bob,path+"/"+action,new(){["Title"]="forged",["DescriptionHtml"]="<p>bad</p>",["EventDate"]="2026-10-21",["bodyHtml"]="bad"},"/entries/new");Assert.Equal(HttpStatusCode.NotFound,r.StatusCode);}
        var form=new HtmlParser().ParseDocument(await alice.GetStringAsync(path+"/edit"));var version=form.QuerySelector("[name=RowVersion]")!.GetAttribute("value")!;
        var data=new Dictionary<string,string>{["Title"]="Revised school meeting",["DescriptionHtml"]="<p>Revised notes</p>",["EventDate"]="2026-10-20",["RowVersion"]=version};
        Assert.Equal(HttpStatusCode.Redirect,(await AppFactory.Post(alice,path+"/edit",new(data))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await AppFactory.Post(alice,path+"/edit",new(data))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await AppFactory.Post(alice,path+"/updates",new(){["bodyHtml"]="<p>Followup-unique-search</p>"},path)).StatusCode);
        var search=await alice.GetStringAsync("/timeline?q=Followup-unique-search&from=2026-10-20&to=2026-10-20");Assert.Contains("Revised school meeting",search);
        Assert.DoesNotContain("Revised school meeting",await bob.GetStringAsync("/timeline?q=school"));
        Assert.DoesNotContain("Revised school meeting",await alice.GetStringAsync("/timeline?from=2026-10-21"));
        var detail=await alice.GetStringAsync(path);Assert.Contains("Version 1",detail);Assert.Contains("Version 2",detail);Assert.DoesNotContain("<script>alert(1)",detail);
        using var scope=app.Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var id=Guid.Parse(path.Split('/').Last());
        var revision=await db.Revisions.FirstAsync(r=>r.EntryId==id);revision.Title="tampered";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
}
