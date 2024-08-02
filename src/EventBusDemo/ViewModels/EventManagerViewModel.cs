using System;
using System.Diagnostics;
using Path = System.IO.Path;

namespace EventBusDemo.ViewModels
{
    public class EventManagerViewModel : ViewModelBase
    {
        public EventManagerViewModel()
        {
            Title = $"EventBus center, PID[{Process.GetCurrentProcess().Id}]";
            Address = "127.0.0.1:5329";
        }

        public void OpenEventServer()
        {
            OpenNewExe(isServer: true);
        }

        public void OpenEventClient()
        {
            OpenNewExe(isServer: false);
        }

        private void OpenNewExe(bool isServer)
        {
            var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EventBusDemo.exe");
            Process.Start(exePath, ["-type", isServer ? "server" : "client", "-address", Address]);
        }
    }
}