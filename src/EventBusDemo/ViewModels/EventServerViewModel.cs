using CodeWF.EventBus.Socket;
using ReactiveUI;
using System;
using System.Diagnostics;
using EventBusDemo.Services;

namespace EventBusDemo.ViewModels
{
    public class EventServerViewModel : ViewModelBase
    {
        private IEventServer? _eventServer;

        public EventServerViewModel(ApplicationConfig  config)
        {
            Address = config.GetHost();
            Title = $"EventBus Server, PID[{Process.GetCurrentProcess().Id}]";
        }

        public void RunServer()
        {
            if (_eventServer?.ConnectStatus == ConnectStatus.Connected)
            {
                AddLog("The event service has been started!");
                return;
            }
            _eventServer ??= new EventServer();
            var addressArray = Address!.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            _eventServer.Start(addressArray[0], int.Parse(addressArray[1]));
            AddLog("Event service has been activated");
        }

        public void Stop()
        {
            _eventServer?.Stop();
            _eventServer = null;
            AddLog("Event service has been stopped");
        }
    }
}