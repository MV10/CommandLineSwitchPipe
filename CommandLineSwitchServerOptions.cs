using Microsoft.Extensions.Logging;

namespace CommandLineSwitchPipe
{
    public class CommandLineSwitchServerOptions
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
        /// Typically, running an application with no switches is used to start the application with
        /// default settings. In that case, if an instance is already running, the new instance should
        /// exit. This is true by default, which generates an exception. If false, no exception is thrown,
        /// but there will be console and/or log output, depending on the configuration.
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
        /// When a logger is provided, this determines the level of messages logged during normal operation. Options are
        /// Information, Debug, or Trace (aka Verbose in most logger systems). This setting is ignored for events logged
        /// as Warning, Error, or Critical (aka Fatal) level messages.
        /// </summary>
        public LogLevel MessageLogLevel { get; set; } = LogLevel.Debug;
    }
}
