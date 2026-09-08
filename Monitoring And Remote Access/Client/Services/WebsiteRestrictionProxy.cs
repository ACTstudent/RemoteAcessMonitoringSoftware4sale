using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Shared.Contracts;

namespace Client.Services;

/// <summary>
/// Session-scoped HTTP proxy. HTTPS is filtered by CONNECT destination without
/// decrypting TLS. Blocking happens before DNS/connect and independently of alerts.
/// </summary>
public sealed class WebsiteRestrictionProxy : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<TcpClient, string> _connections = new();
    private readonly ConcurrentDictionary<string, byte> _blocked = new(StringComparer.OrdinalIgnoreCase);
    private RestrictionRuleMessage[] _rules = Array.Empty<RestrictionRuleMessage>();
    private Task? _acceptTask;
    private bool _disposed;

    public int Port { get; private set; }
    public bool IsRunning => _acceptTask is { IsCompleted: false };

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_acceptTask is not null) return;
        _listener.Start(128);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptTask = AcceptConnectionsAsync(_lifetime.Token);
    }

    public void UpdateRules(IEnumerable<RestrictionRuleMessage> rules)
    {
        var snapshot = rules.Where(rule => rule.RuleType == "Website")
            .Select(rule => rule with { Target = PolicyPatternMatcher.NormalizeDomainPattern(rule.Target) ?? "" })
            .Where(rule => rule.Target.Length > 0)
            .OrderByDescending(rule => rule.Target.Count(c => c != '*'))
            .ThenByDescending(rule => rule.Mode == "Allow").ToArray();
        Volatile.Write(ref _rules, snapshot);
        // Tear down established tunnels immediately when their destination becomes
        // blocked, including downloads and sockets in background tabs.
        foreach (var connection in _connections)
        {
            if (!IsBlocked(connection.Value)) continue;
            RecordBlock(connection.Value);
            connection.Key.Dispose();
        }
    }

    public bool IsBlocked(string domain)
    {
        if (string.IsNullOrEmpty(domain)) return false;
        var rules = Volatile.Read(ref _rules);
        var match = rules.FirstOrDefault(rule => PolicyPatternMatcher.MatchesDomain(domain, rule.Target));
        return match is not null ? match.Mode != "Allow" : rules.Any(rule => rule.Mode == "Allow");
    }

    public IReadOnlyList<string> DrainBlockedDomains()
    {
        var domains = new List<string>();
        foreach (var domain in _blocked.Keys)
            if (_blocked.TryRemove(domain, out _)) domains.Add(domain);
        return domains;
    }

    private void RecordBlock(string domain)
    {
        // Keep diagnostics bounded. Dropping an alert never permits a request.
        if (_blocked.Count < 256) _blocked.TryAdd(domain, 0);
    }

    private async Task AcceptConnectionsAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(token);
                if (_connections.Count >= 512) { client.Dispose(); continue; }
                _connections.TryAdd(client, "");
                _ = HandleConnectionAsync(client, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (SocketException) { /* No DIRECT fallback: browsers fail closed. */ }
    }

    private async Task HandleConnectionAsync(TcpClient client, CancellationToken token)
    {
        using (client)
        using (var upstream = new TcpClient())
        using (var relay = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            try
            {
                var downstream = client.GetStream();
                using var setup = CancellationTokenSource.CreateLinkedTokenSource(token);
                setup.CancelAfter(TimeSpan.FromSeconds(15));
                var request = await ReadHeaderAsync(downstream, setup.Token);
                var lines = request.Header.Split("\r\n", StringSplitOptions.None);
                var firstLine = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (firstLine.Length != 3 || firstLine[2] is not ("HTTP/1.0" or "HTTP/1.1"))
                    throw new InvalidDataException("Invalid proxy request.");

                var tunnel = firstLine[0] == "CONNECT";
                var target = firstLine[1];
                if (tunnel && target.IndexOfAny(new[] { '/', '\\', '@', '?', '#' }) >= 0)
                    throw new InvalidDataException("Invalid CONNECT authority.");
                if (!Uri.TryCreate(tunnel ? $"https://{target}/" : target, UriKind.Absolute, out var uri) ||
                    (tunnel ? uri.Scheme != "https" : uri.Scheme is not ("http" or "ws")) ||
                    uri.UserInfo.Length != 0 || uri.Port < 1 || string.IsNullOrEmpty(uri.Host))
                    throw new InvalidDataException("Unsupported proxy destination.");

                var domain = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
                _connections[client] = domain;
                if (IsBlocked(domain))
                {
                    RecordBlock(domain);
                    await ReplyAsync(downstream, "403 Forbidden",
                        "This website is blocked by your school's CAMS policy. Ask your teacher if you need access.", setup.Token);
                    return;
                }

                // Avoid a self-proxy loop. Other loopback services still pass through
                // the same domain rules when the browser sends them to this proxy.
                if (uri.IsLoopback && uri.Port == Port)
                    throw new InvalidDataException("Recursive proxy request.");
                await upstream.ConnectAsync(domain, uri.Port, setup.Token);
                // A policy refresh may have arrived while DNS/connect was pending.
                if (IsBlocked(domain)) { RecordBlock(domain); return; }
                var origin = upstream.GetStream();
                if (tunnel)
                {
                    await downstream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), setup.Token);
                }
                else
                {
                    var webSocket = lines.Skip(1).Any(line =>
                        line.StartsWith("Upgrade:", StringComparison.OrdinalIgnoreCase) &&
                        line[8..].Trim().Equals("websocket", StringComparison.OrdinalIgnoreCase));
                    var header = new StringBuilder($"{firstLine[0]} {uri.PathAndQuery} HTTP/1.1\r\nHost: {uri.Authority}\r\n");
                    foreach (var line in lines.Skip(1))
                    {
                        if (line.Length == 0) break;
                        var colon = line.IndexOf(':');
                        if (colon <= 0 || char.IsWhiteSpace(line[0]))
                            throw new InvalidDataException("Invalid request header.");
                        var name = line[..colon];
                        if (name.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                            name.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase)) continue;
                        header.Append(line).Append("\r\n");
                    }
                    // One HTTP destination per connection; do not reuse a tunnel
                    // for a later, unchecked absolute-URL request.
                    header.Append(webSocket ? "Connection: Upgrade\r\n\r\n" : "Connection: close\r\n\r\n");
                    await origin.WriteAsync(Encoding.Latin1.GetBytes(header.ToString()), setup.Token);
                }
                if (request.Remainder.Length > 0)
                    await origin.WriteAsync(request.Remainder, setup.Token);

                var upload = downstream.CopyToAsync(origin, relay.Token);
                var download = origin.CopyToAsync(downstream, relay.Token);
                try
                {
                    var completed = await Task.WhenAny(upload, download);
                    if (completed == upload && upload.IsCompletedSuccessfully)
                    {
                        // A client may finish sending while still awaiting the
                        // response. Preserve that TCP half-close until download ends.
                        upstream.Client.Shutdown(SocketShutdown.Send);
                        await download;
                    }
                }
                finally
                {
                    relay.Cancel();
                    upstream.Dispose();
                    client.Dispose();
                    // Observe both tasks even when a rule update cancels a tunnel.
                    await Task.WhenAll(upload, download);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException or InvalidDataException or ArgumentException or FormatException)
            {
                // A failed destination, malformed request, or shutdown closes this
                // connection. No request is retried outside the filter.
            }
            finally
            {
                _connections.TryRemove(client, out _);
            }
        }
    }

    private static async Task<(string Header, byte[] Remainder)> ReadHeaderAsync(NetworkStream stream, CancellationToken token)
    {
        var buffer = new byte[32 * 1024];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), token);
            if (read == 0) throw new IOException("Incomplete proxy request.");
            var start = Math.Max(0, length - 3);
            length += read;
            for (var i = start; i <= length - 4; i++)
            {
                if (buffer[i] == 13 && buffer[i + 1] == 10 && buffer[i + 2] == 13 && buffer[i + 3] == 10)
                    return (Encoding.Latin1.GetString(buffer, 0, i), buffer[(i + 4)..length]);
            }
        }
        throw new InvalidDataException("Proxy header is too large.");
    }

    private static Task ReplyAsync(NetworkStream stream, string status, string message, CancellationToken token)
    {
        var body = Encoding.UTF8.GetBytes(message);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        return stream.WriteAsync(header.Concat(body).ToArray(), token).AsTask();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _listener.Stop();
        foreach (var client in _connections.Keys) client.Dispose();
        _lifetime.Dispose();
    }
}
