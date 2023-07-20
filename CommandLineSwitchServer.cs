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
        public static CommandLinePipeOptions Options { get; set; } = new CommandLinePipeOptions();

        /// <summary>
        /// Populated when TrySendArgs is invoked with the waitForReply flag set to true.
        /// </summary>
        public static string QueryResponse = string.Empty;

        /// <summary>
        /// Returns true if another instance is already running.
        /// </summary>
        public static async Task<bool> TryConnect()
        {
            using (var client = new NamedPipeClientStream(".", PipeName(), PipeDirection.InOut))
            {
                try
                {
                    // UI clients interpret subsequent code as calls from a non-main thread???
                    //await client.ConnectAsync(Options.Advanced.PipeConnectionTimeout);
                    client.Connect(Options.Advanced.PipeConnectionTimeout);
                }
                catch (TimeoutException)
                {
                    Output($"{nameof(TryConnect)}: No running instance found");
                    return false;
                }
            }
            Output($"{nameof(TryConnect)}: Running instance found");
            return true;
        }

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

            QueryResponse = string.Empty;

            // Use the provided arguments, or environment array 1+ (array 0 is the program name and/or pathname)
            var arguments = args ?? Environment.GetCommandLineArgs();
            if(args == null)
                arguments = arguments.Length == 1 ? new string[0] : arguments[1..];

            Output($"Switch list has {arguments.Length} elements, checking for a running instance on pipe \"{PipeName()}\"");

            // Is another instance already running?
            using (var client = new NamedPipeClientStream(".", PipeName(), PipeDirection.InOut))
            {
                try
                {
                    // UI clients interpret subsequent code as calls from a non-main thread???
                    //await client.ConnectAsync(Options.Advanced.PipeConnectionTimeout);
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
                await WriteString(client, message);

                Output("Waiting for reply");
                QueryResponse = await ReadString(client);
            }

            Output("Switches sent, this instance can terminate normally");
            return true;
        }

        /// <summary>
        /// Creates a named-pipe server that waits to receive command-line switches from another
        /// running instance. These are sent to the switchHandler delegate as they're received.
        /// </summary>
        public static async Task StartServer(Func<string[], string> switchHandler, CancellationToken cancellationToken)
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
                    using (var server = new NamedPipeServerStream(PipeName(), PipeDirection.InOut))
                    {
                        Output($"Switch pipe server waiting for connection on pipe \"{PipeName()}\"");
                        await server.WaitForConnectionAsync(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        Output("Switch pipe client has connected to server");

                        var message = await ReadString(server);

                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            // Split into original arg array
                            var args = message.Split(Options.Advanced.SeparatorControlCode, StringSplitOptions.RemoveEmptyEntries);

                            // Process the switches, but prevent any handler exceptions from bringing down the server
                            Output($"Invoking switch handler for {args.Length} switches");
                            try
                            {
                                var response = switchHandler.Invoke(args) ?? string.Empty;
                                await WriteString(server, response);
                            }
                            catch (Exception ex)
                            {
                                Output(LogLevel.Warning, $"{ex.GetType().Name} trapped from switch handler");
                            }

                            try
                            {
                                // Goodbye, client
                                if(server.IsConnected)
                                {
                                    server.Disconnect();
                                    Output("Switch pipe server terminated client connection");
                                }
                                else
                                {
                                    Output("Switch pipe server connection was terminated by client");
                                }
                            }
                            catch (Exception ex)
                            {
                                Output(LogLevel.Warning, $"{ex.GetType().Name} while trying to disconnect from switch pipe client");
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

        private static async Task WriteString(PipeStream stream, string message)
        {
            try
            {
                var messageBuffer = Encoding.ASCII.GetBytes(message);
                Output($"Sending {messageBuffer.Length} bytes");

                var sizeBuffer = BitConverter.GetBytes(messageBuffer.Length);
                await stream.WriteAsync(sizeBuffer, 0, sizeBuffer.Length);

                if (message.Length > 0)
                    await stream.WriteAsync(messageBuffer, 0, messageBuffer.Length);

                stream.WaitForPipeDrain();
            }
            catch (Exception ex)
            {
                Output(LogLevel.Warning, $"{ex.GetType().Name} while writing stream");
            }
        }

        private static async Task<string> ReadString(PipeStream stream)
        {
            string response = string.Empty;

            try
            {
                using (var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true))
                {
                    // Read the length of the message, then the message itself
                    var size = reader.ReadInt32();
                    Output($"Receiving {size} bytes");

                    if (size > 0)
                    {
                        var buffer = reader.ReadBytes(size);
                        response = Encoding.ASCII.GetString(buffer);
                    }
                }
                await stream.FlushAsync();
            }
            catch (Exception ex)
            {
                Output(LogLevel.Warning, $"{ex.GetType().Name} while reading stream");
            }

            return response;
        }

        private static void Output(string message)
        {
            Output(Options.Advanced.MessageLogLevel, message);
        }

        private static void Output(LogLevel level, string message)
        {
            if (Options.LogToConsole || level > LogLevel.Warning) 
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
