
### Releases

#### v2.0.1 2025-12-11
* Supports both .NET 8 and .NET 10

#### v2.0.2 2026-07-21
* Address a .NET Linux bug which can delete files, even the program's own DLL (!)
* Guarantee named pipes do not have access restrictions (by design, see README)
* Filed .NET runtime bug reports ([131200](https://github.com/dotnet/runtime/issues/131200) and [131203](https://github.com/dotnet/runtime/issues/131203))

#### v2.2.0 2026-07-22
* Add `Options.Advanced.Encoding`, defaults to `Encoding.ASCII` (non-breaking change)
* Refactor internal design to a loop rather than spawning new threads on server restarts
* Change auto-restart to only apply if the initial attempt succeeds
* Add `Options.Advanced.ExitOnServerFailure` default true maintains pre-2.2.0 behavior, false re-throws
* Add `Options.Advanced.AutoRestartDelay` in milliseconds (default is 5000)
* Add `Options.Advanced.AutoRestartAttempts` (default is 3)

### TODO

*



### Terminal Paths
C:\Source\CommandLineSwitchPipe\demo\bin\Debug\net10.0
C:\Source\CommandLineSwitchPipe\tcpargs\bin\Debug\net10.0

/data/Source/CommandLineSwitchPipe/demo/bin/Debug/net10.0
/data/Source/CommandLineSwitchPipe/tcpargs/bin/Debug/net10.0
