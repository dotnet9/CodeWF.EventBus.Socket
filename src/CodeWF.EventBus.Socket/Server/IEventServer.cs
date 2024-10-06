// ReSharper disable once CheckNamespace

namespace CodeWF.EventBus.Socket;

public interface IEventServer
{
    ConnectStatus ConnectStatus { get; }
    void Start(string? host = "127.0.0.l", int port = 5000);
    Task StartAsync(string? host = "127.0.0.l", int port = 5000, CancellationTokenSource cancellationToken = default);
    void Stop();
}