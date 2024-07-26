namespace CodeWF.EventBus.Socket.Client;

public interface IEventClient
{
    bool ConnectServer(string host, int port);

    void Subscribe<T>(string subject, Action<T> eventHandler);

    void Unsubscribe<T>(string subject, Action<T> eventHandler);

    void Publish<T>(string subject, string message);
}