# CommandLineSwitchPipe

This library makes it easy for a running console program to receive new command-line switches and arguments by simply running another instance of the same program in a different console window. This is achieved through the use of named pipes.

At startup, the program tries to find another instance that was started earlier by attempting to connect to the named pipe server. If the server is found, any command-line arguments are sent to the existing instance, and the new instance should then terminate.

If another instance is not found, that instance will set up a named pipe server to receive new switches and arguments in the future.

This can be useful for communicating with always-running background services. I recently had a need to run a Kestrel-based WebSocket server this way. This approach works as either a [Windows service](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/windows-service?view=aspnetcore-3.1&tabs=visual-studio) or as a [Linux systemd service](https://docs.microsoft.com/en-us/aspnet/core/host-and-deploy/linux-nginx?view=aspnetcore-3.1#create-the-service-file).

## Usage

The library serves as a communications conduit only. It does not actually handle command-line parsing, which can be surprisingly complicated. You must provide an `Action<string[]>` delegate to process any arguments received from another instance.

The demo program should be relatively easy to follow, but the general steps are:

* Use the default configuration, or set the properties of the `Options` object.
* Call `TrySendArgs` to send the command-line to any already-running instance.
* If `TrySendArgs` returns true, exit -- another instance was found and received the data.
* If `TrySendArgs` returns false, this instance will become the persistent, running instance.
* Use `Task.Run` to invoke `StartServer` on a separate thread.
* Process any arguments that were passed this instance.
* Do whatever work the application should normally perform.
* When the application should exit, cancel the token provided to `StartServer`.


