using ClearParentLite.Data;
using ClearParentLite.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
namespace ClearParentLite;
public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        PdfSharp.Fonts.GlobalFontSettings.UseWindowsFontsUnderWindows = true;
        builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Configure ConnectionStrings:DefaultConnection.")));
        builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o => {
            o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 12; o.SignIn.RequireConfirmedEmail = true;
            o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        builder.Services.ConfigureApplicationCookie(o => {
            o.LoginPath = "/account/login"; o.AccessDeniedPath = "/account/denied";
            o.Cookie.HttpOnly = true; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Lax;
            o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = true;
        });
        builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
        builder.Services.Configure<Microsoft.AspNetCore.Antiforgery.AntiforgeryOptions>(o => { o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.HeaderName = "X-CSRF-TOKEN"; });
        var auth = builder.Services.AddAuthentication();
        if (!string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Google:ClientId"]) && !string.IsNullOrWhiteSpace(builder.Configuration["Authentication:Google:ClientSecret"]))
            auth.AddGoogle(o => { o.ClientId = builder.Configuration["Authentication:Google:ClientId"]!; o.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!; o.SaveTokens = false; });
        builder.Services.AddAuthorization(o => o.AddPolicy("Parent", p => p.RequireAuthenticatedUser().RequireRole("Parent").RequireClaim("email_verified", "true")));
        builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, VerifiedClaimsFactory>();
        builder.Services.AddScoped<AccessService>(); builder.Services.AddScoped<ContentService>();
        builder.Services.AddScoped<IEmailService, EmailService>();
        builder.Services.AddScoped<IPrivateStorage, PrivateStorage>();
        builder.Services.AddScoped<IExportService, ExportService>();
        builder.Services.AddRateLimiter(o => {
            o.RejectionStatusCode = 429;
            o.AddPolicy("account", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        var app = builder.Build();
        if(args.Contains("--initialize-db")) {
            using var scope=app.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
            var roles=scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach(var role in new[]{"Administrator","Parent","Read-only Guest"})if(!await roles.RoleExistsAsync(role))await roles.CreateAsync(new IdentityRole(role));
            return;
        }
        if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/Home/Error"); app.UseHsts(); }
        app.UseHttpsRedirection();
        app.Use(async (context, next) => {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Path.StartsWithSegments("/entries") || context.Request.Path.StartsWithSegments("/shared") || context.Request.Path.StartsWithSegments("/s") || context.Request.Path.StartsWithSegments("/attachments") || context.Request.Path.StartsWithSegments("/account")) context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
            await next();
        });
        app.UseStaticFiles(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication();
        app.Use(async (context, next) => {
            if (context.User.Identity?.IsAuthenticated == true) {
                var manager = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var user = await manager.GetUserAsync(context.User);
                if (user is null || user.DisabledUtc != null) { await context.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>().SignOutAsync(); context.Response.StatusCode = 403; return; }
            }
            await next();
        });
        app.UseAuthorization();
        app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
        await app.RunAsync();
    }
}
public class VerifiedClaimsFactory(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, Microsoft.Extensions.Options.IOptions<IdentityOptions> options) : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(users, roles, options)
{
    protected override async Task<System.Security.Claims.ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user) {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.EmailConfirmed && user.DisabledUtc == null) identity.AddClaim(new("email_verified", "true"));
        return identity;
    }
}
