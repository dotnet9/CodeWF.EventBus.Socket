# CodeWF.EventBus.Socket

简体中文 | [English](README.md)

[![NuGet](https://img.shields.io/nuget/v/CodeWF.EventBus.Socket.svg)](https://www.nuget.org/packages/CodeWF.EventBus.Socket/)
[![NuGet](https://img.shields.io/nuget/dt/CodeWF.EventBus.Socket.svg)](https://www.nuget.org/packages/CodeWF.EventBus.Socket/)
[![License](https://img.shields.io/github/license/dotnet9/CodeWF.EventBus.Socket)](LICENSE)

`CodeWF.EventBus.Socket` 是一个面向 C# 进程间通信的轻量级 TCP 事件总线库。它基于 Socket 提供发布订阅与 Query 式请求响应能力，让本地多进程或轻量服务之间可以通信，而不必先引入 RabbitMQ、Kafka、Redis 等外部 MQ 基础设施。

![Command](docs/imgs/command.png)

![Query](docs/imgs/query.png)

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

- [设计文档](docs/design-zh_CN.md)
- [English design document](docs/design.md)
- [示例工程](src/EventBusDemo)

## 通信协议

数据通过 `TCP` 进行传输。

![Protocol Frame](docs/imgs/0.0.8@2x.png)

## 说明

- 这个库适合轻量级事件分发和进程间通信场景。
- 底层传输由 `CodeWF.NetWrapper` 实现，同名传输对象直接复用包内定义，而不是在本项目重复维护。
- 事件总线层自己的请求、查询和推送协议对象仍保留在本项目中，因为它们承载的是本库特有的语义。
- 当前消息仅保存在内存中，不提供持久化、重试队列或进程重启后的投递保证。
- 如果用于生产环境，请按实际需要补充认证、加密、监控、限流与重试策略。
