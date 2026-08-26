using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Services;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddControllersWithViews();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "CAMS.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/remoteMonitoringHub") ||
                context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/remoteMonitoringHub") ||
                context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("TeacherControl", policy => policy.RequireRole("Teacher", "Admin"));
    options.AddPolicy("StudentClient", policy => policy.RequireRole("Student"));
});

// Application services
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
builder.Services.AddScoped<IClassManagementService, ClassManagementService>();
builder.Services.AddSingleton<IMonitoringService, MonitoringService>();
builder.Services.AddSingleton<SessionManagerService>();
builder.Services.AddHostedService<ServerDiscoveryService>();

// Session for login state
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

// SignalR for real-time screen streaming & remote control
builder.Services.AddSignalR(options =>
{
    options.MaximumReceiveMessageSize = 8 * 1024 * 1024;
});

var httpsPort = builder.Configuration.GetValue("Cams:HttpsPort", 5000);
var certificatePath = builder.Configuration["Cams:CertificatePath"];
var certificatePassword = builder.Configuration["Cams:CertificatePassword"];
if (string.IsNullOrWhiteSpace(certificatePath))
{
    var generatedCertificate = ServerCertificateManager.EnsureGeneratedCertificate(AppContext.BaseDirectory);
    certificatePath = generatedCertificate.CertificatePath;
    certificatePassword = generatedCertificate.CertificatePassword;
    Console.WriteLine($"[CAMS] Generated LAN certificate for this server.");
    Console.WriteLine($"[CAMS] Copy the public trust certificate to student PCs: {generatedCertificate.RootCertificatePath}");
    Console.WriteLine($"[CAMS] Server certificate thumbprint: {generatedCertificate.Thumbprint}");
}
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(httpsPort, listenOptions =>
    {
        if (!string.IsNullOrWhiteSpace(certificatePath))
        {
            if (string.IsNullOrEmpty(certificatePassword))
                listenOptions.UseHttps(certificatePath);
            else
                listenOptions.UseHttps(certificatePath, certificatePassword);
        }
        else
        {
            throw new InvalidOperationException("CAMS could not configure an HTTPS certificate.");
        }
    });
});
builder.Services.AddHttpsRedirection(options => options.HttpsPort = httpsPort);

// EF Core — Sqlite (default) or SQL Server / MySql
var provider = builder.Configuration["DatabaseProvider"] ?? "Sqlite";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (provider.Equals("MySql", StringComparison.OrdinalIgnoreCase))
    {
        options.UseMySql(
            builder.Configuration.GetConnectionString("DefaultConnection"),
            ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("DefaultConnection")));
    }
    else if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite("Data Source=CAMS.db");
    }
    else
    {
        options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
    }
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Account/Login");
    app.UseHsts();
}

// Create the schema automatically on first run (no manual migration step required)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.EnsureCreated();
        DatabaseInitializer.EnsureCurrentSchema(db);
        AccountSeeder.SeedConfiguredAccounts(db, app.Configuration);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[CAMS] Database init warning: {ex.Message}");
    }
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception ex)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.SystemLogs.Add(new Server.Models.SystemLog { Level = "Error", Message = ex.Message, StackTrace = ex.ToString(), Timestamp = DateTime.Now });
            db.SaveChanges();
        }
        catch { }
        throw;
    }
});
app.UseRouting();
app.UseAuthentication();
app.UseSession();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");
app.MapControllers();

app.MapHub<Server.Hubs.RemoteMonitoringHub>("/remoteMonitoringHub");

var listenUrl = $"https://0.0.0.0:{httpsPort}/";
var browseUrl = $"https://localhost:{httpsPort}/";
Console.WriteLine($"[CAMS] Server starting. Listening on {listenUrl}...");

_ = Task.Run(async () =>
{
    await Task.Delay(1500);
    try
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = browseUrl,
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(psi);
        Console.WriteLine("[CAMS] Browser opened.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[CAMS] Could not open browser: {ex.Message}");
    }
});
app.Run();
