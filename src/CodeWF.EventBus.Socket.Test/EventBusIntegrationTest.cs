using System.Net;
using System.Net.Sockets;

namespace CodeWF.EventBus.Socket.Test;

public class EventBusIntegrationTest
{
    [Fact]
    public async Task Publish_ShouldReachSubscribedClient()
    {
        var port = GetAvailablePort();
        var server = new EventServer();
        var publisher = new EventClient();
        var subscriber = new EventClient();
        var received = new TaskCompletionSource<string>();

        try
        {
            await server.StartAsync(IPAddress.Loopback.ToString(), port);
            await subscriber.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await publisher.ConnectAsync(IPAddress.Loopback.ToString(), port);

            // 先订阅再发布，验证最基础的发布订阅链路。
            subscriber.Subscribe<string>("event.integration.publish", message => received.TrySetResult(message));
            await Task.Delay(150);

            var published = publisher.Publish("event.integration.publish", "hello-event-bus", out var errorMessage);

            Assert.True(published);
            Assert.True(string.IsNullOrWhiteSpace(errorMessage));
            Assert.Equal("hello-event-bus", await WaitAsync(received.Task));
        }
        finally
        {
            publisher.Disconnect();
            subscriber.Disconnect();
            server.Stop();
        }
    }

    [Fact]
    public async Task Query_ShouldReturnResponderPayload()
    {
        var port = GetAvailablePort();
        var server = new EventServer();
        var requester = new EventClient();
        var responder = new EventClient();

        try
        {
            await server.StartAsync(IPAddress.Loopback.ToString(), port);
            await responder.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await requester.ConnectAsync(IPAddress.Loopback.ToString(), port);

            // 响应端直接对同主题 Publish，客户端会自动补上 QueryTaskId。
            responder.Subscribe<string>("event.integration.query",
                request => responder.Publish("event.integration.query", $"{request}-response", out _));
            await Task.Delay(150);

            var result = await requester.QueryAsync<string, string>("event.integration.query", "ping", 3000);

            Assert.True(string.IsNullOrWhiteSpace(result.ErrorMessage));
            Assert.Equal("ping-response", result.Result);
        }
        finally
        {
            requester.Disconnect();
            responder.Disconnect();
            server.Stop();
        }
    }

    [Fact]
    public async Task ConcurrentQueries_OnSameSubject_ShouldNotOverwriteEachOther()
    {
        var port = GetAvailablePort();
        var server = new EventServer();
        var requester = new EventClient();
        var responder = new EventClient();

        try
        {
            await server.StartAsync(IPAddress.Loopback.ToString(), port);
            await responder.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await requester.ConnectAsync(IPAddress.Loopback.ToString(), port);

            // 同一主题并发查询是这次修复的核心场景，用不同延迟制造响应乱序。
            responder.Subscribe<string>("event.integration.concurrent-query", async request =>
            {
                await Task.Delay(request == "first" ? 80 : 10);
                responder.Publish("event.integration.concurrent-query", $"{request}-done", out _);
            });
            await Task.Delay(150);

            var firstTask = requester.QueryAsync<string, string>("event.integration.concurrent-query", "first", 3000);
            var secondTask = requester.QueryAsync<string, string>("event.integration.concurrent-query", "second", 3000);

            var results = await Task.WhenAll(firstTask, secondTask);

            Assert.All(results, result => Assert.True(string.IsNullOrWhiteSpace(result.ErrorMessage)));
            Assert.Contains(results, result => result.Result == "first-done");
            Assert.Contains(results, result => result.Result == "second-done");
        }
        finally
        {
            requester.Disconnect();
            responder.Disconnect();
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
}
