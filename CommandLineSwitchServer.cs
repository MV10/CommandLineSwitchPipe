using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
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
        /// Populated when TrySendArgs or TryConnect results in an exception.
        /// </summary>
        public static Exception TryException = null;

        /// <summary>
        /// Returns true if another instance is already running.
        /// </summary>
        public static async Task<bool> TryConnect(string server = null, int port = 0)
        {
            Output($"{nameof(TryConnect)} starting for PID {Environment.ProcessId}");
            TryException = null;
            try
            {
                ValidateNetworkArgs(server, port);

                return (string.IsNullOrWhiteSpace(server))
                    ? await TryConnectLocalNamedPipe().ConfigureAwait(false)
                    : await TryConnectNetworkPort(server, port).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                OutputException(ex);
                TryException = ex;
                return false;
            }
        }

        /// <summary>
        /// Attempt to send any command-line switches to an already-running instance. If another
        /// instance is found but this instance was started without switches, the application will
        /// terminate. Leave the argument list null unless Program.Main needs to pre-process that
        /// data. When null, the command line will be retrieved from the system environment.
        /// </summary>
        public static async Task<bool> TrySendArgs(string[] args = null, string server = null, int port = 0)
        {
            Output($"{nameof(TrySendArgs)} starting for PID {Environment.ProcessId}");
            TryException = null;
            try
            {
                if (Options == null || Options.Advanced == null)
                    throw new ArgumentNullException($"{nameof(CommandLineSwitchServer)}.{nameof(Options)} property must be configured before invoking {nameof(TrySendArgs)}");

                ValidateNetworkArgs(server, port);

                QueryResponse = string.Empty;

                // Use the provided arguments, or environment array 1+ (array 0 is the program name and/or pathname)
                var arguments = args ?? Environment.GetCommandLineArgs();
                if (args == null)
                    arguments = arguments.Length == 1 ? new string[0] : arguments[1..];

                Output($"Switch list has {arguments.Length} elements");

                // Send argument list with control-code separators
                var message = string.Empty;
                foreach (var arg in arguments) message += arg + Options.Advanced.SeparatorControlCode;

                return (string.IsNullOrWhiteSpace(server))
                    ? await TrySendLocalNamedPipe(message).ConfigureAwait(false)
                    : await TrySendNetworkPort(message, server, port).ConfigureAwait(false);
            }
            catch(Exception ex)
            {
                OutputException(ex);
                TryException = ex;
                return false;
            }
        }

        /// <summary>
        /// Creates a named-pipe server that waits to receive command-line switches from another
        /// running instance. These are sent to the switchHandler delegate as they're received.
        /// </summary>
        public static async Task StartServer(Func<string[], string> switchHandler, CancellationToken cancellationToken, int port = 0)
        {
            CancellationTokenSource ctsTCPServer = new();

            try
            {
                if (Options == null || Options.Advanced == null)
                    throw new ArgumentNullException($"{nameof(CommandLineSwitchServer)}.{nameof(Options)} property must be configured before invoking {nameof(StartServer)}");

                if (switchHandler == null)
                    throw new ArgumentNullException(paramName: nameof(switchHandler));

                if (port != 0)
                {
                    ValidatePortArgs(port);
                    _ = Task.Run(() => StartTCPServer(switchHandler, ctsTCPServer.Token, port), ctsTCPServer.Token);
                }

                while (!cancellationToken.IsCancellationRequested)
                {
                    using var server = new NamedPipeServerStream(PipeName(), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

                    Output($"Switch server waiting for connection on namedpipe \"{PipeName()}\"");
                    await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                    Output(LogLevel.Trace, $"Switch server exited namedpipe {nameof(server.WaitForConnectionAsync)}");
                    cancellationToken.ThrowIfCancellationRequested();
                    OutputConsoleSeparator();
                    Output("Switch client has connected to namedpipe server");

                    var message = await ReadStringFromPipe(server);

                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        // Split into original arg array
                        var args = message.Split(Options.Advanced.SeparatorControlCode, StringSplitOptions.RemoveEmptyEntries);

                        // Process the switches, but prevent any handler exceptions from bringing down the server
                        Output($"Invoking switch handler for {args.Length} switches");
                        try
                        {
                            var response = switchHandler.Invoke(args) ?? string.Empty;
                            await WriteStringToPipe(server, response);
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
                                Output("Switch server terminated client namedpipe connection");
                            }
                            else
                            {
                                Output("Switch server connection was terminated by namedpipe client");
                            }
                        }
                        catch (Exception ex)
                        {
                            Output(LogLevel.Warning, $"{ex.GetType().Name} while trying to disconnect from namedpipe client");
                        }
                    }
                }

                ctsTCPServer.Cancel();
            }
            catch (OperationCanceledException)
            {
                Output(LogLevel.Trace, "Switch server namedpipe listener caught OperationCanceledException");
                ctsTCPServer.Cancel();
            }
            catch (Exception ex)
            {
                OutputException(ex);

                if(!cancellationToken.IsCancellationRequested && Options.Advanced.AutoRestartServer)
                {
                    ctsTCPServer.Cancel();
                    Output(LogLevel.Warning, "Restarting switch server task");
                    _ = Task.Run(() => StartServer(switchHandler, cancellationToken), cancellationToken);
                }
                else
                {
                    ctsTCPServer.Cancel();
                    Output(LogLevel.Critical, $"Switch server forcibly terminating process in {nameof(StartServer)}");
                    Environment.Exit(-1);
                }
            }
            finally
            {
                Output("Switch server has stopped listening on namedpipe");
            }
        }

        private static async Task StartTCPServer(Func<string[], string> switchHandler, CancellationToken cancellationToken, int port)
        {
            TcpListener server = null;
            try
            {
                server = new TcpListener(IPAddress.Any, port);
                server.Start(); // This merely queues connection requests

                while (!cancellationToken.IsCancellationRequested)
                {
                    Output($"Switch server waiting for connection on TCP port {port}");
                    using var client = await server.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                    Output(LogLevel.Trace, $"Switch server exited TCP {nameof(server.AcceptTcpClientAsync)}");
                    cancellationToken.ThrowIfCancellationRequested();
                    OutputConsoleSeparator();
                    Output("Switch client has connected to TCP port");

                    string activity = string.Empty;
                    try
                    {
                        activity = "reading data from TCP client";
                        var message = await ReadStringFromNetworkPort(client);

                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            // Split into original arg array
                            var args = message.Split(Options.Advanced.SeparatorControlCode, StringSplitOptions.RemoveEmptyEntries);

                            // Process the switches
                            Output($"Invoking switch handler for {args.Length} switches");
                            activity = "invoking switch handler";
                            var response = switchHandler.Invoke(args) ?? string.Empty;

                            // Can't rely on client.Connected since TCP is stateless
                            activity = "sending response to TCP client";
                            await WriteStringToNetworkPort(client, response).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        Output(LogLevel.Warning, $"{ex.GetType().Name} trapped {activity}");
                    }
                    finally
                    {
                        // Close is handled by Dispose
                        client.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Output(LogLevel.Trace, "Switch server TCP listener caught OperationCanceledException");
            }
            catch (Exception ex)
            {
                OutputException(ex);

                if (!cancellationToken.IsCancellationRequested && Options.Advanced.AutoRestartServer)
                {
                    Output(LogLevel.Warning, "Restarting switch server TCP task");
                    _ = Task.Run(() => StartTCPServer(switchHandler, cancellationToken, port), cancellationToken);
                }
                else
                {
                    Output(LogLevel.Critical, $"Switch server forcibly terminating process in {nameof(StartTCPServer)}");
                    Environment.Exit(-1);
                }
            }
            finally
            {
                server?.Stop();
                Output("Switch server has stopped listening on TCP");
            }
        }

        private static async Task<bool> TryConnectLocalNamedPipe()
        {
            Output(LogLevel.Trace, $"{nameof(TryConnectLocalNamedPipe)} invoked");

            using var client = new NamedPipeClientStream(".", PipeName(), PipeDirection.InOut);

            try
            {
                await client.ConnectAsync(Options.Advanced.PipeConnectionTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Output($"{nameof(TryConnectLocalNamedPipe)}: No running instance found");
                return false;
            }

            Output($"{nameof(TryConnectLocalNamedPipe)}: Running instance found");
            return true;
        }

        private static async Task<bool> TryConnectNetworkPort(string server, int port)
        {
            Output(LogLevel.Trace, $"{nameof(TryConnectNetworkPort)}(server:{server}, port:{port}) invoked");

            var addresses = await Dns.GetHostAddressesAsync(server, (AddressFamily)Options.Advanced.DnsAddressFamily, CancellationToken.None);
            if (addresses.Length == 0) throw new ArgumentException("Could not resolve address for host name");

            using TcpClient client = new();
            try
            {
                await client.ConnectAsync(addresses, port).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                if(ex.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    Output($"{nameof(TryConnectNetworkPort)}: No running instance found");
                    return false;
                }
                throw;
            }

            Output($"{nameof(TryConnectNetworkPort)}: Running instance found");
            return client.Connected;
        }

        private static async Task<bool> TrySendLocalNamedPipe(string message)
        {
            Output(LogLevel.Trace, $"{nameof(TrySendLocalNamedPipe)} invoked");

            Output($"Checking for a running instance on pipe \"{PipeName()}\"");

            using var client = new NamedPipeClientStream(".", PipeName(), PipeDirection.InOut);

            try
            {
                await client.ConnectAsync(Options.Advanced.PipeConnectionTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Output($"No running instance found");
                return false;
            }

            Output($"Connected to switch pipe server");

            // Connected, abort if we don't have arguments to pass
            if (string.IsNullOrEmpty(message))
            {
                Output("No arguments to pass to the running instance.");

                if(Options.Advanced.ThrowIfRunning)
                    throw new ArgumentException("No arguments were provided to pass to the already-running instance");

                Output("Not configured to throw an exception; forcing an exit");
                Environment.Exit(-1);
            }

            Output("Sending switches to running instance");
            await WriteStringToPipe(client, message).ConfigureAwait(false);

            Output("Waiting for reply");
            QueryResponse = await ReadStringFromPipe(client).ConfigureAwait(false);
            Options.Logger?.Log(LogLevel.Debug, $"Received:\n{QueryResponse}");

            Output($"Switches sent, PID {Environment.ProcessId} can terminate normally");
            return true;
        }

        private static async Task<bool> TrySendNetworkPort(string message, string server, int port)
        {
            Output(LogLevel.Trace, $"{nameof(TrySendNetworkPort)} invoked");

            Output($"Checking for a running instance on server {server}:{port}");

            var addresses = await Dns.GetHostAddressesAsync(server, (AddressFamily)Options.Advanced.DnsAddressFamily, CancellationToken.None);
            if (addresses.Length == 0) throw new ArgumentException("Could not resolve address for host name");
            Output($"Resolved {addresses.Length} addresses for server {server}");

            using TcpClient client = new();
            try
            {
                await client.ConnectAsync(addresses, port).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                if (ex.SocketErrorCode == SocketError.ConnectionRefused)
                {
                    Output($"{nameof(TryConnectNetworkPort)}: No running instance found");
                    return false;
                }
                throw;
            }
            if (!client.Connected) throw new Exception("Failed to connect, but no framework exception was thrown");

            Output($"Connected to switch pipe server");

            // Connected, abort if we don't have arguments to pass
            if (string.IsNullOrEmpty(message))
            {
                Output("No arguments to pass to the running instance.");

                if (Options.Advanced.ThrowIfRunning)
                    throw new ArgumentException("No arguments were provided to pass to the already-running instance");

                Output("Not configured to throw an exception; forcing an exit");
                Environment.Exit(-1);
            }

            Output("Sending switches to running instance");
            await WriteStringToNetworkPort(client, message).ConfigureAwait(false);

            Output("Waiting for reply");
            QueryResponse = await ReadStringFromNetworkPort(client).ConfigureAwait(false);
            Options.Logger?.Log(LogLevel.Debug, $"Received:\n{QueryResponse}");

            Output($"Switches sent, PID {Environment.ProcessId} can terminate normally");
            return true;
        }

        private static async Task WriteStringToPipe(PipeStream stream, string message)
        {
            Output(LogLevel.Trace, $"{nameof(WriteStringToPipe)} invoked");

            if (message.Length == 0) return;

            try
            {
                var messageBuffer = Encoding.ASCII.GetBytes(message);
                Output($"Sending {messageBuffer.Length} bytes");

                var sizeBuffer = BitConverter.GetBytes(messageBuffer.Length);
                await stream.WriteAsync(sizeBuffer, 0, sizeBuffer.Length).ConfigureAwait(false);

                await stream.WriteAsync(messageBuffer, 0, messageBuffer.Length).ConfigureAwait(false);

                await stream.FlushAsync().ConfigureAwait(false);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    stream.WaitForPipeDrain();
                }
                else
                {
                    await Task.Delay(Options.Advanced.LinuxWaitAfterWriteMS).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Output(LogLevel.Warning, $"{ex.GetType().Name} while writing to PipeStream");
            }
        }

        private static async Task<string> ReadStringFromPipe(PipeStream stream)
        {
            Output(LogLevel.Trace, $"{nameof(ReadStringFromPipe)} invoked");

            string response = string.Empty;

            try
            {
                using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

                // Read the length of the message, then the message itself
                var size = reader.ReadInt32();
                Output($"Receiving {size} bytes");

                if (size > 0)
                {
                    var buffer = reader.ReadBytes(size);
                    response = Encoding.ASCII.GetString(buffer);
                }

                await stream.FlushAsync().ConfigureAwait(false);
            }
            catch(EndOfStreamException)
            { } // normal, disregard
            catch (Exception ex)
            {
                Output(LogLevel.Warning, $"{ex.GetType().Name} while reading from PipeStream");
            }

            return response;
        }

        private static async Task WriteStringToNetworkPort(TcpClient client, string message)
        {
            Output(LogLevel.Trace, $"{nameof(WriteStringToNetworkPort)} invoked");

            if (string.IsNullOrEmpty(message)) return;

            try
            {
                var stream = client.GetStream();

                var messageBuffer = Encoding.ASCII.GetBytes(message);
                var minBuff = Math.Min(client.SendBufferSize, client.ReceiveBufferSize);
                if (messageBuffer.Length > minBuff) throw new ArgumentException($"Message with delimiters exceeds {minBuff} byte buffer size");

                Output($"Sending {messageBuffer.Length} bytes");
                await stream.WriteAsync(messageBuffer, 0, messageBuffer.Length).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Output(LogLevel.Warning, $"{ex.GetType().Name} while writing to NetworkStream");
            }
        }

        private static async Task<string> ReadStringFromNetworkPort(TcpClient client)
        {
            Output(LogLevel.Trace, $"{nameof(ReadStringFromNetworkPort)} invoked");

            string response = string.Empty;

            try
            {
                var stream = client.GetStream();

                var maxBuff = Math.Max(client.SendBufferSize, client.ReceiveBufferSize);
                var messageBuffer = new byte[maxBuff];
                var bytes = await stream.ReadAsync(messageBuffer, 0, maxBuff).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                Output($"Received {bytes} bytes");

                response = Encoding.ASCII.GetString(messageBuffer, 0, bytes);
            }
            catch (EndOfStreamException)
            { } // normal, disregard
            catch (Exception ex)
            {
                Output(LogLevel.Warning, $"{ex.GetType().Name} while reading from NetworkStream");
            }

            return response;
        }

        private static string PipeName()
            => string.IsNullOrWhiteSpace(Options.PipeName) ? Environment.GetCommandLineArgs()[0] : Options.PipeName;

        private static void ValidateNetworkArgs(string server, int port)
        {
            // local named pipe
            if (string.IsNullOrWhiteSpace(server))
            {
                if (port != 0) throw new ArgumentException("Local servers do not use a TCP port assignment");
            }
            // remote server
            else
            {
                ValidatePortArgs(port);
            }
        }

        private static void ValidatePortArgs(int port)
        {
            if (port < 1 || port > 65536) throw new ArgumentException("Invalid TCP port number");
            if (port < 49151) Output(LogLevel.Warning, "TCP port numbers below 49152 may be reserved");
        }

        private static void Output(string message)
        {
            Output(LogLevel.Debug, message);
        }

        private static void Output(LogLevel level, string message)
        {
            var msg = $"[{nameof(CommandLineSwitchPipe)} {DateTime.Now:yyyy-MM-dd HH:mm:ss.ffff}]: {message}";

            if (Options.LogToConsole || level > LogLevel.Warning) 
                Console.WriteLine(msg);

            Options.Logger?.Log(level, msg);
        }

        private static void OutputException(Exception ex)
        {
            Output(LogLevel.Error, $"Exception {ex.GetType().Name}: {ex.Message}");
            Output(LogLevel.Error, ex.StackTrace);

            var inner = ex.InnerException;
            while(inner is not null)
            {
                Output(LogLevel.Error, $"Inner exception {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                inner = ex.InnerException;
            }
        }

        private static void OutputConsoleSeparator()
        {
            if (Options.LogToConsole) Console.WriteLine("\n".PadRight(70, '-'));
        }
    }
}
