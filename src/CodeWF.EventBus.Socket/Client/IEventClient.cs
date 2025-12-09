// ReSharper disable once CheckNamespace

namespace CodeWF.EventBus.Socket;

public interface IEventClient
{
    ConnectStatus ConnectStatus { get; }
    void Connect(string host, int port);
    Task<bool> ConnectAsync(string host, int port);
    void Disconnect();

    void Subscribe<T>(string subject, Action<T> eventHandler);
    void Subscribe<T>(string subject, Func<T, Task> asyncEventHandler);

    void Unsubscribe<T>(string subject, Action<T> eventHandler);
    void Unsubscribe<T>(string subject, Func<T, Task> asyncEventHandler);

    bool Publish<T>(string subject, T message, out string errorMessage);

    TResponse? Query<TQuery, TResponse>(string subject, TQuery message, out string errorMessage,
        int overtimeMilliseconds = 3000);

    Task<(TResponse? Result, string ErrorMessage)> QueryAsync<TQuery, TResponse>(string subject, TQuery message,
        int overtimeMilliseconds = 3000);
}

public enum ConnectStatus
{
    IsConnecting,
    Connected,
    Disconnected,
    DisconnectedNeedCheckEventServer
}