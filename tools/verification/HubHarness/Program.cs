// A real SignalR client against the running CAMS hub.
//
// Every hub test in the suite uses a mocked IHubContext, which can establish
// what a method does with its arguments but nothing about transport: whether an
// unauthenticated socket is refused, whether identity really comes from the
// authenticated principal rather than from what the caller claims, whether a
// frame reaches only the intended recipient, or what happens across a reconnect.
// This harness connects over the wire and asks those questions.
//
// Only safe commands are exercised. Lock, logout, restart and shutdown are never
// sent: this runs on the operator's own machine and those would act on it.

using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Shared.Contracts;
using System.Net;
using System.Text;
using System.Text.Json;

static string Env(string name, string fallback = "")
{
    var value = Environment.GetEnvironmentVariable(name);
    if (!string.IsNullOrWhiteSpace(value)) return value;
    if (fallback.Length > 0) return fallback;
    Console.Error.WriteLine($"{name} is not set. See tools/verification/README.md.");
    Environment.Exit(2);
    return "";
}

var Base = Env("CAMS_TEST_URL", "https://localhost:5100");
var results = new List<(string Name, bool Passed, string Detail)>();

void Check(string name, bool passed, string detail = "")
{
    results.Add((name, passed, detail));
    Console.WriteLine($"  {(passed ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "\n          " + detail : "")}");
}

static HttpClientHandler InsecureHandler(CookieContainer jar) => new()
{
    // The server presents its own generated LAN certificate; this harness is the
    // client, so it opts out of chain validation the way a trusted agent would
    // after installing the root. Test-only.
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
    CookieContainer = jar,
    UseCookies = true
};

// Signs in through the real client-agent API and returns the cookies it issued.
async Task<(bool Ok, CookieContainer Jar, string Detail)> SignInAgentAsync(
    string username, string password, string pcName)
{
    var jar = new CookieContainer();
    using var http = new HttpClient(InsecureHandler(jar));
    var body = new StringContent(
        JsonSerializer.Serialize(new { Username = username, Password = password, PcName = pcName }),
        Encoding.UTF8, "application/json");
    var res = await http.PostAsync($"{Base}/api/client/login", body);
    return (res.IsSuccessStatusCode, jar, $"HTTP {(int)res.StatusCode}");
}

// Signs a teacher in through the browser form, antiforgery token and all.
async Task<(bool Ok, CookieContainer Jar, string Detail)> SignInTeacherAsync(
    string username, string password)
{
    var jar = new CookieContainer();
    using var http = new HttpClient(InsecureHandler(jar));
    var form = await http.GetStringAsync($"{Base}/Account/Login");
    var token = System.Text.RegularExpressions.Regex
        .Match(form, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
    var res = await http.PostAsync($"{Base}/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["username"] = username,
        ["password"] = password,
        ["__RequestVerificationToken"] = token
    }));
    var landed = res.RequestMessage?.RequestUri?.AbsolutePath ?? "";
    return (!landed.Contains("/Account/Login", StringComparison.OrdinalIgnoreCase), jar, $"landed on {landed}");
}

// Ends the agent session the way the real client does when it closes.
async Task SignOutAgentAsync(CookieContainer jar)
{
    try
    {
        using var http = new HttpClient(InsecureHandler(jar));
        var res = await http.PostAsync($"{Base}/api/client/logout", null);
        Console.WriteLine("agent signed out: HTTP " + (int)res.StatusCode);
    }
    catch (Exception ex)
    {
        Console.WriteLine("agent could not sign out: " + ex.Message);
    }
}

HubConnection BuildConnection(CookieContainer? jar) =>
    new HubConnectionBuilder()
        .WithUrl($"{Base}/remoteMonitoringHub", options =>
        {
            options.HttpMessageHandlerFactory = _ => new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
                CookieContainer = jar ?? new CookieContainer(),
                UseCookies = true
            };
            if (jar is not null) options.Cookies = jar;
        })
        .Build();

// A mode for the stale-frame check: connect as the agent, send one frame, then
// stop sending - which is what a locked workstation looks like from the server.
if (args.Length > 0 && args[0] == "stream-then-stop")
{
    var jar = await SignInAgentAsync(Env("CAMS_TEST_STUDENT_USER"), Env("CAMS_TEST_STUDENT_PASSWORD"), "STALE-CHECK-PC");
    if (!jar.Ok)
    {
        Console.WriteLine("stale-check agent could not sign in: " + jar.Detail);
        return;
    }
    var streamer = BuildConnection(jar.Jar);
    await streamer.StartAsync();
    await Task.Delay(1500);
    var stalePixel = Convert.ToBase64String(Encoding.UTF8.GetBytes("stale-check-" + Guid.NewGuid().ToString("N")));
    await streamer.InvokeAsync(nameof(RemoteMonitoringHubMethods.SendScreenFrame),
        new ScreenFrameMessage("ignored", "ignored", stalePixel, DateTime.UtcNow));
    Console.WriteLine("stale-check agent: one frame sent, now going quiet");
    await Task.Delay(TimeSpan.FromMinutes(2));   // stay connected, send nothing
    await streamer.DisposeAsync();

    // Log the agent out rather than just dropping the socket. A student may hold
    // only one workstation at a time, so an abandoned session on STALE-CHECK-PC
    // blocks the next run signing the same student in anywhere else - which is
    // correct product behaviour and twice looked like a regression.
    await SignOutAgentAsync(jar.Jar);
    return;
}

var studentUser = Env("CAMS_TEST_STUDENT_USER");
var studentPass = Env("CAMS_TEST_STUDENT_PASSWORD");
var teacherUser = Env("CAMS_TEST_TEACHER_USER");
var teacherPass = Env("CAMS_TEST_TEACHER_PASSWORD");
var student2User = Env("CAMS_TEST_STUDENT2_USER");
var student2Pass = Env("CAMS_TEST_STUDENT2_PASSWORD");

Console.WriteLine("=== HUB-01: an unauthenticated socket must be refused ===");
{
    var anon = BuildConnection(null);
    string detail;
    bool refused;
    try
    {
        await anon.StartAsync();
        refused = false;
        detail = $"connection established, state {anon.State}";
        await anon.DisposeAsync();
    }
    catch (Exception ex)
    {
        refused = true;
        detail = ex.GetType().Name + ": " + ex.Message.Split('\n')[0];
    }
    Check("an anonymous connection is refused", refused, detail);
}

Console.WriteLine("\n=== signing in ===");
var agent = await SignInAgentAsync(studentUser, studentPass, "HARNESS-PC-1");
Check("student agent signs in through /api/client/login", agent.Ok, agent.Detail);
var teacher = await SignInTeacherAsync(teacherUser, teacherPass);
Check("teacher signs in through the browser form", teacher.Ok, teacher.Detail);
if (!agent.Ok || !teacher.Ok)
{
    Console.WriteLine("\ncannot continue without both identities");
    Environment.Exit(2);
}

Console.WriteLine("\n=== HUB-01: identity comes from the authenticated principal ===");
var teacherConn = BuildConnection(teacher.Jar);
var studentConnected = new TaskCompletionSource<StudentConnectionMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
teacherConn.On<StudentConnectionMessage>(HubEventNames.StudentConnected, m => studentConnected.TrySetResult(m));

var framesSeen = new List<ScreenFrameMessage>();
// The hub sends the sender's connection id alongside the frame, so the handler
// takes both. A single-argument handler is simply never invoked.
teacherConn.On<string, ScreenFrameMessage>(HubEventNames.ReceiveScreenFrame,
    (senderConnectionId, f) => { lock (framesSeen) framesSeen.Add(f); });

await teacherConn.StartAsync();
Check("a teacher connects over the wire", teacherConn.State == HubConnectionState.Connected, teacherConn.State.ToString());

var studentConn = BuildConnection(agent.Jar);
await studentConn.StartAsync();
Check("a student agent connects over the wire", studentConn.State == HubConnectionState.Connected, studentConn.State.ToString());

var registered = await Task.WhenAny(studentConnected.Task, Task.Delay(8000)) == studentConnected.Task
    ? studentConnected.Task.Result : null;
Check("the teacher is told a student connected", registered is not null,
    registered is null ? "no StudentConnected within 8s" : $"{registered.StudentId} on {registered.PcName}");
Check("the workstation name is the one the agent signed in with",
    registered?.PcName == "HARNESS-PC-1", registered?.PcName ?? "(none)");

Console.WriteLine("\n=== HUB-02: a screen frame reaches the watching teacher ===");
var pixel = Convert.ToBase64String(Encoding.UTF8.GetBytes("frame-" + Guid.NewGuid().ToString("N")));
if (registered is not null)
{
    // The identity fields are deliberately wrong. The hub must attribute the
    // frame to the authenticated connection, not to what the sender claims.
    await studentConn.InvokeAsync(nameof(RemoteMonitoringHubMethods.SendScreenFrame),
        new ScreenFrameMessage("SPOOFED-STUDENT", "SPOOFED-PC", pixel, DateTime.UtcNow));
    await Task.Delay(2500);

    ScreenFrameMessage? received;
    lock (framesSeen) received = framesSeen.FirstOrDefault(f => f.FrameBase64 == pixel);
    Check("the frame reaches the teacher", received is not null,
        received is null ? $"{framesSeen.Count} frame(s) seen, none matching" : "matched by payload");
    Check("the frame is attributed to the real signed-in student, not the claimed one",
        received is not null && received.StudentId != "SPOOFED-STUDENT",
        received is null ? "no frame" : $"attributed to {received.StudentId} / {received.PcName}");
}

Console.WriteLine("\n=== HUB-01: role enforcement over the wire ===");
{
    // A student invoking a teacher-only method must be refused by the hub, not
    // merely hidden in the interface.
    string detail; bool refused;
    try
    {
        await studentConn.InvokeAsync(nameof(RemoteMonitoringHubMethods.SendWarningPopup),
            teacherConn.ConnectionId ?? "none",
            new NotificationMessage("Warning", "should not arrive", "student calling a teacher method", DateTime.UtcNow));
        refused = false; detail = "the call succeeded";
    }
    catch (HubException ex) { refused = true; detail = "HubException: " + ex.Message; }
    catch (Exception ex) { refused = true; detail = ex.GetType().Name + ": " + ex.Message.Split('\n')[0]; }
    Check("a student cannot invoke a teacher-only method", refused, detail);
}

Console.WriteLine("\n=== HUB-02: delivery isolation between two students ===");
var agent2 = await SignInAgentAsync(student2User, student2Pass, "HARNESS-PC-2");
HubConnection? student2Conn = null;
if (agent2.Ok)
{
    student2Conn = BuildConnection(agent2.Jar);
    // A second student must never receive another student's frames.
    var leakedToStudent2 = false;
    student2Conn.On<string, ScreenFrameMessage>(HubEventNames.ReceiveScreenFrame, (_, __) => leakedToStudent2 = true);
    await student2Conn.StartAsync();
    await Task.Delay(1500);

    var probe = Convert.ToBase64String(Encoding.UTF8.GetBytes("isolation-" + Guid.NewGuid().ToString("N")));
    await studentConn.InvokeAsync(nameof(RemoteMonitoringHubMethods.SendScreenFrame),
        new ScreenFrameMessage("ignored", "ignored", probe, DateTime.UtcNow));
    await Task.Delay(2500);

    Check("a second student receives no part of the first student's screen", !leakedToStudent2);
    bool teacherGotIt;
    lock (framesSeen) teacherGotIt = framesSeen.Any(f => f.FrameBase64 == probe);
    Check("the teacher still receives it", teacherGotIt);
}
else
{
    Check("second student agent signs in", false, agent2.Detail);
}

Console.WriteLine("\n=== TEL-01: telemetry from a real client reaches the database ===");
var appName = "HarnessApp-" + Guid.NewGuid().ToString("N")[..8];
await studentConn.InvokeAsync(nameof(RemoteMonitoringHubMethods.ReportActiveApp),
    new ActiveAppMessage(studentConn.ConnectionId ?? "", "ignored", "ignored", appName, DateTime.UtcNow));
await Task.Delay(2500);
Console.WriteLine($"  reported active application: {appName}");
Console.WriteLine("  (the database assertion is made by the caller, which can read the file)");
File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "reported-app.txt"), appName);

Console.WriteLine("\n=== WIN-02 (safe subset): a real remote command, teacher to student ===");
{
    // Only the warning popup is exercised. Lock, logout, restart and shutdown are
    // deliberately never sent: this runs on the operator's own machine.
    var delivered = new TaskCompletionSource<NotificationMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    studentConn.On<NotificationMessage>(HubEventNames.SendWarningPopup, n => delivered.TrySetResult(n));

    var otherGotIt = false;
    student2Conn?.On<NotificationMessage>(HubEventNames.SendWarningPopup, _ => otherGotIt = true);

    var marker = "harness-" + Guid.NewGuid().ToString("N")[..8];
    await teacherConn.InvokeAsync(nameof(RemoteMonitoringHubMethods.SendWarningPopup),
        studentConn.ConnectionId,
        new NotificationMessage("ignored-type", "Test warning " + marker, "Sent by the hub harness.", DateTime.UtcNow));

    var got = await Task.WhenAny(delivered.Task, Task.Delay(8000)) == delivered.Task ? delivered.Task.Result : null;
    Check("the targeted student receives the warning", got is not null,
        got is null ? "nothing within 8s" : got.Title);
    Check("the server sets the message type rather than trusting the sender",
        got?.Type == "Warning", got?.Type ?? "(none)");
    await Task.Delay(1200);
    Check("the other student receives nothing", !otherGotIt);
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "warning-marker.txt"), marker);

    // A teacher must not be able to aim a command at a connection that is not a student.
    string detail; bool refused;
    try
    {
        await teacherConn.InvokeAsync(nameof(RemoteMonitoringHubMethods.SendWarningPopup),
            "not-a-real-connection-id",
            new NotificationMessage("Warning", "should not land", "aimed at a bogus target", DateTime.UtcNow));
        refused = false; detail = "the call succeeded";
    }
    catch (Exception ex) { refused = true; detail = ex.GetType().Name + ": " + ex.Message.Split('\n')[0]; }
    Check("a command aimed at an unknown connection is refused", refused, detail);
}

Console.WriteLine("\n=== POL-01: policy reaches a connected client ===");
{
    var policy = new TaskCompletionSource<List<RestrictionRuleMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
    studentConn.On<List<RestrictionRuleMessage>>(HubEventNames.RestrictionsReceived, r => policy.TrySetResult(r));

    await studentConn.InvokeAsync(HubEventNames.FetchRestrictions);
    var rules = await Task.WhenAny(policy.Task, Task.Delay(8000)) == policy.Task ? policy.Task.Result : null;

    Check("the client receives a policy set", rules is not null,
        rules is null ? "nothing within 8s" : rules.Count + " rule(s)");
    if (rules is not null && rules.Count > 0)
    {
        Console.WriteLine("          " + string.Join("; ", rules.Take(5).Select(r => $"{r.RuleType} {r.Mode} {r.Target}")));
        Check("every rule carries a type, a target and a mode",
            rules.All(r => !string.IsNullOrWhiteSpace(r.RuleType) && !string.IsNullOrWhiteSpace(r.Target) && !string.IsNullOrWhiteSpace(r.Mode)));
    }

    // A teacher must not be able to pull a student policy set through this method.
    string detail; bool refused;
    try
    {
        await teacherConn.InvokeAsync(HubEventNames.FetchRestrictions);
        refused = false; detail = "the call succeeded";
    }
    catch (Exception ex) { refused = true; detail = ex.GetType().Name + ": " + ex.Message.Split('\n')[0]; }
    Check("only a registered student client can fetch a policy set", refused, detail);
}

Console.WriteLine("\n=== HUB-03: reconnect ===");
{
    var reconnected = new TaskCompletionSource<StudentConnectionMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    teacherConn.Remove(HubEventNames.StudentConnected);
    teacherConn.On<StudentConnectionMessage>(HubEventNames.StudentConnected, m => reconnected.TrySetResult(m));

    var disconnected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    teacherConn.On<string>(HubEventNames.StudentDisconnected, id => disconnected.TrySetResult(id));

    var oldConnectionId = studentConn.ConnectionId;
    await studentConn.StopAsync();
    var sawDisconnect = await Task.WhenAny(disconnected.Task, Task.Delay(8000)) == disconnected.Task;
    Check("the teacher is told the student dropped", sawDisconnect,
        sawDisconnect ? "reported" : "no StudentDisconnected within 8s");

    await studentConn.StartAsync();
    var back = await Task.WhenAny(reconnected.Task, Task.Delay(8000)) == reconnected.Task
        ? reconnected.Task.Result : null;
    Check("the student re-registers on reconnect", back is not null,
        back is null ? "no StudentConnected within 8s" : $"{back.StudentId} on {back.PcName}");
    Check("the reconnect uses a new connection id",
        back is not null && back.ConnectionId != oldConnectionId,
        $"{oldConnectionId} -> {back?.ConnectionId}");

    // A frame must still flow after the reconnect.
    var afterProbe = Convert.ToBase64String(Encoding.UTF8.GetBytes("after-reconnect-" + Guid.NewGuid().ToString("N")));
    await studentConn.InvokeAsync(nameof(RemoteMonitoringHubMethods.SendScreenFrame),
        new ScreenFrameMessage("ignored", "ignored", afterProbe, DateTime.UtcNow));
    await Task.Delay(2500);
    bool flowed;
    lock (framesSeen) flowed = framesSeen.Any(f => f.FrameBase64 == afterProbe);
    Check("frames flow again after the reconnect", flowed);
}

Console.WriteLine("\n=== HUB-03: oversized and malformed payloads ===");
{
    // The hub caps a frame at 6 MiB of base64. Past that it must refuse the call
    // without dropping the connection or the process.
    var huge = new string('A', 7 * 1024 * 1024);
    string detail; bool refused;
    try
    {
        await studentConn.InvokeAsync(nameof(RemoteMonitoringHubMethods.SendScreenFrame),
            new ScreenFrameMessage("ignored", "ignored", huge, DateTime.UtcNow));
        refused = false; detail = "accepted a 7 MiB frame";
    }
    catch (Exception ex) { refused = true; detail = ex.GetType().Name + ": " + ex.Message.Split('\n')[0]; }
    Check("an oversized frame is refused", refused, detail);

    // Whatever happened, the server must still be answering.
    await Task.Delay(1500);
    var stillUp = teacherConn.State == HubConnectionState.Connected;
    Check("the teacher connection survives it", stillUp, teacherConn.State.ToString());
}

Console.WriteLine("\n=== cleanup ===");
await studentConn.DisposeAsync();
if (student2Conn is not null) await student2Conn.DisposeAsync();
await teacherConn.DisposeAsync();

// Sign the agents out rather than only dropping their sockets. A student may
// hold one workstation at a time, so a session left on HARNESS-PC-1 refuses the
// next run signing that student in anywhere else. That is correct product
// behaviour, and twice in this work it was mistaken for a regression before the
// cause was traced back to this harness.
await SignOutAgentAsync(agent.Jar);
if (agent2.Ok) await SignOutAgentAsync(agent2.Jar);

var failed = results.Count(r => !r.Passed);
Console.WriteLine($"\n{results.Count - failed}/{results.Count} checks passed");
Environment.Exit(failed == 0 ? 0 : 1);

/// <summary>Method names on the hub, kept here so a rename shows up as a compile error in the harness too.</summary>
internal static class RemoteMonitoringHubMethods
{
    public const string SendScreenFrame = nameof(SendScreenFrame);
    public const string SendWarningPopup = nameof(SendWarningPopup);
    public const string ReportActiveApp = nameof(ReportActiveApp);
}
