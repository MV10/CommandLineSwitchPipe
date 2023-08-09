# CommandLineSwitchPipe [![NuGet](https://img.shields.io/nuget/v/CommandLineSwitchPipe.svg)](https://nuget.org/packages/CommandLineSwitchPipe)

This library uses named pipes to pass command-line switches and arguments to a running service (implemented as a .NET console program) by simply running another instance of the same program with the additional arguments on the command-line. The running instance can return a string value in response.

At startup, the program tries to find another instance that was started earlier by attempting to connect to the named pipe server. If the server is found, any command-line arguments are sent to the existing instance, and the new instance should then terminate.

If another instance is not found, that instance can become the running service by creating a named pipe server to listen for new command-line arguments to process.

Although the included demo should be run using two console windows, this is useful in the real world for communicating with always-running background services. I recently had a need to run a Kestrel-based WebSocket server this way. This approach works as either a [Windows service](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-3.1&tabs=visual-studio) or as a [Linux systemd service](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-3.1#create-the-service-file).

## Network Support

I was asked to add support for remote named pipes, but that's a Windows-only feature, and I want the library to remain cross-platform. (Named pipes originated on UNIX, and there it is specifically defined as local-only IPC which is closely tied to the file system.)

Instead, version 1.1.0 adds a simple TCP-listener feature in the form of an `UnsecuredPort` option for the server (in the "advanced" group of settings), and optional `server` and `port` arguments to the `TryConnect` and `TrySend` methods. As the option name indicates, this is _**not**_ secure -- do not use this where the port might be visible to a public network. It tries to accept _anything_ that is sent to the port.

Although this is somewhat re-inventing the wheel for Windows, this means it will seamlessly work exactly the same way for Linux -- or between Windows and Linux client/server combinations. Note that Windows will probably prompt you to add a firewall rule to allow the program to listen for TCP traffic.

The solution also contains a simple command-line utility in the `tcpargs` project, which can be used to remotely send an argument list to a given endpoint. This uses `TryConnect` and `TrySend` as a stand-alone client (in other words, it doesn't "take over" and start running if `TrySend` fails to locate an existing instance). The syntax is:

* `tcpargs [server|localhost] [port] [arg1] [arg2] ... [argN]`

Since the library is meant for very basic string exchanges, it does not adjust the default 8K send/receive buffer sizes. Only single-buffer read/write operations are supported, so any data larger than 8K (including minor overhead for separators and a data-length header) will cause an exception.

## Usage and Demo

The library serves as a communications conduit only. It does not actually handle command-line parsing, which can be surprisingly complicated. It also doesn't address the problem of how to respond to those new switches in the running program. You must provide a switch-handler delegate (accepting a string array and returning a string) to process any arguments received from another instance.

The demo program should be relatively easy to follow (and the new TCP client is even less complicated), and the code is heavily commented, but the general steps are:

* Call `TryConnect` to discover if another instance is already running
* Call `TrySendArgs` to send arguments to any already-running instance
* If this succeeds, another instance was found and received the data:
  * Optionally read the `QueryResponse` property
  * Exit
* If this fails, this instance can assume the role of the running service:
  * Use `Task.Run` to invoke `StartServer` on a separate thread
  * Process any command-line arguments used to start this instance
  * Do whatever work the application should normally perform
  * When the application should exit, cancel the token provided to `StartServer`

Just execute `demo` to start the server in local named-pipes mode. Once the demo program is running, open a second console window and run it again with any of these switches:

* `-quit` will terminate the running service
* `-date` will return the date portion of the system clock
* `-time` will return the time portion of the system clock

To start the server listening on a port, execute `demo -port [PORT]` where the port number is 49152 to 65536 (the custom/dynamic port range), such as:

* `demo -port 50001`

However, the demo does not have a way to send the switches over the network, as that would significantly complicate the code (which makes it hard to understand, as a demo). Instead, use the `tcpargs` utility described in the _Network Support_ section above. This utility is also a good example of how you'd build a simple remote control app that communicates via switches.

## Options Property

For most applications, the default options are probably adequate. The console's `Main` method can programmatically set properties on the `CommandLineSwitchServer.Options` property, or they can be populated by the .NET configuration extension packages.

#### `PipeName`
Determines the name of the pipe used to communicate command-line arguments. It is null by default, which tells the library to use the application name (or pathname, this is OS dependent).

#### `Logger`
When set to an `ILogger` object, all types of messages are written to the provided logging system. It is null by default, which suppresses log output.

#### `LogToConsole`
When set to true, activity and warning messages will be written to the console (stdout). It is false by default, since these messages are typically uninteresting to day-to-day utility users. Errors and critical (fatal) messages are always written to the console.

#### `Advanced.UnsecuredPort`
Zero by default, which disables the feature. If provided, the server will listen on the indicated TCP port. Anything received will be sent to the switch-handler delegate, and any response is sent back to the client.

#### `Advanced.ThrowIfRunning`
Typically, when the application is started without any command-line arguments, that instance will be the first one to start, and it just means the application's default settings should be used. If the application is started this way and there is already another instance running (it is able to connect to the other instance's named pipe server), the new instance will terminate. This determines whether the new instance simply exits, or if it throws an exception. The default is true, an exception is thrown.

#### `Advanced.PipeConnectionTimeout`
The number of milliseconds to wait for a named pipe server to respond. This defaults to 100ms, but realistically even 10ms is typically adequate.

#### `Advanced.SeparatorControlCode`
Defines the character used to separate the command-line arguments when they are sent to the already-running instance. Defaults to character 14 (0x0E), which is the Record Separator (RS) control code.

#### `Advanced.AutoRestartServer`
This allows the named pipe server to auto-restart if a fatal exception is encountered. This defaults to false, since the server has relatively robust exception handling and should gracefully recover from routine communications issues like a problem on the client side or an exception that bubbles up from the switch-handler delegate.

#### `Advanced.MessageLogLevel`
Defines the default `LogLevel` for routine activity messages. Defaults to `Debug` since these messages are unlikely to be interesting to typical utility end-users. Other options are `Information` or `Trace`. You should not set this to `Warning`, `Error` or `Critical`. Actual errors in the library will automatically be written with those elevated `LogLevel` flags, regardless of how this property is configured.

#### `Advanced.LinuxWaitAfterWriteMS`
On Windows, writing to a pipe is followed by the `WaitForPipeDrain` command, but this throws a "Platform Not Supported" exception on Linux. This provides a short asynchronous delay after a write operation (250ms by default) to allow the other end time to read the contents of the pipe stream.
