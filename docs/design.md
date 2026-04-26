# CodeWF.EventBus.Socket Design

## Overview

`CodeWF.EventBus.Socket` is a lightweight event bus library built on top of `CodeWF.NetWrapper`. Its goal is to keep cross-process event delivery simple:

- one small server process routes messages
- clients can subscribe to subjects and publish events
- query/response uses the same subject while preserving correlation by request `TaskId`
- no third-party MQ is required

## Architecture

![Architecture](./imgs/architecture.svg)

### Components

| Component | Responsibility |
| --- | --- |
| `EventServer` | Accepts socket clients, tracks subscriptions, routes publishes, and maps pending queries to original requesters |
| `EventClient` | Manages socket connection, local handlers, heartbeat, publish/query calls, and response dispatch |
| `CodeWF.NetWrapper` | Provides `TcpSocketServer`, `TcpSocketClient`, transport event dispatch, and shared transport objects |
| `CodeWF.NetWeaver` | Serializes custom packet types and binary payloads used by both this project and `CodeWF.NetWrapper` |

## Message Types

| Type | Purpose | Key Fields |
| --- | --- | --- |
| `RequestSubscribe` | Subscribe to a subject | `TaskId`, `Subject` |
| `RequestUnsubscribe` | Remove a subscription | `TaskId`, `Subject` |
| `RequestPublish` | Publish an event or query response | `TaskId`, `Subject`, `QueryTaskId`, `Buffer` |
| `RequestQuery` | Send a query request | `TaskId`, `Subject`, `Buffer` |
| `UpdateEvent` | Push an event/query to clients | `TaskId`, `Subject`, `IsQueryRequest`, `Buffer` |
| `ResponseCommon` | Common server acknowledgement | `TaskId`, `Status`, `Message` |
| `CodeWF.NetWrapper.Commands.SocketCommand` | Transport event raised by `NetWrapper` | `Client`, `HeadInfo` |
| `CodeWF.NetWrapper.Models.Heartbeat` | Shared heartbeat packet reused from `NetWrapper` | `TaskId` |

### Reused vs custom objects

- Reused directly from `CodeWF.NetWrapper`: `TcpSocketServer`, `TcpSocketClient`, `SocketCommand`, `Heartbeat`
- Kept in this project for event-bus protocol semantics: `RequestSubscribe`, `RequestUnsubscribe`, `RequestPublish`, `RequestQuery`, `UpdateEvent`, `ResponseCommon`

## Publish/Subscribe Flow

1. A client sends `RequestSubscribe` for a subject.
2. The server stores the socket under that subject.
3. Another client sends `RequestPublish`.
4. The server fans out `UpdateEvent` to each subscribed client.
5. Each client invokes local delegates registered for that subject.

## Query Flow

![Query Flow](./imgs/query-flow.svg)

1. The requester sends `RequestQuery` with a unique `TaskId`.
2. The server stores a pending-query entry keyed by that `TaskId`.
3. The server forwards the query to subscribers as `UpdateEvent` with `IsQueryRequest = true`.
4. The responder handles the message and calls `Publish` on the same subject.
5. `EventClient` automatically attaches `QueryTaskId` when a publish happens inside a query handler.
6. The server routes that response back to the original requester and removes the pending-query entry.

### Why this matters

The original design used only the subject to match a response to a pending query. That breaks when two requests are in flight on the same subject at the same time. The current design correlates by `TaskId`, so concurrent queries on the same subject stay isolated.

## Runtime Behavior

### Connection model

- `EventServer` is built on `TcpSocketServer` and receives inbound packets through `CodeWF.EventBus.Default`.
- `EventClient` is built on `TcpSocketClient` and receives server responses through the same transport event bus.
- If heartbeat retries exceed the threshold, the client attempts to reconnect.

### Delivery model

- Messages are in-memory only.
- Ordering is best-effort within a single connection flow.
- The first valid query response wins for a given request `TaskId`.
- Late or duplicate query responses are ignored instead of being broadcast as normal events.

## Limitations

- No persistence or broker-side durable queue
- No built-in authentication or encryption
- No clustering or shared state across multiple server instances
- No built-in metrics, tracing, or dead-letter handling

## Suitable Scenarios

- Desktop or toolchain processes on the same machine
- Internal service utilities where a full MQ is unnecessary
- Lightweight CQRS-style coordination between small components

## Future Improvements

- authentication and authorization hooks
- optional TLS or payload encryption
- configurable reconnect and heartbeat strategies
- richer diagnostics and observability
