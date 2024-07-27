using System;
using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls.Shapes;
using ReactiveUI;
using Path = System.IO.Path;

namespace EventBusDemo.ViewModels
{
    public class EventManagerViewModel : ViewModelBase
    {
        private string? _title;

        public string? Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        private string _address = "127.0.0.1:5329";

        public string Address
        {
            get => _address;
            set => this.RaiseAndSetIfChanged(ref _address, value);
        }

        public EventManagerViewModel()
        {
            Title = $"EventBus管理，进程Id：{Process.GetCurrentProcess().Id}";
        }

        public void OpenEventServer()
        {
            var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EventBusDemo.exe");
            Process.Start(exePath, ["-type", "server", "-address", Address]);
        }

        public void OpenEventClient()
        {
            var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EventBusDemo.exe");
            Process.Start(exePath, ["-type", "client", "-address", Address]);
        }
    }
}