using CodeWF.EventBus.Socket;
using CodeWF.Log.Core;
using EventBusDemo.Services;
using Prism.Commands;
using ReactiveUI;
using System;
using System.Threading.Tasks;

namespace EventBusDemo.ViewModels;

public class EventServerViewModel : ViewModelBase
{
    private IEventServer? _eventServer;
    private bool _isRunning;
    private bool _isStarting;
    private bool _isStopping;

    public EventServerViewModel(ApplicationConfig config)
    {
        Address = config.GetHost();
        Title = "EventBus服务器";
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            this.RaiseAndSetIfChanged(ref _isRunning, value);
            this.RaisePropertyChanged(nameof(CanStartServer));
            this.RaisePropertyChanged(nameof(CanStopServer));
        }
    }

    public bool IsStarting
    {
        get => _isStarting;
        set
        {
            this.RaiseAndSetIfChanged(ref _isStarting, value);
            this.RaisePropertyChanged(nameof(CanStartServer));
        }
    }

    public bool IsStopping
    {
        get => _isStopping;
        set
        {
            this.RaiseAndSetIfChanged(ref _isStopping, value);
            this.RaisePropertyChanged(nameof(CanStopServer));
        }
    }

    public bool CanStartServer
    {
        get => !IsRunning && !IsStarting;
    }

    public bool CanStopServer
    {
        get => IsRunning && !IsStopping;
    }

    public string ServerStatusText
    {
        get => IsRunning ? "运行中" : "已停止";
    }

    public string ServerStatusColor
    {
        get => IsRunning ? "#22c55e" : "#ef4444";
    }

    public DelegateCommand RunServerCommand => new DelegateCommand(async () => await RunServerAsync());
    public DelegateCommand StopCommand => new DelegateCommand(Stop);

    public async Task RunServerAsync()
    {
        if (_eventServer != null && IsRunning)
        {
            Logger.Info("事件服务已启动！");
            return;
        }

        IsStarting = true;
        this.RaisePropertyChanged("ServerStatusText");
        this.RaisePropertyChanged("ServerStatusColor");

        try
        {
            _eventServer ??= new EventServer();

            var addressArray = Address!.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            await _eventServer.StartAsync(addressArray[0], int.Parse(addressArray[1]));
            IsRunning = true;
            Logger.Info("事件服务已启动！");
        }
        catch (Exception ex)
        {
            Logger.Error($"启动事件服务失败：{ex.Message}");
        }
        finally
        {
            IsStarting = false;
            this.RaisePropertyChanged("ServerStatusText");
            this.RaisePropertyChanged("ServerStatusColor");
        }
    }



    public void Stop()
    {
        if (_eventServer == null || !IsRunning)
        {
            Logger.Info("事件服务未运行！");
            return;
        }

        IsStopping = true;
        this.RaisePropertyChanged("ServerStatusText");
        this.RaisePropertyChanged("ServerStatusColor");

        try
        {
            _eventServer.Stop();
            IsRunning = false;
            Logger.Info("事件服务已停止！");
        }
        catch (Exception ex)
        {
            Logger.Error($"停止事件服务失败：{ex.Message}");
        }
        finally
        {
            IsStopping = false;
            this.RaisePropertyChanged("ServerStatusText");
            this.RaisePropertyChanged("ServerStatusColor");
        }
    }
}