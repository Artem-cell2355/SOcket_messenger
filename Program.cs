using System.Net.Sockets;
using System.Text;
class Cleint
{
    static async Task Main(string[] args)
    {
        string host = args.Length > 0 ? args[0] : "127.0.0.1";
        int port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 5555;

        Console.OutputEncoding = Encoding.UTF8;
        using var tcp = new TcpClient();
        Console.WriteLine($"Connecting to {host}:{port} ...");
        await tcp.ConnectAsync(host, port);
        using var net = tcp.GetStream();
        using var reader = new StreamReader(net, Encoding.UTF8);
        using var writer = new StreamWriter(net, Encoding.UTF8) { AutoFlush = true };

        //reader loop
        var cts = new CancellationTokenSource();
        var readTask = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break;
                    Console.WriteLine(line);
                }
            }
            catch { }
            finally { cts.Cancel(); }
        });

        //nickname negotiation
        var prompt = await reader.ReadLineAsync();
        if (prompt != null) Console.WriteLine(prompt);
        string? name = Console.ReadLine();
        await writer.WriteLineAsync(name ?? "User");

        Console.WriteLine("Private: '@nick hi' or '/w nick hi'. Type /exit to quit.");

        //write loop
        while (!cts.IsCancellationRequested)
        {
            var line = Console.ReadLine();
            if (line == null) break;
            await writer.WriteLineAsync(line);
            if (line.Trim().Equals("/exit", StringComparison.OrdinalIgnoreCase))
                break;
        }

        cts.Cancel();
        try { await readTask; } catch { }
        Console.WriteLine("Disconnected.");
    }
}
