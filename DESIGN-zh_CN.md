# CodeWF.EventBus.Socket 设计文档

## 目录

1. [概述](#概述)
2. [架构设计](#架构设计)
3. [设计原则](#设计原则)
4. [核心组件](#核心组件)
5. [通信协议](#通信协议)
6. [消息类型](#消息类型)
7. [CQRS 实现](#cqrs-实现)
8. [代码解析](#代码解析)
9. [使用场景](#使用场景)
10. [最佳实践](#最佳实践)

---

## 概述

**CodeWF.EventBus.Socket** 是一个轻量级的、基于 Socket 的分布式事件总线库，它使用原始 TCP Socket 实现发布/订阅（Pub/Sub）模式。在无需外部消息队列基础设施的情况下，提供兼容 CQRS 的消息通信机制。

### 核心特性

| 特性 | 描述 |
|------|------|
| **传输方式** | 原始 TCP Socket（非 HTTP/WebSocket） |
| **模式** | 发布/订阅 + 请求/响应支持 |
| **协议** | 通过 `CodeWF.NetWeaver` 实现的自定义二进制协议 |
| **依赖** | 零外部 MQ 依赖 |
| **目标框架** | .NET Standard 2.0 / .NET 10+ |

---

## 架构设计

### 系统架构图

```
┌─────────────────────────────────────────────────────────────────┐
│                        EventServer                               │
│  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐        │
│  │ ListenFor     │  │ ListenPublish │  │ ListenQuery   │        │
│  │ Clients       │  │               │  │               │        │
│  └───────┬───────┘  └───────┬───────┘  └───────┬───────┘        │
│          │                  │                  │                 │
│  ┌───────┴──────────────────┴──────────────────┴───────┐        │
│  │              Channel<RequestPublish>                  │        │
│  │              Channel<RequestQuery>                   │        │
│  │              Channel<RequestPublish> (Response)      │        │
│  └───────┬──────────────────┬──────────────────┬───────┘        │
│          │                  │                  │                 │
│  ┌───────┴───────┐  ┌───────┴───────┐  ┌───────┴───────┐        │
│  │ _subscribed   │  │ _querySubject │  │ _querySubject │        │
│  │ SubjectAnd    │  │ AndClients    │  │ AndQueries    │        │
│  │ Clients       │  │               │  │               │        │
│  └───────┬───────┘  └───────────────┘  └───────────────┘        │
└──────────┼──────────────────────────────────────────────────────┘
           │
    ┌──────┴──────┐
    │  TCP Socket │
    └──────┬──────┘
           │
┌──────────┼──────────────────────────────────────────────────────┐
│          │              EventClient                             │
│  ┌───────┴───────┐  ┌───────────────┐  ┌───────────────┐       │
│  │ ListenFor     │  │ CheckResponse │  │ CheckIsEvent   │       │
│  │ Server        │  │               │  │ Server         │       │
│  └───────┬───────┘  └───────┬───────┘  └───────────────┘       │
│          │                  │                                  │
│  ┌───────┴──────────────────┴───────┐                          │
│  │  Channel<SocketCommand>           │                          │
│  └───────┬──────────────────┬───────┘                          │
│          │                  │                                   │
│  ┌───────┴───────┐  ┌───────┴───────┐                           │
│  │ _subjectAnd   │  │ _queryTaskId  │                           │
│  │ Handlers      │  │ AndResponse   │                           │
│  └───────────────┘  └───────────────┘                           │
└─────────────────────────────────────────────────────────────────┘
```

### 通信流程

```
客户端 A                    服务器                       客户端 B
   │                           │                            │
   │──── 订阅 ────────────────▶│                            │
   │◀─── 响应 ──────────────── │                            │
   │                           │                            │
   │                           │◀─── 订阅 ────────────────│
   │                           │──── 响应 ──────────────▶│
   │                           │                            │
   │──── 发布 ────────────────▶│                            │
   │◀─── 响应 ──────────────── │                            │
   │                           │──── 推送给订阅者 ─────────▶│
   │                           │                            │
```

---

## 设计原则

### 1. 零依赖

本库不依赖任何外部消息队列服务（如 RabbitMQ、Kafka 或 Redis），这减少了：
- 基础设施复杂性
- 部署开销
- 额外跳转带来的网络延迟

### 2. 基于 Channel 的异步处理

`EventServer` 和 `EventClient` 都使用 `System.Threading.Channels` 进行内部消息处理：

```csharp
private readonly Channel<RequestPublish> _needPublishEvents = Channel.CreateUnbounded<RequestPublish>();
private readonly Channel<RequestQuery> _needQueryEvents = Channel.CreateUnbounded<RequestQuery>();
```

**为什么选择 Channel？**
- 高性能的生产者/消费者队列
- 原生异步设计
- 内置背压处理
- 线程安全设计

### 3. 线程安全字典

所有共享状态都使用 `ConcurrentDictionary`：

```csharp
private readonly ConcurrentDictionary<string, List<System.Net.Sockets.Socket>>
    _subscribedSubjectAndClients = new();

private readonly ConcurrentDictionary<string, List<Delegate>> 
    _subjectAndHandlers = new();
```

### 4. 基于 Task 的后台处理

每个主要功能都作为后台 `Task` 运行：

```csharp
Task.Factory.StartNew(async () =>
{
    while (_cancellationTokenSource?.IsCancellationRequested == false)
    {
        // 处理逻辑
    }
});
```

---

## 核心组件

### EventServer

**文件**: `src/CodeWF.EventBus.Socket/Server/EventServer.cs`

**职责**:
- 接受 TCP 客户端连接
- 路由消息到相应的处理器
- 维护订阅注册表
- 向订阅者广播事件

**关键方法**:

| 方法 | 用途 |
|------|------|
| `Start(host, port)` | 开始监听连接 |
| `Stop()` | 优雅关闭 |
| `ListenForClients()` | 接受新的 TCP 连接 |
| `ListenPublish()` | 处理发布请求 |
| `ListenQuery()` | 处理查询请求 |
| `ListenResponseQuery()` | 将查询响应路由回请求方 |

**内部状态**:

```csharp
// 订阅关系：subject -> 已订阅的客户端列表
ConcurrentDictionary<string, List<Socket>> _subscribedSubjectAndClients;

// 查询追踪：subject -> 等待响应的客户端
ConcurrentDictionary<string, Socket> _querySubjectAndClients;

// 查询追踪：subject -> 原始查询
ConcurrentDictionary<string, RequestQuery> _querySubjectAndQueries;
```

### EventClient

**文件**: `src/CodeWF.EventBus.Socket/Client/EventClient.cs`

**职责**:
- 建立到服务器的 TCP 连接
- 发送订阅/取消订阅请求
- 发布事件
- 执行查询/响应操作
- 处理传入事件

**关键方法**:

| 方法 | 用途 |
|------|------|
| `Connect(host, port)` | 建立连接 |
| `Disconnect()` | 关闭连接 |
| `Subscribe<T>(subject, handler)` | 订阅事件 |
| `Unsubscribe<T>(subject, handler)` | 取消订阅 |
| `Publish<T>(subject, message)` | 发布事件 |
| `Query<TQuery, TResponse>(subject, query)` | 同步查询 |
| `QueryAsync<TQuery, TResponse>(subject, query)` | 异步查询 |

**内部状态**:

```csharp
// 本地订阅：subject -> 处理器列表
ConcurrentDictionary<string, List<Delegate>> _subjectAndHandlers;

// 待处理查询：taskId -> 响应
ConcurrentDictionary<string, UpdateEvent?> _queryTaskIdAndResponse;

// 从服务器接收的响应队列
Channel<SocketCommand> _responses = Channel.CreateUnbounded<SocketCommand>();
```

---

## 通信协议

### 数据包结构

协议使用 `CodeWF.NetWeaver` 进行二进制序列化。每个数据包包含 23 字节的头部 + 可变长度的数据体：

```
┌────────────────────────────────────────────┐
│  Header (23 bytes)                         │
│  ├── BufferLen: int     (4 bytes)          │
│  ├── SystemId: long     (8 bytes)          │
│  ├── ObjectId: ushort   (2 bytes)          │
│  ├── ObjectVersion: byte (1 byte)         │
│  └── UnixTimeMs: long   (8 bytes)          │
├────────────────────────────────────────────┤
│  Body (可变长度)                            │
└────────────────────────────────────────────┘
```

**说明**：
- `BufferLen`：整个数据包（Header + Body）的长度
- `SystemId`：系统标识符（由 `Serialize()` 传入）
- `ObjectId`：对象类型 ID（由 `[NetHead]` 属性指定）
- `ObjectVersion`：对象版本号（由 `[NetHead]` 属性指定）
- `UnixTimeMs`：发送时间戳（毫秒）

### 消息类型

| ObjectId | 类名 | 方向 | 用途 |
|----------|------|------|------|
| 1 | `RequestIsEventServer` | C → S | 健康检查 |
| 2 | `RequestSubscribe` | C → S | 订阅某个主题 |
| 3 | `RequestUnsubscribe` | C → S | 取消订阅某个主题 |
| 4 | `RequestPublish` | C → S | 发布事件 |
| 5 | `UpdateEvent` | S → C | 向客户端推送事件 |
| 6 | `RequestQuery` | C → S | 查询（请求/响应模式） |
| 254 | `ResponseCommon` | S → C | 确认请求已收到 |
| 255 | `Heartbeat` | 双向 | 心跳保活 |

### 时序图

#### 发布流程

```
客户端                     服务器                       订阅者
  │                           │                            │
  │ Publish(RequestPublish)   │                            │
  │──────────────────────────▶│                            │
  │                           │                            │
  │ ResponseCommon            │                            │
  │◀──────────────────────────│                            │
  │                           │                            │
  │                           │ UpdateEvent                │
  │                           │───────────────────────────▶│
  │                           │                            │
```

#### 查询流程（CQRS 模式）

```
客户端                     服务器                       查询处理器
  │                           │                            │
  │ Query(RequestQuery)       │                            │
  │──────────────────────────▶│                            │
  │                           │                            │
  │                           │ UpdateEvent                │
  │                           │───────────────────────────▶│
  │                           │                            │
  │                           │◀───────────────────────────│
  │                           │ Publish(Response)          │
  │                           │                            │
  │ UpdateEvent(Response)     │                            │
  │◀──────────────────────────│                            │
  │                           │                            │
```

---

## CQRS 实现

本库原生支持 CQRS（命令查询职责分离）模式：

### Command（命令 - 发送即忘记）

```csharp
// Command：不需要响应
eventClient.Publish("event.user.created", new UserCreatedCommand
{
    UserId = Guid.NewGuid(),
    Username = "john_doe"
});
```

### Query（查询 - 请求/响应）

```csharp
// Query：需要响应
var response = eventClient.Query<UserQuery, UserQueryResponse>(
    "query.user.get",
    new UserQuery { UserId = userId }
);
```

**Query 实现解析**（`EventClient.QueryAsync`）：

```csharp
public async Task<(TResponse? Result, string ErrorMessage)> QueryAsync<TQuery, TResponse>(
    string subject, TQuery message, int overtimeMilliseconds = 3000)
{
    var taskId = SocketHelper.GetNewTaskId();

    // 1. 发送查询请求
    var request = new RequestQuery
    {
        TaskId = taskId,
        Subject = subject,
        Buffer = message.SerializeObject()
    };
    _queryTaskIdAndResponse[request.TaskId] = default;
    SendCommand(request);

    // 2. 带超时等待响应
    var tcs = new TaskCompletionSource<UpdateEvent>();
    using var timeoutTimer = new Timer(
        _ => tcs.TrySetException(new Exception("查询超时！")),
        null, overtimeMilliseconds, Timeout.Infinite);

    // 3. 轮询等待响应
    while (!cancellationToken.IsCancellationRequested)
    {
        if (_queryTaskIdAndResponse.TryGetValue(taskId, out var response) && response != null)
        {
            return ((TResponse)response.Buffer.DeserializeObject(typeof(TResponse)), string.Empty);
        }
        await Task.Delay(10, cancellationToken);
    }

    return (default, "操作已取消");
}
```

---

## 代码解析

### EventServer 客户端处理

```csharp
private void HandleClient(System.Net.Sockets.Socket tcpClient)
{
    Task.Factory.StartNew(async () =>
    {
        while (_cancellationTokenSource?.IsCancellationRequested == false)
        {
            var (success, buffer, headInfo) = await tcpClient.ReadPacketAsync(token);
            if (!success) break;

            // 根据消息类型路由
            if (headInfo.IsNetObject<RequestSubscribe>())
                HandleRequest(tcpClient, buffer.Deserialize<RequestSubscribe>());
            else if (headInfo.IsNetObject<RequestPublish>())
                HandleRequest(tcpClient, buffer.Deserialize<RequestPublish>());
            // ... 其他消息类型
        }
    });
}
```

### EventClient 响应处理

```csharp
private void CheckResponse()
{
    Task.Factory.StartNew(async () =>
    {
        while (_cancellationTokenSource is { IsCancellationRequested: false })
        {
            var readTask = _responses.Reader.WaitToReadAsync(token);
            if (!await readTask) break;
            if (!_responses.Reader.TryRead(out var response)) continue;

            // 根据消息类型路由
            if (response.IsMessage<ResponseCommon>())
                HandleResponse(response.Message<ResponseCommon>());
            else if (response.IsMessage<UpdateEvent>())
                HandleResponse(response.Message<UpdateEvent>());
            else if (response.IsMessage<Heartbeat>())
                HandleResponse(response.Message<Heartbeat>());
        }
    });
}
```

### 订阅管理

**客户端侧**:
```csharp
private void AddSubscribe(string subject, Delegate eventHandler)
{
    if (!_subjectAndHandlers.TryGetValue(subject, out var handlers))
    {
        handlers = [eventHandler];
        _subjectAndHandlers.TryAdd(subject, handlers);

        // 仅在首次订阅时通知服务器
        SendCommand(new RequestSubscribe
        {
            TaskId = SocketHelper.GetNewTaskId(),
            Subject = subject
        });
    }
    else
    {
        handlers.Add(eventHandler);
    }
}
```

**服务器侧**:
```csharp
private void HandleRequest(System.Net.Sockets.Socket client, RequestSubscribe command)
{
    if (!_subscribedSubjectAndClients.TryGetValue(command.Subject, out var sockets))
    {
        sockets = [client];
        _subscribedSubjectAndClients.TryAdd(command.Subject, sockets);
    }
    else
    {
        sockets.Add(client);
    }

    SendCommand(client, new ResponseCommon
    {
        TaskId = command.TaskId,
        Status = (byte)ResponseCommonStatus.Success
    });
}
```

---

## 使用场景

### 场景一：微服务事件通知

```
┌──────────────┐      发布       ┌──────────────┐
│ OrderService │───────────────▶│ EventServer  │
└──────────────┘                └──────┬───────┘
                                      │
                    ┌─────────────────┼─────────────────┐
                    │                 │                 │
                    ▼                 ▼                 ▼
             ┌──────────────┐  ┌──────────────┐  ┌──────────────┐
             │ EmailService │  │InventoryService│ │ShippingService│
             └──────────────┘  └──────────────┘  └──────────────┘
```

```csharp
// OrderService 发布订单创建事件
_orderClient.Publish("event.order.created", new OrderCreatedEvent
{
    OrderId = orderId,
    CustomerId = customerId,
    TotalAmount = total
});

// EmailService 订阅该事件
_emailClient.Subscribe<OrderCreatedEvent>("event.order.created",
    async order => await _emailService.SendConfirmationAsync(order));
```

### 场景二：CQRS 查询模式

```
┌──────────────┐                   ┌──────────────┐
│   客户端      │── Query<User> ──▶│ EventServer  │
└──────────────┘                   └──────┬───────┘
      ▲                                │
      │                                ▼
      │                         ┌──────────────┐
      └───── Response<User> ────│ QueryHandler │
                                 └──────────────┘
```

```csharp
// 客户端查询用户数据
var user = _client.Query<UserQuery, UserResponse>("query.user.get",
    new UserQuery { UserId = userId });

// 查询处理器响应
_userService.Subscribe<UserQuery>("query.user.get",
    query => {
        var user = _repository.GetById(query.UserId);
        _client.Publish("query.user.get", new UserResponse { User = user });
    });
```

### 场景三：分布式请求/响应

```
┌──────────────┐                   ┌──────────────┐
│  Service A   │──── Request ────▶│ EventServer  │
└──────────────┘                   └──────┬───────┘
      ▲                                │
      │                                ▼
      └─────────── Response ───────────┤
                                 ┌──────────────┐
                                 │  Service B   │
                                 └──────────────┘
```

---

## 最佳实践

### 1. 连接管理

```csharp
public class MyService : IDisposable
{
    private readonly IEventClient _client;

    public MyService()
    {
        _client = new EventClient();
        _client.Connect("127.0.0.1", 9100);

        // 等待连接建立
        var timeout = DateTime.Now.AddSeconds(5);
        while (_client.ConnectStatus != ConnectStatus.Connected && DateTime.Now < timeout)
        {
            Thread.Sleep(100);
        }
    }

    public void Dispose()
    {
        _client?.Disconnect();
    }
}
```

### 2. 订阅清理

```csharp
public class EventHandler : IDisposable
{
    private readonly IEventClient _client;
    private readonly List<string> _subscriptions = new();

    public void SubscribeAll()
    {
        _client.Subscribe<UserCreatedEvent>("event.user.created", OnUserCreated);
        _subscriptions.Add("event.user.created");

        _client.Subscribe<OrderPlacedEvent>("event.order.placed", OnOrderPlaced);
        _subscriptions.Add("event.order.placed");
    }

    public void Dispose()
    {
        foreach (var subject in _subscriptions)
        {
            // 注意：取消订阅需要原始的处理器
            // 将处理器存为实例字段以便正确清理
        }
    }
}
```

### 3. 查询超时处理

```csharp
var (result, error) = await _client.QueryAsync<ProductQuery, ProductResponse>(
    "query.product.get",
    new ProductQuery { ProductId = productId },
    overtimeMilliseconds: 5000  // 5秒超时
);

if (!string.IsNullOrEmpty(error))
{
    _logger.LogError("查询失败: {Error}", error);
    // 处理超时或连接问题
}
```

### 4. 心跳配置

服务器每 5 秒发送一次心跳消息：

```csharp
private const int HeartbeatInterval = 5000;
private const int MaxTrySendHeartTime = 3;
private int _trySendHeartbeatTimes;
```

客户端应处理心跳以监控连接状态。

### 5. 错误处理

```csharp
try
{
    _client.Publish("event.order", order, out var error);
    if (!string.IsNullOrEmpty(error))
    {
        _logger.LogWarning("发布返回错误: {Error}", error);
    }
}
catch (SocketException ex)
{
    _logger.LogError(ex, "发布时发生 Socket 错误");
    // 实现重连逻辑
}
```

### 6. 主题命名规范

使用点分隔的层次化命名：

```
event.{领域}.{实体}.{动作}
query.{领域}.{实体}.{操作}
command.{领域}.{实体}.{操作}
```

示例：
- `event.user.created`
- `event.order.placed`
- `query.product.details`
- `command.inventory.adjust`

---

## 性能考虑

### Channel 有界 vs 无界

本库使用无界 channel 以简化实现。在高吞吐量场景下，可考虑使用有界 channel 实现背压：

```csharp
// 当前实现（无界）
Channel<RequestPublish> _needPublishEvents = Channel.CreateUnbounded<RequestPublish>();

// 高负载场景（有界 + 背压）
Channel<RequestPublish> _needPublishEvents = Channel.CreateBounded<RequestPublish>(
    new BoundedChannelOptions(10000)
    {
        FullMode = BoundedChannelFullMode.Wait
    });
```

### ConcurrentDictionary 竞争

在高负载下，`ConcurrentDictionary` 可能成为瓶颈。极端可扩展性场景下，考虑按 subject 分片。

---

## 未来增强

- [ ] TLS/SSL 支持加密通信
- [ ] 认证和授权
- [ ] 消息压缩
- [ ] 带确认的可靠交付
- [ ] 集群支持水平扩展
- [ ] 消息持久化层

---

## 许可证

本项目基于 MIT 许可证开源。详见 [LICENSE](LICENSE)。
