# CodeWF.EventBus.Socket Design Document

## Table of Contents

1. [Overview](#overview)
2. [Architecture](#architecture)
3. [Design Principles](#design-principles)
4. [Core Components](#core-components)
5. [Communication Protocol](#communication-protocol)
6. [Message Types](#message-types)
7. [CQRS Implementation](#cqrs-implementation)
8. [Code Analysis](#code-analysis)
9. [Usage Scenarios](#usage-scenarios)
10. [Best Practices](#best-practices)

---

## Overview

**CodeWF.EventBus.Socket** is a lightweight, Socket-based distributed event bus library that implements the Publish/Subscribe (Pub/Sub) pattern using raw TCP sockets. It provides a CQRS-compatible message communication mechanism without requiring any external message queue infrastructure.

### Key Characteristics

| Characteristic | Description |
|----------------|-------------|
| **Transport** | Raw TCP Socket (not HTTP/WebSocket) |
| **Pattern** | Publish/Subscribe with Request/Response support |
| **Protocol** | Custom binary protocol via `CodeWF.NetWeaver` |
| **Dependencies** | Zero external MQ dependencies |
| **Target Framework** | .NET Standard 2.0 / .NET 10+ |

---

## Architecture

### System Architecture Diagram

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

### Communication Flow

```
Client A                     Server                      Client B
   │                           │                            │
   │──── Subscribe ───────────▶│                            │
   │◀─── Response ─────────────│                            │
   │                           │                            │
   │                           │◀─── Subscribe ────────────│
   │                           │──── Response ─────────────▶│
   │                           │                            │
   │──── Publish ─────────────▶│                            │
   │◀─── Response ─────────────│                            │
   │                           │──── Push to subscribers ──▶│
   │                           │                            │
```

---

## Design Principles

### 1. Zero Dependency

The library does not rely on any external message queue services like RabbitMQ, Kafka, or Redis. This reduces:
- Infrastructure complexity
- Deployment overhead
- Network latency from additional hops

### 2. Channel-Based Async Processing

Both `EventServer` and `EventClient` use `System.Threading.Channels` for internal message processing:

```csharp
private readonly Channel<RequestPublish> _needPublishEvents = Channel.CreateUnbounded<RequestPublish>();
private readonly Channel<RequestQuery> _needQueryEvents = Channel.CreateUnbounded<RequestQuery>();
```

**Why Channel?**
- High-performance producer/consumer queue
- Async-native design
- Backpressure handling built-in
- Thread-safe by design

### 3. Thread-Safe Dictionaries

All shared state uses `ConcurrentDictionary`:

```csharp
private readonly ConcurrentDictionary<string, List<System.Net.Sockets.Socket>>
    _subscribedSubjectAndClients = new();

private readonly ConcurrentDictionary<string, List<Delegate>> 
    _subjectAndHandlers = new();
```

### 4. Task-Based Background Processing

Each major function runs as a background `Task`:

```csharp
Task.Factory.StartNew(async () =>
{
    while (_cancellationTokenSource?.IsCancellationRequested == false)
    {
        // Processing logic
    }
});
```

---

## Core Components

### EventServer

**File**: `src/CodeWF.EventBus.Socket/Server/EventServer.cs`

**Responsibilities**:
- Accept TCP client connections
- Route messages to appropriate handlers
- Maintain subscription registry
- Broadcast events to subscribers

**Key Methods**:

| Method | Purpose |
|--------|---------|
| `Start(host, port)` | Begin listening for connections |
| `Stop()` | Graceful shutdown |
| `ListenForClients()` | Accept new TCP connections |
| `ListenPublish()` | Process publish requests |
| `ListenQuery()` | Process query requests |
| `ListenResponseQuery()` | Route query responses back to requesters |

**Internal State**:

```csharp
// Subscriptions: subject -> list of subscribed clients
ConcurrentDictionary<string, List<Socket>> _subscribedSubjectAndClients;

// Query tracking: subject -> client waiting for response
ConcurrentDictionary<string, Socket> _querySubjectAndClients;

// Query tracking: subject -> original query
ConcurrentDictionary<string, RequestQuery> _querySubjectAndQueries;
```

### EventClient

**File**: `src/CodeWF.EventBus.Socket/Client/EventClient.cs`

**Responsibilities**:
- Establish TCP connection to server
- Send subscribe/unsubscribe requests
- Publish events
- Execute query/response operations
- Handle incoming events

**Key Methods**:

| Method | Purpose |
|--------|---------|
| `Connect(host, port)` | Establish connection |
| `Disconnect()` | Close connection |
| `Subscribe<T>(subject, handler)` | Subscribe to events |
| `Unsubscribe<T>(subject, handler)` | Unsubscribe from events |
| `Publish<T>(subject, message)` | Publish an event |
| `Query<TQuery, TResponse>(subject, query)` | Synchronous query |
| `QueryAsync<TQuery, TResponse>(subject, query)` | Async query |

**Internal State**:

```csharp
// Local subscriptions: subject -> list of handlers
ConcurrentDictionary<string, List<Delegate>> _subjectAndHandlers;

// Pending queries: taskId -> response
ConcurrentDictionary<string, UpdateEvent?> _queryTaskIdAndResponse;

// Response queue from server
Channel<SocketCommand> _responses = Channel.CreateUnbounded<SocketCommand>();
```

---

## Communication Protocol

### Packet Structure

The protocol uses `CodeWF.NetWeaver` for binary serialization. Each packet consists of a 23-byte header + variable-length body:

```
┌────────────────────────────────────────────┐
│  Header (23 bytes)                         │
│  ├── BufferLen: int     (4 bytes)          │
│  ├── SystemId: long     (8 bytes)          │
│  ├── ObjectId: ushort   (2 bytes)          │
│  ├── ObjectVersion: byte (1 byte)         │
│  └── UnixTimeMs: long   (8 bytes)          │
├────────────────────────────────────────────┤
│  Body (Variable length)                    │
└────────────────────────────────────────────┘
```

**Explanation**:
- `BufferLen`: Total packet length (Header + Body)
- `SystemId`: System identifier (passed to `Serialize()`)
- `ObjectId`: Object type ID (specified by `[NetHead]` attribute)
- `ObjectVersion`: Object version (specified by `[NetHead]` attribute)
- `UnixTimeMs`: Send timestamp in milliseconds

### Message Types

| ObjectId | Class | Direction | Purpose |
|----------|-------|-----------|---------|
| 1 | `RequestIsEventServer` | C → S | Health check |
| 2 | `RequestSubscribe` | C → S | Subscribe to a subject |
| 3 | `RequestUnsubscribe` | C → S | Unsubscribe from a subject |
| 4 | `RequestPublish` | C → S | Publish an event |
| 5 | `UpdateEvent` | S → C | Push event to client |
| 6 | `RequestQuery` | C → S | Query (Request/Response) |
| 254 | `ResponseCommon` | S → C | Acknowledge request |
| 255 | `Heartbeat` | Both | Keep-alive |

### Sequence Diagrams

#### Publish Flow

```
Client                      Server                      Subscriber
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

#### Query Flow (CQRS Pattern)

```
Client                      Server                      Query Handler
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

## CQRS Implementation

The library natively supports the CQRS (Command Query Responsibility Segregation) pattern:

### Command (Fire-and-Forget)

```csharp
// Command: No response expected
eventClient.Publish("event.user.created", new UserCreatedCommand
{
    UserId = Guid.NewGuid(),
    Username = "john_doe"
});
```

### Query (Request-Response)

```csharp
// Query: Expects a response
var response = eventClient.Query<UserQuery, UserQueryResponse>(
    "query.user.get",
    new UserQuery { UserId = userId }
);
```

**Query Implementation** (`EventClient.QueryAsync`):

```csharp
public async Task<(TResponse? Result, string ErrorMessage)> QueryAsync<TQuery, TResponse>(
    string subject, TQuery message, int overtimeMilliseconds = 3000)
{
    var taskId = SocketHelper.GetNewTaskId();
    
    // 1. Send query request
    var request = new RequestQuery
    {
        TaskId = taskId,
        Subject = subject,
        Buffer = message.SerializeObject()
    };
    _queryTaskIdAndResponse[request.TaskId] = default;
    SendCommand(request);

    // 2. Wait for response with timeout
    var tcs = new TaskCompletionSource<UpdateEvent>();
    using var timeoutTimer = new Timer(
        _ => tcs.TrySetException(new Exception("Query timeout!")),
        null, overtimeMilliseconds, Timeout.Infinite);

    // 3. Poll for response
    while (!cancellationToken.IsCancellationRequested)
    {
        if (_queryTaskIdAndResponse.TryGetValue(taskId, out var response) && response != null)
        {
            return ((TResponse)response.Buffer.DeserializeObject(typeof(TResponse)), string.Empty);
        }
        await Task.Delay(10, cancellationToken);
    }

    return (default, "Operation cancelled");
}
```

---

## Code Analysis

### EventServer Client Handling

```csharp
private void HandleClient(System.Net.Sockets.Socket tcpClient)
{
    Task.Factory.StartNew(async () =>
    {
        while (_cancellationTokenSource?.IsCancellationRequested == false)
        {
            var (success, buffer, headInfo) = await tcpClient.ReadPacketAsync(token);
            if (!success) break;

            // Route based on message type
            if (headInfo.IsNetObject<RequestSubscribe>())
                HandleRequest(tcpClient, buffer.Deserialize<RequestSubscribe>());
            else if (headInfo.IsNetObject<RequestPublish>())
                HandleRequest(tcpClient, buffer.Deserialize<RequestPublish>());
            // ... other message types
        }
    });
}
```

### EventClient Response Processing

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

            // Route based on message type
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

### Subscription Management

**Client Side**:
```csharp
private void AddSubscribe(string subject, Delegate eventHandler)
{
    if (!_subjectAndHandlers.TryGetValue(subject, out var handlers))
    {
        handlers = [eventHandler];
        _subjectAndHandlers.TryAdd(subject, handlers);
        
        // Notify server only on first subscription
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

**Server Side**:
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

## Usage Scenarios

### Scenario 1: Microservice Event Notification

```
┌──────────────┐      Publish       ┌──────────────┐
│ OrderService │──────────────────▶│ EventServer  │
└──────────────┘                   └──────┬───────┘
                                          │
                    ┌─────────────────────┼─────────────────────┐
                    │                     │                     │
                    ▼                     ▼                     ▼
             ┌──────────────┐      ┌──────────────┐      ┌──────────────┐
             │EmailService  │      │InventoryService│     │ShippingService│
             └──────────────┘      └──────────────┘      └──────────────┘
```

```csharp
// OrderService publishes
_orderClient.Publish("event.order.created", new OrderCreatedEvent
{
    OrderId = orderId,
    CustomerId = customerId,
    TotalAmount = total
});

// EmailService subscribes
_emailClient.Subscribe<OrderCreatedEvent>("event.order.created", 
    async order => await _emailService.SendConfirmationAsync(order));
```

### Scenario 2: CQRS with Query Handler

```
┌──────────────┐                   ┌──────────────┐
│   Client     │── Query<User> ───▶│ EventServer  │
└──────────────┘                   └──────┬───────┘
      ▲                                  │
      │                                  ▼
      │                           ┌──────────────┐
      └───── Response<User> ──────│ QueryHandler │
                                   └──────────────┘
```

```csharp
// Client queries user data
var user = _client.Query<UserQuery, UserResponse>("query.user.get",
    new UserQuery { UserId = userId });

// Query handler responds
_userService.Subscribe<UserQuery>("query.user.get",
    query => {
        var user = _repository.GetById(query.UserId);
        _client.Publish("query.user.get", new UserResponse { User = user });
    });
```

### Scenario 3: Distributed Request/Response

```
┌──────────────┐                   ┌──────────────┐
│  Service A   │─── Request ─────▶│ EventServer  │
└──────────────┘                   └──────┬───────┘
      ▲                                  │
      │                                  ▼
      └───────── Response ───────────────┤
                                   ┌──────────────┐
                                   │  Service B   │
                                   └──────────────┘
```

---

## Best Practices

### 1. Connection Management

```csharp
public class MyService : IDisposable
{
    private readonly IEventClient _client;
    
    public MyService()
    {
        _client = new EventClient();
        _client.Connect("127.0.0.1", 9100);
        
        // Wait for connection
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

### 2. Subscription Cleanup

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
            // Note: Unsubscribe requires the original handler
            // Store handlers as instance fields for proper cleanup
        }
    }
}
```

### 3. Query Timeout Handling

```csharp
var (result, error) = await _client.QueryAsync<ProductQuery, ProductResponse>(
    "query.product.get",
    new ProductQuery { ProductId = productId },
    overtimeMilliseconds: 5000  // 5 second timeout
);

if (!string.IsNullOrEmpty(error))
{
    _logger.LogError("Query failed: {Error}", error);
    // Handle timeout or connection issue
}
```

### 4. Heartbeat Configuration

The server sends heartbeat messages every 5 seconds:

```csharp
private const int HeartbeatInterval = 5000;
private const int MaxTrySendHeartTime = 3;
private int _trySendHeartbeatTimes;
```

Client should handle heartbeat for connection monitoring.

### 5. Error Handling

```csharp
try
{
    _client.Publish("event.order", order, out var error);
    if (!string.IsNullOrEmpty(error))
    {
        _logger.LogWarning("Publish returned error: {Error}", error);
    }
}
catch (SocketException ex)
{
    _logger.LogError(ex, "Socket error during publish");
    // Implement reconnection logic
}
```

### 6. Subject Naming Convention

Use dot-separated hierarchical names:

```
event.{domain}.{entity}.{action}
query.{domain}.{entity}.{operation}
command.{domain}.{entity}.{operation}
```

Examples:
- `event.user.created`
- `event.order.placed`
- `query.product.details`
- `command.inventory.adjust`

---

## Performance Considerations

### Channel Bounded vs Unbounded

The library uses unbounded channels for simplicity. For high-throughput scenarios, consider bounded channels with backpressure:

```csharp
// Current (unbounded)
Channel<RequestPublish> _needPublishEvents = Channel.CreateUnbounded<RequestPublish>();

// For high load (bounded with backpressure)
Channel<RequestPublish> _needPublishEvents = Channel.CreateBounded<RequestPublish>(
    new BoundedChannelOptions(10000)
    {
        FullMode = BoundedChannelFullMode.Wait
    });
```

### Concurrent Dictionary Contention

Under high load, `ConcurrentDictionary` can become a bottleneck. For extreme scalability, consider sharding by subject.

---

## Future Enhancements

- [ ] TLS/SSL support for encrypted communication
- [ ] Authentication and authorization
- [ ] Message compression
- [ ] Guaranteed delivery with acknowledgment
- [ ] Cluster support for horizontal scaling
- [ ] Message persistence layer

---

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
