# CodeWF.EventBus.Socket

[![NuGet](https://img.shields.io/nuget/v/CodeWF.EventBus.Socket.svg)](https://www.nuget.org/packages/CodeWF.EventBus.Socket/)
[![NuGet](https://img.shields.io/nuget/dt/CodeWF.EventBus.Socket.svg)](https://www.nuget.org/packages/CodeWF.EventBus.Socket/)
[![License](https://img.shields.io/github/license/dotnet9/CodeWF.EventBus.Socket)](LICENSE)

`CodeWF.EventBus.Socket` 是一个面向 C# 进程间通信的轻量级 TCP 事件总线库。它基于 Socket 提供发布订阅与 Query 式请求响应能力，让本地多进程或轻量服务之间可以通信，而不必先引入 RabbitMQ、Kafka、Redis 等外部 MQ 基础设施。

![Command](docs/imgs/command.png)

![Query](docs/imgs/query.png)

## 仓库规范

- 当前版本：`1.2.6`，版本号统一维护在根目录 `Directory.Build.props` 的 `<Version>` 节点。
- NuGet 包项目统一支持 `net8.0;net10.0`；Demo、App、测试与内部应用项目统一使用 `net11.0` / `net11.0-windows`。
- 根目录 `logo.svg`、`logo.png`、`logo.ico` 是唯一图标源，子工程只通过 MSBuild `Link` 引用，不维护图标副本。
- 运行时帮助、Markdown 示例、内置备忘录、设计说明等业务文档按功能保留；仓库级入口文档使用根目录 `README.md` 和 `UpdateLog.md`。

## 特性

- 基于 `CodeWF.NetWrapper` 的 TCP Socket 传输
- 支持 Publish/Subscribe 跨进程事件通知
- 支持同一主题下的 Query/Response 交互
- 查询请求按 `TaskId` 关联，支持同主题并发查询
- 直接复用 `CodeWF.NetWrapper` 的 `TcpSocketServer`、`TcpSocketClient`、`SocketCommand`、`Heartbeat`
- 不依赖第三方 MQ
- 自带示例工程 `src/EventBusDemo`

## 安装

```bash
dotnet add package CodeWF.EventBus.Socket
```

或

```powershell
Install-Package CodeWF.EventBus.Socket
```

## 快速开始

### 启动服务端

```csharp
using CodeWF.EventBus.Socket;

IEventServer eventServer = new EventServer();
await eventServer.StartAsync("127.0.0.1", 9100);
```

### 连接客户端

```csharp
using CodeWF.EventBus.Socket;

IEventClient eventClient = new EventClient();
await eventClient.ConnectAsync("127.0.0.1", 9100);
```

### 订阅并发布事件

```csharp
eventClient.Subscribe<NewEmailCommand>("event.email.new", command =>
{
    Console.WriteLine($"收到邮件主题：{command.Subject}");
});

eventClient.Publish("event.email.new", new NewEmailCommand
{
    Subject = "欢迎使用",
    Content = "您的账号已经准备完成",
    SendTime = DateTime.Now
}, out _);
```

### 查询与响应

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
    new EmailQuery { Subject = "账户" },
    3000);
```

## 文档

- [设计文档](docs/design.md)
- [示例工程](src/EventBusDemo)

## 脚本

- `pack.bat`：还原、构建并打包 `CodeWF.EventBus.Socket` 到 `artifacts\packages`。如果本机存在 `..\CodeWF.NetWeaver\artifacts\packages`，脚本会自动作为本地包源使用，方便在兄弟仓库尚未发布新包时完成本地打包验证。

## 通信协议

数据通过 `TCP` 进行传输。

![Protocol Frame](docs/imgs/0.0.8@2x.png)

## 说明

- 这个库适合轻量级事件分发和进程间通信场景。
- 底层传输由 `CodeWF.NetWrapper` 实现，同名传输对象直接复用包内定义，而不是在本项目重复维护。
- 事件总线层自己的请求、查询和推送协议对象仍保留在本项目中，因为它们承载的是本库特有的语义。
- 当前消息仅保存在内存中，不提供持久化、重试队列或进程重启后的投递保证。
- 如果用于生产环境，请按实际需要补充认证、加密、监控、限流与重试策略。

## 第三方开源组件审计（2026-05-20）

检查方式：`dotnet restore CodeWF.EventBus.Socket.slnx`、`dotnet list package --include-transitive`、NuGet `.nuspec`、NuGet.org 与源码仓库信息。优先接受 MIT / Apache-2.0 / BSD；其它开源协议在源码与传递依赖均可追溯时单独标注。

整改：

- 解决方案已从 `CodeWF.EventBus.Socket.sln` 迁移为 `CodeWF.EventBus.Socket.slnx`。
- 新增 `Directory.Packages.props`，直接依赖统一走中央包管理。
- `Prism.Avalonia` / `Prism.DryIoc.Avalonia` 从 9.x 降到 MIT 的 `8.1.97.11073`，并继续保留该开源线。
- 移除未使用的 `Irihi.Ursa.PrismExtension`。
- 示例依赖升级到 `Avalonia 12.0.3`、`Semi.Avalonia 12.0.1`、`Irihi.Ursa 2.0.0`、`ReactiveUI.Avalonia 12.0.1`、`CodeWF.NetWrapper 2.1.2.3`、`CodeWF.EventBus 3.4.5.5`、`CodeWF.LogViewer.Avalonia 12.0.3.1`。
- 移除 `Avalonia.Diagnostics`，该包目前没有 Avalonia 12 对应包线。
- 旧传递依赖 `System.Configuration.ConfigurationManager`、`System.Drawing.Common`、`System.Security.Cryptography.ProtectedData` 等已 pin 到 `10.0.8`。

| 包 | 使用范围 | 协议 | 源码/项目地址 | 结论 |
| --- | --- | --- | --- | --- |
| `CodeWF.EventBus` / `CodeWF.NetWrapper` / `CodeWF.NetWeaver` / `CodeWF.Log.Core` / `CodeWF.LogViewer.Avalonia` | 事件总线、TCP 传输与示例日志 | MIT | CodeWF 自研仓库 | 自研开源包，通过 |
| `Avalonia` / `Avalonia.Desktop` / `Avalonia.Markup.Xaml.Loader` | 示例 UI | MIT | https://github.com/AvaloniaUI/Avalonia | 通过，`12.0.3` |
| `Semi.Avalonia` | 示例主题 | MIT | https://github.com/irihitech/Semi.Avalonia | 通过，仅使用开源主体包 |
| `Irihi.Ursa` / `Irihi.Ursa.Themes.Semi` | 示例控件与主题 | MIT | https://github.com/irihitech/Ursa.Avalonia | 通过，`2.0.0` |
| `Prism.Avalonia` / `Prism.DryIoc.Avalonia` | 示例 DI / Prism shell | MIT | https://github.com/AvaloniaCommunity/Prism.Avalonia | 通过，固定到 8.x 开源线 |
| `ReactiveUI.Avalonia` | 示例 MVVM | MIT | https://github.com/reactiveui/reactiveui | 通过 |
| `System.Configuration.ConfigurationManager` / `System.Drawing.Common` / `System.Security.Cryptography.ProtectedData` / `System.Security.Permissions` / `System.Windows.Extensions` | 传递依赖兼容 pin | MIT | https://github.com/dotnet/dotnet | 通过，固定到 `10.0.8` |
| `Tmds.DBus.Protocol` | Avalonia Linux 桌面传输 | MIT | https://github.com/tmds/Tmds.DBus | 通过，固定到 `0.93.0` |
| `YY-Thunks` | Windows 兼容 | MIT | https://github.com/Chuyu-Team/YY-Thunks | 源码开放，通过 |
| `Microsoft.NET.Test.Sdk` / `coverlet.collector` | 测试 | MIT | https://github.com/microsoft/vstest / https://github.com/coverlet-coverage/coverlet | 通过 |
| `xunit` / `xunit.runner.visualstudio` | 测试 | Apache-2.0 | https://github.com/xunit/xunit | 通过 |

传递依赖检查结论：有效依赖链未发现 Prism 9 预览包或旧 `System.Drawing.Common 4.7.0` / `System.Configuration.ConfigurationManager 4.7.0` / `System.Security.Cryptography.ProtectedData 4.7.0` 链路。`CodeWF.NetWrapper` 已解析到 `2.1.2.3`。
## Package Versioning Convention

Keep NuGet package versions and Central Package Management settings in `Directory.Packages.props`, including shared version properties such as `AvaloniaVersion`. Keep `Directory.Build.props` focused on build, compiler, and NuGet package metadata. When referenced, `VC-LTL` and `YY-Thunks` should use their latest prerelease versions for OS platform compatibility.
