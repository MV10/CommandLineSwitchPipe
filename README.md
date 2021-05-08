# CommandLineSwitchPipe

This library makes it easy for a running service (a .NET console program) to receive new command-line switches and arguments by simply running another instance of the same program with the additional arguments on the command-line.

At startup, the program tries to find another instance that was started earlier by attempting to connect to the named pipe server. If the server is found, any command-line arguments are sent to the existing instance, and the new instance should then terminate.

If another instance is not found, that instance will set up a named pipe server to receive new switches and arguments in the future, and that instance becomes the running service.

This can be useful for communicating with always-running background services. I recently had a need to run a Kestrel-based WebSocket server this way. This approach works as either a [Windows service](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-3.1&tabs=visual-studio) or as a [Linux systemd service](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-3.1#create-the-service-file).

## Usage

The library serves as a communications conduit only. It does not actually handle command-line parsing, which can be surprisingly complicated. It also doesn't address the problem of how to respond to those new switches. You must provide an `Action<string[]>` switch-handler delegate to process any arguments received from another instance.

The demo program should be relatively easy to follow, and the code is heavily commented, but the general steps are:

* Call `TrySendArgs` to send the command-line to any already-running instance.
* If this succeeds, exit, another instance was found and received the data.
* If this fails, this instance will become the running service:
  * Use `Task.Run` to invoke `StartServer` on a separate thread.
  * Process any arguments that were passed this instance.
  * Do whatever work the application should normally perform.
  * When the application should exit, cancel the token provided to `StartServer`.

## Options

For most applications, the default options are probably adequate. The console's `Main` method can programmatically set properties on the `CommandLineSwitchServer.Options` property, or they can be populated by the .NET configuration extension packages.

#### `PipeName`
Determines the name of the pipe used to communicate command-line arguments. It is null by default, which tells the library to use the application name (or pathname, this is OS dependent).

#### `Logger`
When set to an `ILogger` object, all types of messages are written to the provided logging system. It is null by default, which suppresses log output.

#### `LogToConsole`
When set to true, activity and warning messages will be written to the console (stdout). It is false by default, since these messages are typically uninteresting to day-to-day utility users. Errors and critical (fatal) messages are always written to the console.

#### `Advanced.ThrowIfRunning`
Typically, when the application is started without any command-line arguments, that instance will be the first one to start, and it just means the application's default settings should be used. If the application is started this way and there is already another instance running (it is able to connect to the other instance's named pipe server), the new instance will terminate. This determines whether the new instance simply exits, or if it throws an exception. The default is true, an exception is thrown.

#### `Advanced.PipeConnectionTimeout`
The number of milliseconds to wait for a named pipe server to respond. This defaults to 100ms, but realistically even 10ms is typically adequate.

#### `Advanced.SeparatorControlCode`
Defines the character used to separate the command-line arguments when they are sent to the already-running instance. Defaults to character 14 (0x0E), which is the Record Separator (RS) control code.

#### `Advanced.AutoRestartServer`
This allows the named pipe server to auto-restart if a fatal exception is encountered. This defaults to false, since the server has relatively robust exception handling and should gracefully recover from routine communications issues like a problem on the client side or an exception that bubbles up from the switch-handler delegate.

#### `Advanced.MessageLogLevel`
Defines the default `LogLevel` for routine activity messages. Defaults to `Debug` since these messages are unlikely to be interesting to typical utility end-users. Other options are `Information` or `Trace`. You should not set this to `Warning`, `Error` or `Critical`.
