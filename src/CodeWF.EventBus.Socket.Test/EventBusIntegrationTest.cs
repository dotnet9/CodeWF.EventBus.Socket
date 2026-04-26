using System.Net;
using System.Net.Sockets;

namespace CodeWF.EventBus.Socket.Test;

public class EventBusIntegrationTest
{
    private sealed class OrderCreated
    {
        public string OrderNo { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
    }

    private sealed class LunchQuery
    {
        public string District { get; set; } = string.Empty;
        public bool PreferSoup { get; set; }
    }

    private sealed class LunchSuggestion
    {
        public string Restaurant { get; set; } = string.Empty;
        public string DishName { get; set; } = string.Empty;
    }

    [Fact]
    public async Task Publish_ShouldReachSubscribedClient()
    {
        var port = GetAvailablePort();
        var server = new EventServer();
        var publisher = new EventClient();
        var subscriber = new EventClient();
        var received = new TaskCompletionSource<OrderCreated>(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await server.StartAsync(IPAddress.Loopback.ToString(), port);
            await subscriber.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await publisher.ConnectAsync(IPAddress.Loopback.ToString(), port);

            // 先订阅“新订单通知”，再由发布方发送一条业务消息。
            subscriber.Subscribe<OrderCreated>("demo.order.created", message => received.TrySetResult(message));
            await WaitForTransportAsync();

            var published = publisher.Publish(
                "demo.order.created",
                new OrderCreated
                {
                    OrderNo = "SO-20260426-001",
                    CustomerName = "林小满"
                },
                out var errorMessage);

            Assert.True(published);
            Assert.True(string.IsNullOrWhiteSpace(errorMessage));

            var result = await WaitAsync(received.Task);
            Assert.Equal("SO-20260426-001", result.OrderNo);
            Assert.Equal("林小满", result.CustomerName);
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

            // 响应端根据午餐查询返回推荐菜单。
            responder.Subscribe<LunchQuery>("demo.lunch.recommendation", request =>
                responder.Publish(
                    "demo.lunch.recommendation",
                    new LunchSuggestion
                    {
                        Restaurant = request.District == "浦东" ? "江边食堂" : "园区小馆",
                        DishName = request.PreferSoup ? "番茄牛腩汤面" : "黑椒鸡排饭"
                    },
                    out _));
            await WaitForTransportAsync();

            var result = await requester.QueryAsync<LunchQuery, LunchSuggestion>(
                "demo.lunch.recommendation",
                new LunchQuery
                {
                    District = "浦东",
                    PreferSoup = true
                },
                3000);

            Assert.True(string.IsNullOrWhiteSpace(result.ErrorMessage));
            Assert.NotNull(result.Result);
            Assert.Equal("江边食堂", result.Result!.Restaurant);
            Assert.Equal("番茄牛腩汤面", result.Result.DishName);
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

            // 同一个主题下并发查询两份不同报表，确保响应不会串单。
            responder.Subscribe<string>("demo.report.summary", async request =>
            {
                await Task.Delay(request.Contains("周报", StringComparison.Ordinal) ? 80 : 10);
                responder.Publish("demo.report.summary", $"{request}-已生成", out _);
            });
            await WaitForTransportAsync();

            var weeklyReportTask = requester.QueryAsync<string, string>(
                "demo.report.summary",
                "华东销售周报",
                3000);
            var inventoryTask = requester.QueryAsync<string, string>(
                "demo.report.summary",
                "库存预警清单",
                3000);

            var results = await Task.WhenAll(weeklyReportTask, inventoryTask);

            Assert.All(results, result => Assert.True(string.IsNullOrWhiteSpace(result.ErrorMessage)));
            Assert.Contains(results, result => result.Result == "华东销售周报-已生成");
            Assert.Contains(results, result => result.Result == "库存预警清单-已生成");
        }
        finally
        {
            requester.Disconnect();
            responder.Disconnect();
            server.Stop();
        }
    }

    [Fact]
    public async Task Unsubscribe_ShouldStopReceivingPublishedEvents()
    {
        var port = GetAvailablePort();
        var server = new EventServer();
        var publisher = new EventClient();
        var subscriber = new EventClient();
        var receiveCount = 0;

        void Handler(string _)
        {
            Interlocked.Increment(ref receiveCount);
        }

        try
        {
            await server.StartAsync(IPAddress.Loopback.ToString(), port);
            await subscriber.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await publisher.ConnectAsync(IPAddress.Loopback.ToString(), port);

            subscriber.Subscribe<string>("demo.alert.inventory", Handler);
            await WaitForTransportAsync();
            subscriber.Unsubscribe<string>("demo.alert.inventory", Handler);
            await WaitForTransportAsync();

            publisher.Publish("demo.alert.inventory", "库存不足：龙井茶", out _);
            await Task.Delay(250);

            Assert.Equal(0, Volatile.Read(ref receiveCount));
        }
        finally
        {
            publisher.Disconnect();
            subscriber.Disconnect();
            server.Stop();
        }
    }

    [Fact]
    public async Task Query_ShouldReturnTimeout_WhenNoResponderExists()
    {
        var port = GetAvailablePort();
        var server = new EventServer();
        var requester = new EventClient();

        try
        {
            await server.StartAsync(IPAddress.Loopback.ToString(), port);
            await requester.ConnectAsync(IPAddress.Loopback.ToString(), port);

            var result = await requester.QueryAsync<string, string>(
                "demo.meeting.room.search",
                "帮我找一个 10 人会议室",
                200);

            Assert.Null(result.Result);
            Assert.Contains("超时", result.ErrorMessage, StringComparison.Ordinal);
        }
        finally
        {
            requester.Disconnect();
            server.Stop();
        }
    }

    [Fact]
    public async Task PublishBurst_ShouldReachSubscriberInOrder()
    {
        var port = GetAvailablePort();
        var server = new EventServer();
        var publisher = new EventClient();
        var subscriber = new EventClient();
        var received = new List<string>();
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        const int messageCount = 20;

        try
        {
            await server.StartAsync(IPAddress.Loopback.ToString(), port);
            await subscriber.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await publisher.ConnectAsync(IPAddress.Loopback.ToString(), port);

            subscriber.Subscribe<string>("demo.kitchen.ticket", value =>
            {
                lock (received)
                {
                    received.Add(value);
                    if (received.Count == messageCount)
                    {
                        completed.TrySetResult(true);
                    }
                }
            });
            await WaitForTransportAsync();

            for (var i = 0; i < messageCount; i++)
            {
                var ticketNo = $"TICKET-{i:00}";
                Assert.True(publisher.Publish("demo.kitchen.ticket", ticketNo, out var errorMessage), errorMessage);
            }

            await WaitAsync(completed.Task);

            lock (received)
            {
                Assert.Equal(Enumerable.Range(0, messageCount).Select(x => $"TICKET-{x:00}"), received);
            }
        }
        finally
        {
            publisher.Disconnect();
            subscriber.Disconnect();
            server.Stop();
        }
    }

    [Fact]
    public async Task QueryResponse_ShouldReturnOnlyToRequester_NotBroadcastToOtherSubscribers()
    {
        var port = GetAvailablePort();
        var server = new EventServer();
        var requester = new EventClient();
        var responder = new EventClient();
        var observer = new EventClient();
        var observerMessages = new List<string>();

        try
        {
            await server.StartAsync(IPAddress.Loopback.ToString(), port);
            await responder.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await observer.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await requester.ConnectAsync(IPAddress.Loopback.ToString(), port);

            responder.Subscribe<string>("demo.coffee.recommendation",
                request => responder.Publish("demo.coffee.recommendation", $"{request}：推荐燕麦拿铁", out _));
            observer.Subscribe<string>("demo.coffee.recommendation", message =>
            {
                lock (observerMessages)
                {
                    observerMessages.Add(message);
                }
            });
            await WaitForTransportAsync();

            var result = await requester.QueryAsync<string, string>(
                "demo.coffee.recommendation",
                "下午开会喝什么",
                3000);

            Assert.True(string.IsNullOrWhiteSpace(result.ErrorMessage));
            Assert.Equal("下午开会喝什么：推荐燕麦拿铁", result.Result);
            await Task.Delay(150);

            lock (observerMessages)
            {
                Assert.Contains("下午开会喝什么", observerMessages);
                Assert.DoesNotContain("下午开会喝什么：推荐燕麦拿铁", observerMessages);
            }
        }
        finally
        {
            requester.Disconnect();
            responder.Disconnect();
            observer.Disconnect();
            server.Stop();
        }
    }

    [Fact]
    public async Task QueryRequesterSubscribedToSameSubject_ShouldStillReceiveQueryRequest()
    {
        var port = GetAvailablePort();
        var server = new EventServer();
        var requester = new EventClient();
        var responder = new EventClient();
        var requesterSubscriptionReceived = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            await server.StartAsync(IPAddress.Loopback.ToString(), port);
            await requester.ConnectAsync(IPAddress.Loopback.ToString(), port);
            await responder.ConnectAsync(IPAddress.Loopback.ToString(), port);

            requester.Subscribe<string>("demo.travel.plan", request =>
                requesterSubscriptionReceived.TrySetResult(request));
            responder.Subscribe<string>("demo.travel.plan", async request =>
            {
                await Task.Delay(50);
                responder.Publish("demo.travel.plan", $"{request}：建议高铁出行", out _);
            });
            await WaitForTransportAsync();

            var queryTask = requester.QueryAsync<string, string>(
                "demo.travel.plan",
                "周末去苏州怎么玩",
                3000);

            Assert.Equal("周末去苏州怎么玩", await WaitAsync(requesterSubscriptionReceived.Task));

            var result = await queryTask;
            Assert.True(string.IsNullOrWhiteSpace(result.ErrorMessage));
            Assert.Equal("周末去苏州怎么玩：建议高铁出行", result.Result);
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

    // 订阅与发布通过网络同步需要短暂传播时间，统一等待片刻让测试更稳定。
    private static Task WaitForTransportAsync()
    {
        return Task.Delay(150);
    }
}
