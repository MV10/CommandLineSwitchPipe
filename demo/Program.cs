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
                Console.WriteLine("\ndemo: Forcing console logging for demo purposes.");
                CommandLineSwitchServer.Options.LogToConsole = true;

                // Test the connection
                if (await CommandLineSwitchServer.TryConnect())
                {
                    Console.WriteLine("\ndemo: Successfully connected to another instance.");
                }
                else
                {
                    Console.WriteLine("\ndemo: Did not connect to another instance. For demo-logging purposes, args will be sent anyway.");
                }

                // Try to send any command line switches to an already-running instance.
                // If this returns true, the switches were sent and this instance can exit.
                if (await CommandLineSwitchServer.TrySendArgs())
                {
                    Console.WriteLine($"\ndemo: QueryResponse after sending argument list: \"{CommandLineSwitchServer.QueryResponse}\"");
                    return;
                }

                // Another instance is not running. This instance will start listening for
                // new switches, and will process any switches provided to this instance.
                Console.WriteLine($"\ndemo: Results of processing startup switches:");
                Console.WriteLine(ProcessSwitches(args, argsReceivedFromPipe: false));

                ctsSwitchPipe = new CancellationTokenSource();
                _ = Task.Run(() => CommandLineSwitchServer.StartServer(ProcessSwitches, ctsSwitchPipe.Token, tcpListeningPort));

                // Loop until somebody sends a -quit switch.
                Console.WriteLine($"\n\ndemo: Application is running.\n* -quit\tTerminates the running instance\n* -date\tReturns the current date\n* -time\tReturns the current time\n\n");
                ctsRunningInstance = new CancellationTokenSource();
                while (!ctsRunningInstance.IsCancellationRequested)
                {
                    await Task.Delay(1000, ctsRunningInstance.Token);
                    Console.WriteLine($"{DateTime.Now}                  ");
                    Console.SetCursorPosition(0, Console.CursorTop - 1);
                };
            }
            catch (OperationCanceledException)
            { } // normal, disregard
            catch (Exception ex)
            {
                Console.WriteLine($"\nException of type {ex.GetType().Name}\n{ex.Message}");
                if (ex.InnerException != null) Console.Write(ex.InnerException.Message);
                Console.WriteLine($"\n{ex.StackTrace}");
            }
            finally
            {
                // Stephen Cleary says disposal is unnecessary as long as the token is cancelled
                ctsSwitchPipe?.Cancel();
                ctsRunningInstance?.Cancel(); // in case of exception...
            }

            Console.Write("demo: Application is exiting.");
        }

        private static string ProcessSwitches(string[] args)
        {
            Console.WriteLine($"Processing {args.Length} arguments from non-server instance");
            return ProcessSwitches(args, argsReceivedFromPipe: true);
        }

        private static string ProcessSwitches(string[] args, bool argsReceivedFromPipe)
        {
            if (args.Length == 0)
                return string.Empty;

            if (!argsReceivedFromPipe)
            {
                Console.WriteLine($"Processing {args.Length} arguments directly from the console");

                if(args.Length != 2 || !(args[0].Equals("-port", StringComparison.InvariantCultureIgnoreCase) && int.TryParse(args[1], out tcpListeningPort)))
                {
                    return "\nInvalid argument list. First-run usage:\ndemo -port [TCP_PORT_NUMBER]\n\nPort numbers between 49152 and 65536 are recommended.\n";
                }

                return $"Received command to start listening on TCP port {tcpListeningPort}";
            }

            if (args.Length > 1)
            {
                Console.WriteLine("Invalid argument list.");
                return string.Empty;
            }

            if (args[0].Equals("-quit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Running instance received the \"-quit\" switch");
                ctsRunningInstance?.CancelAfter(1000);
                return "OK";
            }

            if (args[0].Equals("-date", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Running instance received the \"-date\" switch");
                return DateTime.Now.Date.ToString();
            }

            if (args[0].Equals("-time", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Running instance received the \"-time\" switch");
                return DateTime.Now.TimeOfDay.ToString();
            }

            Console.WriteLine("Invalid argument.");
            return string.Empty;
        }
    }
}
