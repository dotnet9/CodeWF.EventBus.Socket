using Avalonia;
using Avalonia.ReactiveUI;
using System;

namespace EventBusDemo
{
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
            Args = args;
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace()
                .UseReactiveUI();
    }
}