namespace CodeWF.EventBus.Socket.Server;

public interface IEventServer
{
    void Start(string? host = "127.0.0.l", int port = 5000);
    void Stop();
}