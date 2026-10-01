using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace CodeWF.EventBus.Socket.Test;

/// <summary>
/// 断线重连与离线消息补发（issue #1）的集成测试：
/// 覆盖断线期间广播补发、缓冲容量上限和功能开关。
/// </summary>
[Collection(LoggingCollection.Name)]
public class OfflineMessageIntegrationTest
{
    [Fact]
    public async Task Reconnect_ShouldReplayMessagesPublishedWhileOffline()
    {
        var port = GetAvailablePort();
        // 服务端空闲超时 1 秒主动下线死连接（客户端心跳 200ms 远小于阈值，不会被误踢）。
        var server = new EventServer(new EventBusOptions
        {
            ClientIdleTimeout = TimeSpan.FromSeconds(1)
        });
        var publisher = new EventClient(new EventBusOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(200)
        });
        var subscriber = new EventClient(new EventBusOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(200),
            ReconnectInterval = TimeSpan.FromMilliseconds(200)
        });

        var offlineReceived = new ConcurrentQueue<string>();
        var threeOfflineReceived = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var liveReceived = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await server.StartAsync(IPAddress.Loopback.ToString(), port);
            await subscriber.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await publisher.ConnectAsync(IPAddress.Loopback.ToString(), port);

            subscriber.Subscribe<string>("demo.offline.live", message => liveReceived.TrySetResult(message));
            subscriber.Subscribe<string>("demo.offline.batch", message =>
            {
                offlineReceived.Enqueue(message);
                if (offlineReceived.Count >= 3)
                {
                    threeOfflineReceived.TrySetResult(true);
                }
            });
            await WaitForTransportAsync();

            publisher.Publish("demo.offline.live", "在线消息", out _);
            Assert.Equal("在线消息", await WaitAsync(liveReceived.Task));

            // 断开后等服务端空闲扫描（1 秒阈值 + 1 秒扫描周期）把订阅转入离线缓冲，再发布三条离线消息。
            subscriber.Disconnect();
            await Task.Delay(2500);

            publisher.Publish("demo.offline.batch", "离线-1", out _);
            publisher.Publish("demo.offline.batch", "离线-2", out _);
            publisher.Publish("demo.offline.batch", "离线-3", out _);

            await subscriber.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await WaitAsync(threeOfflineReceived.Task, 5000);
            await Task.Delay(250);

            Assert.Equal(3, offlineReceived.Count);
            Assert.Equal(new[] { "离线-1", "离线-2", "离线-3" }, offlineReceived.ToArray());
        }
        finally
        {
            publisher.Disconnect();
            subscriber.Disconnect();
            server.Stop();
        }
    }

    [Fact]
    public async Task OfflineBuffer_ShouldRespectCapacityAndDropOldest()
    {
        var port = GetAvailablePort();
        var server = new EventServer(new EventBusOptions
        {
            OfflineMessageCapacity = 3,
            ClientIdleTimeout = TimeSpan.FromSeconds(1)
        });
        var publisher = new EventClient(new EventBusOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(200)
        });
        var subscriber = new EventClient(new EventBusOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(200),
            ReconnectInterval = TimeSpan.FromMilliseconds(200)
        });

        var received = new ConcurrentQueue<string>();
        var threeReceived = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await server.StartAsync(IPAddress.Loopback.ToString(), port);
            await subscriber.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await publisher.ConnectAsync(IPAddress.Loopback.ToString(), port);

            subscriber.Subscribe<string>("demo.offline.capacity", message =>
            {
                received.Enqueue(message);
                if (received.Count >= 3)
                {
                    threeReceived.TrySetResult(true);
                }
            });
            await WaitForTransportAsync();

            subscriber.Disconnect();
            await Task.Delay(2500);

            for (var index = 1; index <= 5; index++)
            {
                publisher.Publish("demo.offline.capacity", $"m{index}", out _);
            }

            await subscriber.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await WaitAsync(threeReceived.Task, 5000);
            await Task.Delay(250);

            Assert.Equal(new[] { "m3", "m4", "m5" }, received.ToArray());
        }
        finally
        {
            publisher.Disconnect();
            subscriber.Disconnect();
            server.Stop();
        }
    }

    [Fact]
    public async Task DisabledOfflineMessage_ShouldNotReplay()
    {
        var port = GetAvailablePort();
        var server = new EventServer(new EventBusOptions
        {
            EnableOfflineMessage = false,
            ClientIdleTimeout = TimeSpan.FromSeconds(1)
        });
        var publisher = new EventClient(new EventBusOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(200)
        });
        var subscriber = new EventClient(new EventBusOptions
        {
            HeartbeatInterval = TimeSpan.FromMilliseconds(200),
            ReconnectInterval = TimeSpan.FromMilliseconds(200)
        });

        var receiveCount = 0;

        try
        {
            await server.StartAsync(IPAddress.Loopback.ToString(), port);
            await subscriber.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await publisher.ConnectAsync(IPAddress.Loopback.ToString(), port);

            subscriber.Subscribe<string>("demo.offline.disabled", _ => Interlocked.Increment(ref receiveCount));
            await WaitForTransportAsync();

            subscriber.Disconnect();
            await Task.Delay(2500);

            publisher.Publish("demo.offline.disabled", "m1", out _);
            publisher.Publish("demo.offline.disabled", "m2", out _);

            await subscriber.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await Task.Delay(500);

            Assert.Equal(0, Volatile.Read(ref receiveCount));
        }
        finally
        {
            publisher.Disconnect();
            subscriber.Disconnect();
            server.Stop();
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, int timeoutMilliseconds = 3000)
    {
        using var cancellationTokenSource = new CancellationTokenSource(timeoutMilliseconds);
        var completedTask = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cancellationTokenSource.Token));
        if (completedTask != task)
        {
            throw new TimeoutException($"Operation did not complete within {timeoutMilliseconds} ms.");
        }

        return await task;
    }

    // 订阅与发布通过网络同步需要短暂传播时间，统一等待片刻让测试更稳定。
    private static Task WaitForTransportAsync()
    {
        return Task.Delay(150);
    }
}
