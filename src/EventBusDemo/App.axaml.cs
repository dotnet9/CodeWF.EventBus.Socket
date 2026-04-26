using Avalonia;
using Avalonia.Markup.Xaml;
using DryIoc;
using EventBusDemo.Models;
using EventBusDemo.Services;
using EventBusDemo.Views;
using Prism.DryIoc;
using Prism.Ioc;
using System;
using System.Linq;

namespace EventBusDemo;

public class App : PrismApplication
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize();
    }

    protected override AvaloniaObject CreateShell()
    {
        // 示例：
        // -type server -address "127.0.0.1:3253"
        // -type client -address "127.0.0.1:3253"
        var args = Program.Args?.ToList();
        if (args == null || args.Count < 4)
        {
            return Container.Resolve<EventManagerView>();
        }

        var typeIndex = args.IndexOf("-type");
        var addressIndex = args.IndexOf("-address");
        if (typeIndex < 0 || addressIndex < 0 || typeIndex + 1 >= args.Count || addressIndex + 1 >= args.Count)
        {
            return Container.Resolve<EventManagerView>();
        }

        var windowType = (WindowType)Enum.Parse(typeof(WindowType), args[typeIndex + 1], true);
        var address = args[addressIndex + 1];
        if (!EventBusAddressParser.TryParse(address, out _, out _))
        {
            return Container.Resolve<EventManagerView>();
        }

        var configService = Container.Resolve<ApplicationConfig>();
        configService.SetHost(address);

        return windowType == WindowType.Server
            ? Container.Resolve<EventServerView>()
            : Container.Resolve<EventClientView>();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<ApplicationConfig>();
        containerRegistry.Register<EventManagerView>();
        containerRegistry.Register<EventServerView>();
        containerRegistry.Register<EventClientView>();
    }
}
