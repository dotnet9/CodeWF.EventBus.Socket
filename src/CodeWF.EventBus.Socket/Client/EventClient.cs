using CodeWF.EventBus.Socket.Extensions;
using CodeWF.EventBus.Socket.Helpers;
using CodeWF.EventBus.Socket.Models.Responses;
using System.Linq;

// ReSharper disable once CheckNamespace
namespace CodeWF.EventBus.Socket;

public class EventClient : IEventClient
{
    private System.Net.Sockets.Socket? _client;
    private CancellationTokenSource? _cancellationTokenSource;

    private readonly ConcurrentDictionary<string, List<Delegate>> _subjectAndHandlers =
        new ConcurrentDictionary<string, List<Delegate>>();

    private readonly BlockingCollection<SocketCommand> _responses = new(new ConcurrentQueue<SocketCommand>());

    private int? _requestIsEventServerTaskId;

    #region interface methods

    public bool Connect(string host, int port, out string message)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        var ipEndPoint = new IPEndPoint(IPAddress.Parse(host), port);
        message = string.Empty;

        try
        {
            _client = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream,
                ProtocolType.Tcp);
            _client.Connect(ipEndPoint);

            ListenForServer();
            CheckResponse();
            return CheckIsEventServer(out message);
        }
        catch (SocketException ex)
        {
            Debug.Write($"TCP service connection exception, will reconnect in 3 seconds：{ex.Message}");
            Thread.Sleep(TimeSpan.FromMilliseconds(30000));
        }
        catch (Exception ex)
        {
            throw ex;
        }

        return false;
    }

    public void Disconnect()
    {
        _cancellationTokenSource?.Cancel();
        _client?.Disconnect(false);
        _client = null;
    }

    public void Subscribe<T>(string subject, Action<T> eventHandler)
    {
        if (!_subjectAndHandlers.TryGetValue(subject, out var handlers))
        {
            handlers = new List<Delegate>();
            _subjectAndHandlers.TryAdd(subject, handlers);
        }

        handlers.Add(eventHandler);
        SendCommand(new RequestSubscribe()
        {
            TaskId = SocketHelper.GetNewTaskId(),
            Subject = subject
        });
    }

    public void Subscribe<T>(string subject, Func<T, Task> asyncEventHandler)
    {
        if (!_subjectAndHandlers.TryGetValue(subject, out var handlers))
        {
            handlers = new List<Delegate>();
            _subjectAndHandlers.TryAdd(subject, handlers);
        }

        handlers.Add(asyncEventHandler);
        SendCommand(new RequestSubscribe()
        {
            TaskId = SocketHelper.GetNewTaskId(),
            Subject = subject
        });
    }

    public void Unsubscribe<T>(string subject, Action<T> eventHandler)
    {
        if (!_subjectAndHandlers.TryGetValue(subject, out var handlers)) return;
        handlers.Remove(eventHandler);
        SendCommand(new RequestUnsubscribe()
        {
            TaskId = SocketHelper.GetNewTaskId(),
            Subject = subject
        });
    }

    public void Unsubscribe<T>(string subject, Func<T, Task> asyncEventHandler)
    {
        if (!_subjectAndHandlers.TryGetValue(subject, out var handlers)) return;
        handlers.Remove(asyncEventHandler);
        SendCommand(new RequestUnsubscribe()
        {
            TaskId = SocketHelper.GetNewTaskId(),
            Subject = subject
        });
    }

    public void Publish<T>(string subject, T message)
    {
        SendCommand(new RequestPublish
        {
            TaskId = SocketHelper.GetNewTaskId(),
            Subject = subject,
            Message = message.GetString()
        });
    }

    #endregion

    #region private methods

    private void ListenForServer()
    {
        Task.Run(() =>
        {
            while (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
                try
                {
                    if (_client!.ReadPacket(out var buffer, out var headInfo))
                    {
                        _responses.Add(new SocketCommand(headInfo, buffer, _client));
                    }
                }
                catch (SocketException ex)
                {
                    Debug.Write($"Receive data exception：{ex.Message}");
                    break;
                }
                catch (Exception ex)
                {
                    Debug.Write($"Receive data exception：{ex.Message}");
                }

            return Task.CompletedTask;
        });
    }

    private void CheckResponse()
    {
        Task.Run(async () =>
        {
            while (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                if (_responses.TryTake(out var response, TimeSpan.FromMilliseconds(10)))
                {
                    if (response.IsMessage<ResponseCommon>())
                    {
                        HandleResponse(response.Message<ResponseCommon>());
                    }
                    else if (response.IsMessage<UpdateEvent>())
                    {
                        HandleResponse(response.Message<UpdateEvent>());
                    }
                }
            }
        });
    }

    private bool CheckIsEventServer(out string message)
    {
        message = String.Empty;
        _requestIsEventServerTaskId = SocketHelper.GetNewTaskId();
        SendCommand(new RequestIsEventServer() { TaskId = _requestIsEventServerTaskId.Value });

        if (!ActionHelper.CheckOvertime(() =>
            {
                while (_cancellationTokenSource is { IsCancellationRequested: false } &&
                       _requestIsEventServerTaskId != null)
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(10));
                }
            }))
        {
            _cancellationTokenSource?.Cancel();
            message = "Please check if the event bus service is connected correctly";
            return false;
        }

        return true;
    }

    private void HandleResponse(ResponseCommon response)
    {
        if (_requestIsEventServerTaskId == response.TaskId)
        {
            _requestIsEventServerTaskId = null;
        }
    }

    private void HandleResponse(UpdateEvent response)
    {
        if (!_subjectAndHandlers.TryGetValue(response.Subject, out var handlers)) return;
        foreach (var handler in handlers)
        {
            try
            {
                var param1 = handler.Method.GetParameters().First();
                var param1Type = param1.ParameterType;
                var param1Value = response.Message?.GetInstance(param1Type);
                if (handler.Method.ReturnType == typeof(Task))
                {
                    ((Task)handler.DynamicInvoke(param1Value)).GetAwaiter().GetResult();
                }
                else
                {
                    handler.DynamicInvoke(param1Value);
                }
            }
            catch (Exception ex)
            {
                Debug.Write($"Send data exception：{ex.Message}");
            }
        }
    }

    private void SendCommand(INetObject command)
    {
        var buffer = command.Serialize(DateTime.Now.ToFileTimeUtc());
        _client?.Send(buffer);
    }

    #endregion
}