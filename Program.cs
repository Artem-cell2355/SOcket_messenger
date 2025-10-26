using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Text;
class ClientInfo
{
    public required TcpClient Tcp;
    public required StreamReader Reader;
    public required StreamWriter Writer;
    public string Name = "";
}
class ChatServer
{
    private readonly TcpListener _listener;
    private readonly ConcurrentDictionary<string, ClientInfo> _clientsByName = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _logFile = "server.log";
    public ChatServer(int port) => _listener = new TcpListener(IPAddress.Any, port);

    public async Task StartAsync()
    {
        _listener.Start();
        Log($"Server started on port {((_listener.LocalEndpoint as IPEndPoint)!).Port}");
        _ = Task.Run(AcceptLoop);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; _cts.Cancel(); };
        await Task.Delay(Timeout.Infinite, _cts.Token).ContinueWith(_ => { });
    }
    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient tcp = await _listener.AcceptTcpClientAsync(_cts.Token);
            _ = Task.Run(() => HandleClientAsync(tcp));
        }
    }
    private async Task HandleClientAsync(TcpClient tcp)
    {
        using var net = tcp.GetStream();
        using var reader = new StreamReader(net, Encoding.UTF8);
        using var writer = new StreamWriter(net, Encoding.UTF8) { AutoFlush = true };

        var info = new ClientInfo { Tcp = tcp, Reader = reader, Writer = writer };

        await writer.WriteLineAsync("Welcome! Enter your nickname:");
        string? name;
        while (true)
        {
            name = await reader.ReadLineAsync();
            if (name is null) { tcp.Close(); return; }
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Contains(' ') || name.StartsWith("@"))
            {
                await writer.WriteLineAsync("Invalid nickname. Try again (no spaces, not starting with @):");
                continue;
            }
            if (_clientsByName.ContainsKey(name))
            {
                await writer.WriteLineAsync("Name already taken. Try another:");
                continue;
            }
            break;
        }

        info.Name = name!;
        _clientsByName[name!] = info;

        Log($"[JOIN] {name} connected.");
        await BroadcastAsync($"🔵 {name} joined the chat.", except: name);

        try
        {
            await writer.WriteLineAsync($"Hello, {name}! Private: '@nick message' or '/w nick message'. Type /exit to leave.");
            while (true)
            {
                var line = await reader.ReadLineAsync();
                if (line is null) break; // disconnect
                line = line.Trim();
                if (line.Length == 0) continue;

                if (line.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                {
                    await writer.WriteLineAsync("Bye!");
                    break;
                }

                if (line.StartsWith("@"))
                {
                    var idx = line.IndexOf(' ');
                    if (idx > 1)
                    {
                        var target = line.Substring(1, idx - 1);
                        var msg = line[(idx + 1)..];
                        await PrivateAsync(name!, target, msg);
                    }
                    else await writer.WriteLineAsync("Usage: @nick message");
                }
                else if (line.StartsWith("/w "))
                {
                    var rest = line[3..].Trim();
                    var idx = rest.IndexOf(' ');
                    if (idx > 0)
                    {
                        var target = rest[..idx];
                        var msg = rest[(idx + 1)..];
                        await PrivateAsync(name!, target, msg);
                    }
                    else await writer.WriteLineAsync("Usage: /w nick message");
                }
                else
                {
                    var payload = $"[{Timestamp()}] {name}: {line}";
                    Log(payload);
                    await BroadcastAsync(payload, except: null);
                }
            }
        }
        catch (IOException) { /* client dropped */ }
        finally
        {
            Cleanup(name!);
        }
    }
    private async Task BroadcastAsync(string message, string? except)
    {
        Log($"[BROADCAST] {message}");
        foreach (var (nick, ci) in _clientsByName)
        {
            if (except != null && nick == except) continue;
            try { await ci.Writer.WriteLineAsync(message); } catch { }
        }
    }

    private async Task PrivateAsync(string from, string to, string msg)
    {
        var text = $"[whisper] {from} → {to}: {msg}";
        Log($"[PRIVATE] {text}");
        if (_clientsByName.TryGetValue(to, out var target))
        {
            try { await target.Writer.WriteLineAsync(text); } catch { }
            if (_clientsByName.TryGetValue(from, out var me))
                try { await me.Writer.WriteLineAsync(text); } catch { }
        }
        else
        {
            if (_clientsByName.TryGetValue(from, out var me))
                try { await me.Writer.WriteLineAsync($"User '{to}' not found."); } catch { }
        }
    }

    private void Cleanup(string name)
    {
        if (_clientsByName.TryRemove(name, out var ci))
        {
            try { ci.Tcp.Close(); } catch { }
            Log($"[LEAVE] {name} disconnected.");
            _ = BroadcastAsync($"🔴 {name} left the chat.", except: null);
        }
    }

    private void Log(string line)
    {
        var text = $"[{Timestamp()}] {line}";
        Console.WriteLine(text);
        try { File.AppendAllText(_logFile, text + Environment.NewLine, Encoding.UTF8); } catch { }
    }

    private static string Timestamp() => DateTime.Now.ToString("HH:mm:ss");
}

class Program
{
    static async Task Main(string[] args)
    {
        int port = args.Length > 0 && int.TryParse(args[0], out var p) ? p : 5555;
        var server = new ChatServer(port);
        await server.StartAsync();
    }
}
