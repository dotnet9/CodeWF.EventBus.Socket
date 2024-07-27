// ReSharper disable once CheckNamespace

namespace CodeWF.EventBus.Socket;

public interface IEventClient
{
    bool Connect(string host, int port, out string message);
    void Disconnect();

    void Subscribe<T>(string subject, Action<T> eventHandler);
    void Subscribe<T>(string subject, Func<T, Task> asyncEventHandler);

    void Unsubscribe<T>(string subject, Action<T> eventHandler);
    void Unsubscribe<T>(string subject, Func<T, Task> asyncEventHandler);

    void Publish<T>(string subject, T message);
}