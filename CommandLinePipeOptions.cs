using Microsoft.Extensions.Logging;

namespace CommandLineSwitchPipe
{
    public class CommandLinePipeOptions
    {
        /// <summary>
        /// The pipe name used to communicate command line switches. If this is not provided,
        /// the application name (and possibly the path, depending on OS) will be used.
        /// </summary>
        public string PipeName { get; set; } = null;

        /// <summary>
        /// Optional log writer.
        /// </summary>
        public ILogger Logger { get; set; } = null;

        /// <summary>
        /// Messages are written to stdout when true. Default is false.
        /// </summary>
        public bool LogToConsole { get; set; } = false;

        /// <summary>
        /// Settings for which the defaults are normally adequate.
        /// </summary>
        public AdvancedOptions Advanced { get; set; } = new AdvancedOptions();
    }

    public class AdvancedOptions
    {
        /// <summary>
        /// When populated, the server will listen on this TCP port and relay anything received
        /// to the switch handler delegage, and send back any response. As the name indicates,
        /// there is no security associated with this. Do not expose it to the Internet. The
        /// default value of 0 disables this feature.
        /// </summary>
        public int UnsecuredPort { get; set; } = 0;

        /// <summary>
        /// Default is 0, which is Unspecified, which in practice returns IPv4 and IPv6. On Windows
        /// this can be slow if IPv6 is not used but DNS returns an IPv6 address anyway (particularly
        /// for localhost). Currently .NET checks the IPs sequentially which can turn a 100ms localhost
        /// request into a 2000+ms request. The setting corresponds to the .NET AddressFamily enum.
        /// Specify 2 for IPv4 only, or 23 for IPv6 only.
        /// </summary>
        public int DnsAddressFamily { get; set; } = 0;

        // Related issues, the above supposedly to be fixed in .NET9 by polling all addresses
        // simultaneously (but not holding my breath, this problem goes back to .NET Core 2.1)
        // https://github.com/dotnet/runtime/issues/87932
        // https://github.com/dotnet/runtime/issues/26177
        // https://github.com/dotnet/runtime/issues/31085

        /// <summary>
        /// Typically, running an application with no switches is used to start the application with
        /// default settings. In that case, if an instance is already running, the new instance should
        /// exit. This is true by default, which generates a System.ArgumentException. If false, no
        /// exception is thrown, but there will be console and/or log output, depending on the configuration.
        /// If no exception is thrown, the library will forcibly end the process.
        /// </summary>
        public bool ThrowIfRunning { get; set; } = true;

        /// <summary>
        /// Milliseconds to wait for the attempt to connect to a running instance. Defaults to 100ms.
        /// </summary>
        public int PipeConnectionTimeout { get; set; } = 100;

        /// <summary>
        /// Can be specified in code, if necessary. Defaults to the RS (record separator) control code, 0x0E or ASCII 14.
        /// </summary>
        public string SeparatorControlCode { get; set; } = "\u0014";

        /// <summary>
        /// If the server thread encounters a fatal exception, it can auto-restart if this is true. Default is false,
        /// an exception will be logged and the process will be forcibly terminated.
        /// </summary>
        public bool AutoRestartServer { get; set; } = false;

        /// <summary>
        /// Linux does not support WaitForPipeDrain on writes, so a short delay can be
        /// applied to give the other end time to read the contents. Default is 250ms.
        /// </summary>
        public int LinuxWaitAfterWriteMS { get; set; } = 250;
    }
}
