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

        static async Task Main(string[] args)
        {
            try
            {
                // For demo purposes, we want console output.
                CommandLineSwitchServer.Options.LogToConsole = true;

                // Try to send any command line switches to an already-running instance.
                // If this returns true, the switches were sent and this instance can exit.
                if (await CommandLineSwitchServer.TrySendArgs())
                    return;

                // Another instance is not running. This instance will start listening for
                // new switches, and will process any switches provided to this instance.
                ctsSwitchPipe = new CancellationTokenSource();
                _ = Task.Run(() => CommandLineSwitchServer.StartServer(ProcessSwitches, ctsSwitchPipe.Token));

                // Process any switches from this instance's command line
                ProcessSwitches(args, argsReceivedFromPipe: false);

                // Loop until somebody sends a -quit switch.
                Console.WriteLine($"\nApplication is running. Run with the \"-quit\" switch to terminate this instance.");
                ctsRunningInstance = new CancellationTokenSource();
                while (!ctsRunningInstance.IsCancellationRequested)
                {
                    await Task.Delay(1000, ctsRunningInstance.Token);
                    Console.WriteLine($"{DateTime.Now}                  ");
                    Console.SetCursorPosition(0, Console.CursorTop - 1);
                };

                Console.Write("Application is exiting.");
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
        }

        private static void ProcessSwitches(string[] args)
        {
            Console.WriteLine($"Processing {args.Length} arguments from client instance");
            ProcessSwitches(args, argsReceivedFromPipe: true);
        }

        private static void ProcessSwitches(string[] args, bool argsReceivedFromPipe)
        {
            if (args.Length == 0)
                return;

            if (!argsReceivedFromPipe)
                Console.WriteLine($"Processing {args.Length} arguments directly from the console");

            if (args.Length > 1 || !args[0].Equals("-quit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("Arguments ignored. This demo only accepts a \"-quit\" switch.");
                return;
            }

            if (!argsReceivedFromPipe)
            {
                Console.WriteLine("The \"-quit\" argument is only valid when another copy is already running");
                return;
            }

            Console.WriteLine("Running instance received the \"-quit\" switch");
            ctsRunningInstance?.Cancel();
        }
    }
}
