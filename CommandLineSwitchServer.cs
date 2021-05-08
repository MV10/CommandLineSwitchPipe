using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CommandLineSwitchPipe
{
    /// <summary>
    /// Manages a named-pipe connection to send command-line switches to a running instance, or
    /// if this is the only instance, to receive command-line switches after processing starts.
    /// </summary>
    public static class CommandLineSwitchServer
    {
        /// <summary>
        /// The defaults are appropriate for most needs, but can also be created or modified through code,
        /// or populated from any of the standard .NET configuration extension packages.
        /// </summary>
        public static CommandLineSwitchServerOptions Options { get; set; } = new CommandLineSwitchServerOptions();

        /// <summary>
        /// Attempt to send any command-line switches to an already-running instance. If another
        /// instance is found but this instance was started without switches, the application will
        /// terminate. Leave the argument list null unless Program.Main needs to pre-process that
        /// data. When null, the command line will be retrieved from the system environment.
        /// </summary>
        public static async Task<bool> TrySendArgs(string[] args = null)
        {
            if (Options == null || Options.Advanced == null) 
                ThrowOutput(new ArgumentNullException($"{nameof(CommandLineSwitchServer)}.{nameof(Options)} property must be configured before invoking {nameof(TrySendArgs)}"));

            // Use the provided arguments, or environment array 1+ (array 0 is the program name and/or pathname)
            var arguments = args ?? Environment.GetCommandLineArgs();
            if(args == null)
                arguments = arguments.Length == 1 ? new string[0] : arguments[1..];

            Output($"Switch list has {arguments.Length} elements, checking for a running instance on pipe \"{PipeName()}\"");

            // Is another instance already running?
            using (var client = new NamedPipeClientStream(PipeName()))
            {
                try
                {
                    client.Connect(Options.Advanced.PipeConnectionTimeout);
                }
                catch (TimeoutException)
                {
                    Output($"No running instance found");
                    return false;
                }

                Output($"Connected to switch pipe server");

                // Connected, abort if we don't have arguments to pass
                if (arguments.Length == 0)
                {
                    string err = "No arguments were provided to pass to the already-running instance";

                    if (Options.Advanced.ThrowIfRunning)
                        ThrowOutput(new Exception(err));

                    Output(LogLevel.Error, err);
                    Environment.Exit(-1);
                }

                Output("Sending switches to running instance");

                // Send argument list with control-code separators
                var message = string.Empty;
                foreach (var arg in arguments) message += arg + Options.Advanced.SeparatorControlCode;
                var messageBuffer = Encoding.ASCII.GetBytes(message);
                var sizeBuffer = BitConverter.GetBytes(messageBuffer.Length);
                await client.WriteAsync(sizeBuffer, 0, sizeBuffer.Length);
                await client.WriteAsync(messageBuffer, 0, messageBuffer.Length);
                client.WaitForPipeDrain();
            }

            Output("Switches sent, this instance can terminate normally");
            return true;
        }

        /// <summary>
        /// Creates a named-pipe server that waits to receive command-line switches from
        /// another running instance. These are handed off as they're received.
        /// </summary>
        public static async Task StartServer(Action<string[]> switchHandler, CancellationToken cancellationToken)
        {
            if (Options == null || Options.Advanced == null)
                ThrowOutput(new ArgumentNullException($"{nameof(CommandLineSwitchServer)}.{nameof(Options)} property must be configured before invoking {nameof(StartServer)}"));

            if (switchHandler == null)
                ThrowOutput(new ArgumentNullException(paramName: nameof(switchHandler)));

            if (cancellationToken == null)
                ThrowOutput(new ArgumentNullException(paramName: nameof(cancellationToken)));

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    using (var server = new NamedPipeServerStream(PipeName()))
                    {
                        Output($"Switch pipe server waiting for connection on pipe \"{PipeName()}\"");
                        await server.WaitForConnectionAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        Output("Switch pipe client has connected to server");
                        using (var reader = new BinaryReader(server))
                        {
                            int size = 0;
                            byte[] buffer = null;
                            try
                            {
                                // Read the length of the message, then the message itself
                                size = reader.ReadInt32();
                                buffer = reader.ReadBytes(size);
                                Output($"Switch pipe client sending {size} bytes");
                            }
                            catch (Exception ex)
                            {
                                Output(LogLevel.Warning, $"Trapped exception {ex.GetType().Name} reading from client");
                            }
                            finally
                            {
                                // Goodbye, client
                                server.Disconnect();
                                Output("Switch pipe server terminated client connection");
                            }

                            if(size > 0)
                            {
                                // Split into original arg array and send for processing
                                var message = Encoding.ASCII.GetString(buffer);
                                var args = message.Split(Options.Advanced.SeparatorControlCode, StringSplitOptions.RemoveEmptyEntries);

                                // Process the switches, but prevent any handler exceptions from bringing down the server
                                Output($"Invoking switch handler for {args.Length} switches");
                                try
                                {
                                    switchHandler.Invoke(args);
                                }
                                catch (Exception ex)
                                {
                                    Output(LogLevel.Warning, $"Trapped exception {ex.GetType().Name} from switch handler");
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            { } // normal, disregard
            catch (Exception ex)
            {
                Output(LogLevel.Error, $"Exception {ex.GetType().Name}: {ex.Message}");
                if(ex.InnerException != null) Output(LogLevel.Error, $"Inner exception {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                Output(LogLevel.Error, ex.StackTrace);

                if(!cancellationToken.IsCancellationRequested && Options.Advanced.AutoRestartServer)
                {
                    Output(LogLevel.Warning, "Restarting server process");
                    _ = Task.Run(() => StartServer(switchHandler, cancellationToken));
                }
                else
                {
                    Output(LogLevel.Critical, "Forcibly terminating process");
                    Environment.Exit(-1);
                }
            }
            finally
            {
                Output("Switch pipe server has stopped listening");
            }
        }

        private static string PipeName()
            => string.IsNullOrWhiteSpace(Options.PipeName) ? Environment.GetCommandLineArgs()[0] : Options.PipeName;

        private static void Output(string message)
        {
            Output(Options.Advanced.MessageLogLevel, message);
        }

        private static void Output(LogLevel level, string message)
        {
            if (Options.LogToConsole) 
                Console.WriteLine(message);

            if (Options.Logger != null)
                Options.Logger.Log(level, message);
        }

        private static void ThrowOutput(Exception ex)
        {
            Output(LogLevel.Error, ex.Message);
            throw ex;
        }
    }
}
