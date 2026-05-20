# CodeWF.EventBus.Socket

English | [简体中文](README-zh_CN.md)

[![NuGet](https://img.shields.io/nuget/v/CodeWF.EventBus.Socket.svg)](https://www.nuget.org/packages/CodeWF.EventBus.Socket/)
[![NuGet](https://img.shields.io/nuget/dt/CodeWF.EventBus.Socket.svg)](https://www.nuget.org/packages/CodeWF.EventBus.Socket/)
[![License](https://img.shields.io/github/license/dotnet9/CodeWF.EventBus.Socket)](LICENSE)

`CodeWF.EventBus.Socket` is a lightweight TCP event bus for C# processes. It provides Publish/Subscribe messaging and Query-style request/response over sockets, so local processes or lightweight services can communicate without introducing RabbitMQ, Kafka, Redis, or other external MQ infrastructure.

![Command](docs/imgs/command.png)

![Query](docs/imgs/query.png)

## Highlights

- TCP socket transport based on `CodeWF.NetWrapper`
- Publish/Subscribe for cross-process notifications
- Query/response flow on the same subject
- Query correlation by request `TaskId`, including concurrent queries on the same subject
- Reuses `CodeWF.NetWrapper` transport objects such as `TcpSocketServer`, `TcpSocketClient`, `SocketCommand`, and `Heartbeat`
- No external MQ dependency
- Demo application in `src/EventBusDemo`

## Installation

```bash
dotnet add package CodeWF.EventBus.Socket
```

or

```powershell
Install-Package CodeWF.EventBus.Socket
```

## Quick Start

### Start the server

```csharp
using CodeWF.EventBus.Socket;

IEventServer eventServer = new EventServer();
await eventServer.StartAsync("127.0.0.1", 9100);
```

### Connect a client

```csharp
using CodeWF.EventBus.Socket;

IEventClient eventClient = new EventClient();
await eventClient.ConnectAsync("127.0.0.1", 9100);
```

### Subscribe and publish

```csharp
eventClient.Subscribe<NewEmailCommand>("event.email.new", command =>
{
    Console.WriteLine($"Received: {command.Subject}");
});

eventClient.Publish("event.email.new", new NewEmailCommand
{
    Subject = "Welcome",
    Content = "Your account is ready",
    SendTime = DateTime.Now
}, out _);
```

### Query and respond

```csharp
eventClient.Subscribe<EmailQuery>("event.email.query", query =>
{
    var response = new EmailQueryResponse
    {
        Emails = EmailManager.QueryEmail(query.Subject)
    };

    eventClient.Publish("event.email.query", response, out _);
});

var result = await eventClient.QueryAsync<EmailQuery, EmailQueryResponse>(
    "event.email.query",
    new EmailQuery { Subject = "Account" },
    3000);
```

## Documentation

- [Design document](docs/design.md)
- [中文设计文档](docs/design-zh_CN.md)
- [Demo application](src/EventBusDemo)

## Communication Protocol

Data packets are exchanged over `TCP`.

![Protocol Frame](docs/imgs/0.0.8@2x.png)

## Notes

- This library is designed for lightweight event distribution and IPC-style communication.
- The transport layer is implemented with `CodeWF.NetWrapper`, and same-name transport objects are reused directly instead of being duplicated locally.
- Event-bus-specific request/query/update packets remain defined in this project because they carry library-specific semantics.
- Messages are kept in memory only. There is no persistence, broker-side retry queue, or delivery guarantee after a process restart.
- For production use, consider whether you need authentication, encryption, retry, monitoring, and back-pressure around the socket transport.

## Third-Party Open Source Audit (2026-05-20)

Checked with `dotnet restore CodeWF.EventBus.Socket.slnx`, `dotnet list package --include-transitive`, NuGet `.nuspec` metadata, NuGet.org, and upstream source/license links. MIT / Apache-2.0 / BSD are preferred; other source-open licenses are noted when source and transitive dependencies are traceable.

Remediation:

- Migrated the solution from `CodeWF.EventBus.Socket.sln` to `CodeWF.EventBus.Socket.slnx`.
- Added `Directory.Packages.props` and centralized direct package versions.
- Upgraded the demo package line to `Avalonia 12.0.3`, `Semi.Avalonia 12.0.1`, `Irihi.Ursa 2.0.0`, `ReactiveUI.Avalonia 12.0.1`, `CodeWF.NetWrapper 2.1.1`, `CodeWF.EventBus 3.4.5.5`, and `CodeWF.LogViewer.Avalonia 12.0.3.1`.
- Removed `Avalonia.Diagnostics` because it does not have an Avalonia 12 package line.
- Kept `Prism.Avalonia` / `Prism.DryIoc.Avalonia` on MIT `8.1.97.11073`; the newer 9.x line is intentionally not used.
- Removed unused `Irihi.Ursa.PrismExtension 9.0.1` and pinned old transitive system packages to `10.0.8`.

| Package | Usage | License | Source | Status |
| --- | --- | --- | --- | --- |
| `CodeWF.EventBus` / `CodeWF.NetWrapper` / `CodeWF.NetWeaver` / `CodeWF.Log.Core` / `CodeWF.LogViewer.Avalonia` | Event bus, socket transport, demo logging | MIT | CodeWF repositories | Own open-source packages; approved |
| `Avalonia` / `Avalonia.Desktop` / `Avalonia.Markup.Xaml.Loader` | Demo UI | MIT | https://github.com/AvaloniaUI/Avalonia | Approved, `12.0.3` |
| `Semi.Avalonia` | Demo theme | MIT | https://github.com/irihitech/Semi.Avalonia | Approved, only the open core package is used |
| `Irihi.Ursa` / `Irihi.Ursa.Themes.Semi` | Demo controls and theme | MIT | https://github.com/irihitech/Ursa.Avalonia | Approved, `2.0.0` |
| `Prism.Avalonia` / `Prism.DryIoc.Avalonia` | Demo DI / Prism shell | MIT | https://github.com/AvaloniaCommunity/Prism.Avalonia | Approved, pinned to the 8.x open line |
| `ReactiveUI.Avalonia` | Demo MVVM | MIT | https://github.com/reactiveui/reactiveui | Approved |
| `System.Configuration.ConfigurationManager` / `System.Drawing.Common` / `System.Security.Cryptography.ProtectedData` / `System.Security.Permissions` / `System.Windows.Extensions` | Transitive compatibility pins | MIT | https://github.com/dotnet/dotnet | Approved, pinned to `10.0.8` |
| `Tmds.DBus.Protocol` | Avalonia Linux desktop transport | MIT | https://github.com/tmds/Tmds.DBus | Approved, pinned to `0.93.0` |
| `YY-Thunks` | Windows compatibility | MIT | https://github.com/Chuyu-Team/YY-Thunks | Source-open; approved |
| `Microsoft.NET.Test.Sdk` / `coverlet.collector` | Tests | MIT | https://github.com/microsoft/vstest / https://github.com/coverlet-coverage/coverlet | Approved |
| `xunit` / `xunit.runner.visualstudio` | Tests | Apache-2.0 | https://github.com/xunit/xunit | Approved |

Transitive dependency check: the effective dependency graph does not contain Prism 9 preview packages, or old `System.Drawing.Common 4.7.0` / `System.Configuration.ConfigurationManager 4.7.0` / `System.Security.Cryptography.ProtectedData 4.7.0` chains. `CodeWF.NetWrapper` is now resolved to `2.1.1`.
