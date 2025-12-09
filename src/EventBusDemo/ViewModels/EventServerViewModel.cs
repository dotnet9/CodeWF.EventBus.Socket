using CodeWF.EventBus.Socket;
using CodeWF.Log.Core;
using EventBusDemo.Services;
using System;
using System.Threading.Tasks;

namespace EventBusDemo.ViewModels;

public class EventServerViewModel : ViewModelBase
{
    private IEventServer? _eventServer;

    public EventServerViewModel(ApplicationConfig config)
    {
        Address = config.GetHost();
        Title = "EventBus Server";
    }

    public async Task RunServer()
    {
        if (_eventServer?.ConnectStatus == ConnectStatus.Connected)
        {
            Logger.Info("The event service has been started!");
            return;
        }

        _eventServer ??= new EventServer();
        var addressArray = Address!.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
        await _eventServer.StartAsync(addressArray[0], int.Parse(addressArray[1]));
        Logger.Info("The event service has been activated");
    }

    public async Task Stop()
    {
        _eventServer?.Stop();
        _eventServer = null;
        Logger.Warn("The event service has been stopped");
    }
}