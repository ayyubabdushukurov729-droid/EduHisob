using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using EduHisob.Components;
using EduHisob.Components.Account;
using EduHisob.Data;

var builder = WebApplication.CreateBuilder(args);
var databaseProvider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
var usePostgreSql = databaseProvider.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase);
if (!usePostgreSql && !databaseProvider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException($"Unsupported database provider '{databaseProvider}'.");
}
if (builder.Environment.IsProduction() && !usePostgreSql)
{
    throw new InvalidOperationException("Production requires DatabaseProvider=PostgreSQL; SQLite is only supported for local development.");
}
if (builder.Environment.IsProduction())
{
    var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
    if (string.IsNullOrWhiteSpace(dataProtectionKeysPath))
    {
        throw new InvalidOperationException("Production requires DataProtection:KeysPath to point to persistent storage.");
    }

    Directory.CreateDirectory(dataProtectionKeysPath);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
builder.Services.AddAuthorization(options =>
    options.AddPolicy(PlatformAdminDataService.Policy, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(PlatformAdminDataService.ClaimType, "true")));

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var sqliteConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var postgreSqlConnectionString = builder.Configuration.GetConnectionString("PostgreSQL");
if (usePostgreSql && string.IsNullOrWhiteSpace(postgreSqlConnectionString))
{
    postgreSqlConnectionString = ParsePostgreSqlDatabaseUrl(builder.Configuration["DATABASE_URL"]);
}

if (usePostgreSql && string.IsNullOrWhiteSpace(postgreSqlConnectionString))
{
    throw new InvalidOperationException("Production requires the ConnectionStrings:PostgreSQL or DATABASE_URL setting.");
}
if (!usePostgreSql && string.IsNullOrWhiteSpace(sqliteConnectionString))
{
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
}

if (usePostgreSql)
{
    builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
        options.UseNpgsql(postgreSqlConnectionString));
    builder.Services.AddDbContextFactory<PostgreSqlApplicationDbContext>(options =>
        options.UseNpgsql(postgreSqlConnectionString, postgres =>
            postgres.MigrationsHistoryTable("__EFMigrationsHistory_PostgreSQL")));
}
else
{
    builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
        options.UseSqlite(sqliteConnectionString));
}
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    // Platforma sahifalari platforma egasining kirish sahifasiga yuboradi.
    var redirectToLogin = options.Events.OnRedirectToLogin;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/platforma"))
        {
            context.Response.Redirect("/platforma/kirish");
            return Task.CompletedTask;
        }

        return redirectToLogin(context);
    };
});
builder.Services.AddAntiforgery(options =>
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always);

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager<CenterSignInManager>()
    .AddClaimsPrincipalFactory<CenterUserClaimsPrincipalFactory>()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
builder.Services.AddSingleton(new RegistrationAvailability(
    !builder.Environment.IsProduction() || builder.Configuration.GetValue<bool>("Registration:Enabled")));
builder.Services.AddScoped<CenterAccess>();
builder.Services.AddScoped<CenterDataService>();
builder.Services.AddScoped<StudentAccountService>();
builder.Services.AddScoped<CashDeskService>();
builder.Services.AddScoped<SalesService>();
builder.Services.AddScoped<TeamAccessService>();
builder.Services.AddScoped<GlobalSearchService>();
builder.Services.AddScoped<EduHisob.Data.Assistant.AssistantTools>();
builder.Services.AddScoped<EduHisob.Data.Assistant.LocalAgent>();
builder.Services.AddScoped<EduHisob.Data.Assistant.AssistantHistoryService>();
builder.Services.AddScoped<PlatformAdminDataService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    if (usePostgreSql)
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PostgreSqlApplicationDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }
    else
    {
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        await db.Database.MigrateAsync();
    }

    // Platforma egasining akkaunti sozlamadan yaratiladi: PlatformAdmin:Phone va PlatformAdmin:Password.
    await PlatformAdminDataService.EnsureAdminAccountAsync(
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
        app.Configuration,
        app.Logger);
}

// Serverda sayt nginx ortida ishlaydi: asl manzil va https sxemasi shu sarlavhalardan olinadi
// (faqat shu kompyuterdagi proksiga ishoniladi).
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

app.Run();

static string? ParsePostgreSqlDatabaseUrl(string? databaseUrl)
{
    if (string.IsNullOrWhiteSpace(databaseUrl))
    {
        return null;
    }

    if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri) ||
        (uri.Scheme != "postgres" && uri.Scheme != "postgresql") ||
        string.IsNullOrWhiteSpace(uri.Host) ||
        string.IsNullOrWhiteSpace(uri.AbsolutePath.Trim('/')))
    {
        throw new InvalidOperationException("DATABASE_URL must be a valid PostgreSQL database URL.");
    }

    var credentials = uri.UserInfo.Split(':', 2);
    if (credentials.Length != 2 || string.IsNullOrWhiteSpace(credentials[0]))
    {
        throw new InvalidOperationException("DATABASE_URL must include a PostgreSQL username and password.");
    }

    return new NpgsqlConnectionStringBuilder
    {
        Host = uri.Host,
        Port = uri.IsDefaultPort ? 5432 : uri.Port,
        Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')),
        Username = Uri.UnescapeDataString(credentials[0]),
        Password = Uri.UnescapeDataString(credentials[1]),
        SslMode = SslMode.Require
    }.ConnectionString;
}
