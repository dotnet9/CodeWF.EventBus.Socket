using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using EventBusDemo.Models;
using EventBusDemo.ViewModels;
using EventBusDemo.Views;
using System;
using System.Linq;

namespace EventBusDemo
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // empty
                // -type server -address "127.0.0.1:3253"
                // -type client -address "127.0.0.1:3253"
                var args = Program.Args?.ToList();
                if (args == null || args.Count < 4)
                {
                    desktop.MainWindow = new EventManagerView()
                    {
                        DataContext = new EventManagerViewModel()
                    };
                }
                else
                {
                    var typeIndex = args.IndexOf("-type");
                    var windowType = (WindowType)Enum.Parse(typeof(WindowType), args[typeIndex + 1], true);
                    var addressIndex = args.IndexOf("-address");
                    var address = args[addressIndex + 1];

                    if (windowType == WindowType.Server)
                    {
                        desktop.MainWindow = new EventServerView()
                        {
                            DataContext = new EventServerViewModel() { Address = address },
                        };
                    }
                    else
                    {
                        desktop.MainWindow = new EventClientView()
                        {
                            DataContext = new EventClientViewModel() { Address = address },
                        };
                    }
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}