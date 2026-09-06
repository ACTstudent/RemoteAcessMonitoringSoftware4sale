using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Server.Authorization;
using Server.Data;
using Server.Services;

Directory.SetCurrentDirectory(AppContext.BaseDirectory);
var initializeOnly = args.Any(argument => string.Equals(argument, "--initialize-only", StringComparison.OrdinalIgnoreCase));

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddControllersWithViews(options =>
{
    // Carries a rejected form back to the user with what they typed still in it.
    options.Filters.Add<Server.Filters.PreserveSubmissionFilter>();
});

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
builder.Services.AddScoped<AdminControllerAuthorizationFilter>();
builder.Services.AddScoped<ActiveTeacherAuthorizationFilter>();
builder.Services.AddScoped<IClassManagementService, ClassManagementService>();
builder.Services.AddSingleton<IMonitoringService, MonitoringService>();
builder.Services.AddSingleton<IDeploymentService, DeploymentService>();
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

    // Defaults are a 15s keep-alive and a 30s client timeout, which meant a
    // dropped classroom connection sat unreported for around 25 seconds - long
    // enough for a teacher to act on a monitoring view that had already stopped
    // updating. A 5s ping lets the browser notice within about 15s.
    //
    // The browser sets its serverTimeout to three times this interval; the
    // guidance is at least double, so a single missed ping on a busy Wi-Fi link
    // does not read as a disconnection.
    options.KeepAliveInterval = TimeSpan.FromSeconds(5);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(15);
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

DatabaseRestoreStartup.ApplyPendingRestore(
    builder.Configuration,
    builder.Environment.ContentRootPath);

// CAMS is intentionally SQLite-only. Ignore legacy provider configuration.
builder.Services.AddSingleton<PolicyChangeBroadcastInterceptor>();
builder.Services.AddDbContext<ApplicationDbContext>((services, options) =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? $"Data Source={Path.Combine(AppContext.BaseDirectory, "CAMS.db")}";
    options.UseSqlite(connectionString);
    options.AddInterceptors(services.GetRequiredService<PolicyChangeBroadcastInterceptor>());
});
builder.Services.AddScoped<ITelemetryService, TelemetryService>();
builder.Services.Configure<TelemetryRetentionOptions>(builder.Configuration.GetSection("TelemetryRetention"));
builder.Services.AddHostedService<TelemetryRetentionCleanupService>();
builder.Services.AddScoped<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
builder.Services.AddScoped<IWorkstationRegistrationService, WorkstationRegistrationService>();
builder.Services.AddScoped<LabSessionLifecycleService>();
builder.Services.AddHostedService<ExpiredLabSessionCleanupService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddSingleton<CategoryPolicyEngine>();

var app = builder.Build();
// Throttling state, keyed by client address and path. See RequestThrottle for
// the window, sweep and key-ceiling behaviour.
var requestThrottle = new RequestThrottle();
const int LoginAttemptsPerMinute = 10;
const int ApiRequestsPerMinute = 120;

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Account/Login");
    app.UseHsts();
}

// Apply migrations before starting services that depend on the database.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    DatabaseInitializer.Initialize(db);
    try
    {
        AccountSeeder.SeedConfiguredAccounts(db, app.Configuration);
    }
    finally
    {
        Environment.SetEnvironmentVariable("Cams__InitialAdminPassword", null, EnvironmentVariableTarget.Process);
    }
}

if (initializeOnly)
{
    return;
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.Use(async (context, next) =>
{
    // The login ceiling is here to slow credential guessing, and guessing happens
    // on the POST. Counting form loads as well spent the same budget on ordinary
    // use: a sign-in costs one GET and one POST, so an address was refused after
    // five correct sign-ins in a minute, and eleven page loads with nothing
    // submitted at all was enough to lock it out. Only submissions count now.
    var isLoginAttempt = context.Request.Path.StartsWithSegments("/Account/Login") &&
        HttpMethods.IsPost(context.Request.Method);
    var isApiRequest = context.Request.Path.StartsWithSegments("/api");

    if (isLoginAttempt || isApiRequest)
    {
        var key = $"{context.Connection.RemoteIpAddress}:{context.Request.Path}";
        var limit = isLoginAttempt ? LoginAttemptsPerMinute : ApiRequestsPerMinute;
        if (requestThrottle.ShouldRefuse(key, limit))
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }
    }
    await next(context);
});
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data: blob:; font-src 'self' data:; connect-src 'self' ws: wss:; " +
        "object-src 'none'; frame-ancestors 'none'; base-uri 'self'";
    await next(context);
});
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception ex)
    {
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.SystemLogs.Add(new Server.Models.SystemLog { Level = "Error", Message = "An unexpected server error occurred.", StackTrace = app.Environment.IsDevelopment() ? ex.ToString() : null, Timestamp = DateTime.UtcNow });
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
