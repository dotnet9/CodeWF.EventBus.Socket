using System;
using CodeWF.EventBus.Socket;
using CodeWF.LogViewer.Avalonia.Log4Net;
using EventBusDemo.Services;

namespace EventBusDemo.ViewModels;

public class EventServerViewModel : ViewModelBase
{
    private IEventServer? _eventServer;

    public EventServerViewModel(ApplicationConfig config)
    {
        Address = config.GetHost();
        Title = "EventBus Server";
    }

    public void RunServer()
    {
        if (_eventServer?.ConnectStatus == ConnectStatus.Connected)
        {
            LogFactory.Instance.Log.Info("The event service has been started!");
            return;
        }

        _eventServer ??= new EventServer();
        var addressArray = Address!.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
        _eventServer.Start(addressArray[0], int.Parse(addressArray[1]));
        LogFactory.Instance.Log.Info("The event service has been activated");
    }

    public void Stop()
    {
        _eventServer?.Stop();
        _eventServer = null;
        LogFactory.Instance.Log.Warn("The event service has been stopped");
    }
}