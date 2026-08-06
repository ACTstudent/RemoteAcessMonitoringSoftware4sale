using Microsoft.EntityFrameworkCore;
using Server.Data;
using Server.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Application services
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();
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
});

// SignalR for real-time screen streaming & remote control
builder.Services.AddSignalR();

// EF Core — SQL Server (default, LocalDB) or MySQL via appsettings "DatabaseProvider": "MySql"
var provider = builder.Configuration["DatabaseProvider"] ?? "SqlServer";
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
}

// Create the schema automatically on first run (no manual migration step required)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        // Surface DB connectivity problems clearly instead of failing silently
        Console.Error.WriteLine($"[CAMS] Database init failed: {ex.Message}");
        throw;
    }
}

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
app.UseSession();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.MapHub<Server.Hubs.RemoteMonitoringHub>("/remoteMonitoringHub");

app.Run();
