using CommandLineSwitchPipe;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace demo
{
    class Program
    {
        // Cancel this to terminate the switch server's named pipe.
        private static CancellationTokenSource ctsSwitchPipe;

        // Cancel this to terminate the running instance.
        private static CancellationTokenSource ctsRunningInstance;

        // This can be set by starting the program with: demo --port [PORT]
        private static int tcpListeningPort = 0;

        static async Task Main(string[] args)
        {
            Console.Clear();

            try
            {
                Output($"\nStarting PID {Environment.ProcessId}");

                Output("Forcing console logging for demo purposes.");
                Output("Anything not prefixed by \"demo\" is logged by the library");
                CommandLineSwitchServer.Options.LogToConsole = true;

                Output("Disabling ArgumentException if secondary instance started without arguments.\n\n");
                CommandLineSwitchServer.Options.Advanced.ThrowIfRunning = false;

                // Test the connection
                if (await CommandLineSwitchServer.TryConnect())
                {
                    Output("\nSuccessfully connected to another instance.");
                }
                else
                {
                    Output("\nDid not connect to another instance. For demo-logging purposes, args will be sent anyway.");
                    if(CommandLineSwitchServer.TryException is not null)
                    {
                        Output($"\nConnection attempt returned {CommandLineSwitchServer.TryException}.");
                    }
                }

                // Try to send any command line switches to an already-running instance.
                // If this returns true, the switches were sent and this instance can exit.
                // If this returns false, report any exceptions and try to become the running instance.
                if (await CommandLineSwitchServer.TrySendArgs())
                {
                    Output($"\nQueryResponse after sending argument list: \"{CommandLineSwitchServer.QueryResponse}\"");
                    return;
                }
                else
                {
                    if (CommandLineSwitchServer.TryException is not null)
                    {
                        Output($"\nConnection attempt returned {CommandLineSwitchServer.TryException}.");
                        Output("\nProceeding to become the running instance anyway.");
                    }
                }


                // Another instance is not running. This instance will start listening for
                // new switches, and will process any switches provided to this instance.
                Output($"\nResults of processing startup switches:");
                Output(ProcessSwitches(args, argsReceivedFromPipe: false));

                ctsSwitchPipe = new();
                _ = Task.Run(() => CommandLineSwitchServer.StartServer(ProcessSwitches, ctsSwitchPipe.Token, tcpListeningPort), ctsSwitchPipe.Token);

                // Loop until somebody sends a --quit switch.
                Output($"\n\nApplication is running.\n* --quit\tTerminates the running instance\n* --date\tReturns the current date\n* --time\tReturns the current time\n\n");
                ctsRunningInstance = new();
                while (!ctsRunningInstance.IsCancellationRequested)
                {
                    await Task.Delay(1000, ctsRunningInstance.Token);
                    Console.WriteLine($"{DateTime.Now}                  ");
                    Console.SetCursorPosition(0, Console.CursorTop - 1);
                };
                Output("\nRunning instance exited polling loop in Program.Main");
            }
            catch (OperationCanceledException)
            {
                Output("\nRunning instance caught OperationCanceledException, ending polling loop in Program.Main");
            }
            catch (Exception ex)
            {
                Output($"\nException of type {ex.GetType().Name}\n{ex.Message}");
                if (ex.InnerException != null) Console.Write(ex.InnerException.Message);
                Output($"\n{ex.StackTrace}");
            }
            finally
            {
                // Stephen Cleary says disposal is unnecessary as long as the token is cancelled
                ctsSwitchPipe?.Cancel();
                ctsRunningInstance?.Cancel(); // in case of exception...
            }

            Output($"Application will exit after 1000ms async delay");
            await Task.Delay(1000);
            Output($"Exiting PID {Environment.ProcessId}");
        }

        private static string ProcessSwitches(string[] args)
        {
            Output($"Processing {args.Length} arguments from non-server instance");
            return ProcessSwitches(args, argsReceivedFromPipe: true);
        }

        private static string ProcessSwitches(string[] args, bool argsReceivedFromPipe)
        {
            if (args.Length == 0)
                return string.Empty;

            if (!argsReceivedFromPipe)
            {
                Output($"Processing {args.Length} arguments directly from the console");

                if(args.Length != 2 || !(args[0].Equals("--port", StringComparison.InvariantCultureIgnoreCase) && int.TryParse(args[1], out tcpListeningPort)))
                {
                    return "\nInvalid argument list. First-run usage:\ndemo --port [TCP_PORT_NUMBER]\n\nPort numbers between 49152 and 65536 are recommended.\n";
                }

                return $"Received command to start listening on TCP port {tcpListeningPort}";
            }

            if (args.Length == 1 && args[0].Equals("--quit", StringComparison.OrdinalIgnoreCase))
            {
                Output("Running instance received the \"--quit\" switch, will exit in 1000ms");
                ctsRunningInstance?.CancelAfter(1000);
                return "Server quitting in 1000ms";
            }

            if (args.Length == 1 && args[0].Equals("--date", StringComparison.OrdinalIgnoreCase))
            {
                Output("Running instance received the \"--date\" switch");
                return DateTime.Now.Date.ToString("ddd MM-dd-yyyy");
            }

            if (args.Length == 1 && args[0].Equals("--time", StringComparison.OrdinalIgnoreCase))
            {
                Output("Running instance received the \"--time\" switch");
                return DateTime.Now.ToString("h:mm:ss tt");
            }

            Output("Invalid argument list.");
            return "Invalid argument list, the listening server only accepts a --date, --time, or --quit switch.";
        }

        private static void Output(string message)
        {
            var linefeed = message.StartsWith('\n') ? "\n" : string.Empty;
            message = message.StartsWith('\n') ? message.Substring(1) : message;
            var msg = $"{linefeed}[demo {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff}] {message}";
            Console.WriteLine(msg);
        }
    }
}
