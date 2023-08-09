using CommandLineSwitchPipe;

namespace tcpargs;

internal class Program
{
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

        Console.WriteLine($"tcpargs: Calling TryConnect for {server}:{port}.");
        if(!await CommandLineSwitchServer.TryConnect(server, port))
        {
            Console.WriteLine("tcpargs: Failed to connect to server.");
            return;
        }

        Console.WriteLine($"tcpargs: Sending {arguments.Length} args to TCP switch server.");
        if (!await CommandLineSwitchServer.TrySendArgs(arguments, server, port))
        {
            Console.WriteLine("tcpargs: Failed to send arguments to server.");
            return;
        }

        Console.WriteLine($"tcpargs: Response: {CommandLineSwitchServer.QueryResponse}");
    }

    static void ShowHelp()
        => Console.WriteLine("\ntcpargs\nSends a string of switches and arguments to a given remote endpoint using CommandLineSwitchPipe.\n\ntcpargs [server] [port] [arg1] [arg2] ... [argN]\n");
}
