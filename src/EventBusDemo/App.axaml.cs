using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using DryIoc;
using EventBusDemo.Models;
using EventBusDemo.Services;
using EventBusDemo.Views;
using Prism.DryIoc;
using Prism.Ioc;
using Prism.Regions;
using System;
using System.Linq;

namespace EventBusDemo;

public class App : PrismApplication
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize(); // <-- Required
    }

    protected override void ConfigureRegionAdapterMappings(RegionAdapterMappings regionAdapterMappings)
    {
        regionAdapterMappings.RegisterMapping<ItemsControl, ItemsControlRegionAdapter>();
        regionAdapterMappings.RegisterMapping<ContentControl, ContentControlRegionAdapter>();
    }

    protected override AvaloniaObject CreateShell()
    {
        // empty
        // -type server -address "127.0.0.1:3253"
        // -type client -address "127.0.0.1:3253"
        var args = Program.Args?.ToList();

        if (args == null || args.Count < 4) return Container.Resolve<EventManagerView>();

        var typeIndex = args.IndexOf("-type");
        var windowType = (WindowType)Enum.Parse(typeof(WindowType), args[typeIndex + 1], true);
        var addressIndex = args.IndexOf("-address");
        var address = args[addressIndex + 1];

        var configService = Container.Resolve<ApplicationConfig>();
        configService.SetHost(address);

        if (windowType == WindowType.Server)
            return Container.Resolve<EventServerView>();
        return Container.Resolve<EventClientView>();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<ApplicationConfig>();
        containerRegistry.Register<EventManagerView>();
        containerRegistry.Register<EventServerView>();
        containerRegistry.Register<EventClientView>();
    }
}