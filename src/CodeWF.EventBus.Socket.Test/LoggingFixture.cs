using CodeWF.Log.Core;

namespace CodeWF.EventBus.Socket.Test;

[CollectionDefinition(Name)]
public sealed class LoggingCollection : ICollectionFixture<LoggingFixture>
{
    public const string Name = "Logging";
}

public sealed class LoggingFixture : IAsyncLifetime
{
    public Task InitializeAsync()
    {
        Logger.Initialize(new LoggerOptions
        {
            EnableConsole = false,
            EnableEventFeed = false
        });
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Logger.ShutdownAsync();
}
