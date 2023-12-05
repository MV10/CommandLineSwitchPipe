using CommandLineSwitchPipe;
using System.Diagnostics;

namespace tcpargs;

internal class Program
{
    static Stopwatch timer = new();

    static async Task Main(string[] args)
    {
        int port = 0;

        if (args.Length < 3 || !int.TryParse(args[1], out port))
        {
            ShowHelp();
            return;
        }

        string server = args[0];
        var arguments = args[2..];

        Console.WriteLine("tcpargs: Forcing console logging for demo purposes.");
        CommandLineSwitchServer.Options.LogToConsole = true;

        // 0 is default/unspecified; IPv4 is 2, IPv6 is 23. If connection attempts seem slow
        // and your network doesn't use IPv6, uncomment this to ignore IPv6. See the repository
        // README for details.
        //CommandLineSwitchServer.Options.Advanced.DnsAddressFamily = 2; 

        Console.WriteLine($"tcpargs: Calling TryConnect for {server}:{port}.");
        timer.Restart();
        if(!await CommandLineSwitchServer.TryConnect(server, port))
        {
            timer.Stop();
            Console.WriteLine($"tcpargs: Failed to connect to server. ({timer.ElapsedMilliseconds} ms)");
            return;
        }
        timer.Stop();
        Console.WriteLine($"tcpargs: Connected. ({timer.ElapsedMilliseconds} ms)");

        Console.WriteLine($"tcpargs: Sending {arguments.Length} args to TCP switch server.");
        timer.Restart();
        if (!await CommandLineSwitchServer.TrySendArgs(arguments, server, port))
        {
            timer.Stop();
            Console.WriteLine($"tcpargs: Failed to send arguments to server. ({timer.ElapsedMilliseconds} ms)");
            return;
        }
        timer.Stop();
        Console.WriteLine($"tcpargs: Sent. ({timer.ElapsedMilliseconds} ms)");

        Console.WriteLine($"tcpargs: Response: {CommandLineSwitchServer.QueryResponse}");
    }

    static void ShowHelp()
        => Console.WriteLine("\ntcpargs\nSends a string of switches and arguments to a given remote endpoint using CommandLineSwitchPipe.\n\ntcpargs [server] [port] [arg1] [arg2] ... [argN]\n");
}
