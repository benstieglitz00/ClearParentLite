using ClearParentLite;
using ClearParentLite.Data;
using ClearParentLite.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using AngleSharp.Html.Parser;
namespace ClearParentLite.Tests;
public class TestMail : IEmailService
{
    public List<(string To,string Subject,string Body)> Messages { get; }=[];
    public Task Send(string to,string subject,string text) { Messages.Add((to,subject,text));return Task.CompletedTask; }
}
public class AppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string DatabaseName { get; }="ClearParentLiteTest_"+Guid.NewGuid().ToString("N");
    public TestMail Mail { get; }=new();
    protected override void ConfigureWebHost(IWebHostBuilder builder) {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(new Dictionary<string,string?> { ["App:PublicOrigin"]="https://localhost",["Storage:Root"]=Path.Combine(Directory.GetCurrentDirectory(),"App_Data","test-files"),["Storage:RequireMalwareScan"]="false" }));
        builder.ConfigureServices(s=>{
            s.RemoveAll<DbContextOptions<AppDbContext>>();s.AddDbContext<AppDbContext>(o=>o.UseSqlServer($"Server=(localdb)\\MSSQLLocalDB;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True"));
            s.RemoveAll<IEmailService>();s.AddSingleton<IEmailService>(Mail);
        });
    }
    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress=new Uri("https://localhost"),AllowAutoRedirect=false });
    public async Task InitializeAsync() {
        using var scope=Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();await db.Database.MigrateAsync();
        var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();foreach(var role in new[]{"Parent","Read-only Guest","Administrator"})await roles.CreateAsync(new IdentityRole(role));
        var users=scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach(var address in new[]{"alice@example.test","bob@example.test","guest@example.test"}) {
            var user=new ApplicationUser { UserName=address,Email=address,EmailConfirmed=true,DisplayName=address.Split('@')[0] };
            var r=await users.CreateAsync(user,"Testing-Password123!");if(!r.Succeeded)throw new Exception(string.Join(',',r.Errors.Select(e=>e.Description)));
            await users.AddToRoleAsync(user,address.StartsWith("guest")?"Read-only Guest":"Parent");
        }
    }
    async Task IAsyncLifetime.DisposeAsync() { using var scope=Services.CreateScope();await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureDeletedAsync();await base.DisposeAsync(); }
    public static async Task<string> Token(HttpClient client,string path) {
        var response=await client.GetAsync(path);response.EnsureSuccessStatusCode();var doc=new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());return doc.QuerySelector("input[name=__RequestVerificationToken]")?.GetAttribute("value") ?? throw new Exception("CSRF token missing at "+path);
    }
    public static async Task<HttpResponseMessage> Post(HttpClient c,string path,Dictionary<string,string> fields,string? tokenPath=null) {
        fields["__RequestVerificationToken"]=await Token(c,tokenPath??path);return await c.PostAsync(path,new FormUrlEncodedContent(fields));
    }
    public async Task<HttpClient> Login(string address) { var c=Client();var r=await Post(c,"/account/login",new(){["Email"]=address,["Password"]="Testing-Password123!"});Assert.Equal(System.Net.HttpStatusCode.Redirect,r.StatusCode);return c; }
}
