using System;
using System.IO;
using Avalonia;
using CodeWF.Log.Core;
using ReactiveUI.Avalonia;

namespace EventBusDemo;

internal sealed class Program
{
    // empty
    // -type server -address "127.0.0.1:3253"
    // -type client -address "127.0.0.1:3253"
    public static string[]? Args { private set; get; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Logger.Initialize(new LoggerOptions
        {
            File = new FileLogOptions
            {
                DirectoryPath = Path.Combine(Environment.CurrentDirectory, "Log")
            }
        });

        try
        {
            Args = args;
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            Logger.ShutdownAsync().GetAwaiter().GetResult();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI(_ => { });
    }
}
