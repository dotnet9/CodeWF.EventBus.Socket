namespace CodeWF.EventBus.Socket.Client;

public class EventClient : IEventClient
{
    public bool ConnectServer(string host, int port)
    {
        throw new NotImplementedException();
    }

    public void Subscribe<T>(string subject, Action<T> eventHandler)
    {
        throw new NotImplementedException();
    }

    public void Unsubscribe<T>(string subject, Action<T> eventHandler)
    {
        throw new NotImplementedException();
    }

    public void Publish<T>(string subject, string message)
    {
        throw new NotImplementedException();
    }
}