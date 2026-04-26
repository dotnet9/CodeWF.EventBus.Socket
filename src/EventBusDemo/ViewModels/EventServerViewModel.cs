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
        Title = "EventBus服务端";
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

    public bool CanStartServer => !IsRunning && !IsStarting;

    public bool CanStopServer => IsRunning && !IsStopping;

    public string ServerStatusText => IsRunning ? "运行中" : "已停止";

    public string ServerStatusColor => IsRunning ? "#22c55e" : "#ef4444";

    public DelegateCommand RunServerCommand => new(async () => await RunServerAsync());
    public DelegateCommand StopCommand => new(Stop);

    public async Task RunServerAsync()
    {
        if (_eventServer != null && IsRunning)
        {
            Logger.Info("事件服务已启动。");
            return;
        }

        IsStarting = true;
        this.RaisePropertyChanged(nameof(ServerStatusText));
        this.RaisePropertyChanged(nameof(ServerStatusColor));

        try
        {
            _eventServer ??= new EventServer();

            if (!EventBusAddressParser.TryParse(Address, out var host, out var port))
            {
                Logger.Warn("事件服务地址格式无效，请使用 host:port、[::1]:port 或 tcp://host:port，例如 127.0.0.1:5329");
                return;
            }

            await _eventServer.StartAsync(host, port);
            IsRunning = _eventServer.ConnectStatus == ConnectStatus.Connected;
            if (IsRunning)
            {
                Logger.Info("事件服务启动成功。");
            }
            else
            {
                Logger.Warn("事件服务正在启动中，请稍后查看状态。");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"启动事件服务失败：{ex.Message}");
        }
        finally
        {
            IsStarting = false;
            this.RaisePropertyChanged(nameof(ServerStatusText));
            this.RaisePropertyChanged(nameof(ServerStatusColor));
        }
    }

    public void Stop()
    {
        if (_eventServer == null || !IsRunning)
        {
            Logger.Info("事件服务未运行。");
            return;
        }

        IsStopping = true;
        this.RaisePropertyChanged(nameof(ServerStatusText));
        this.RaisePropertyChanged(nameof(ServerStatusColor));

        try
        {
            _eventServer.Stop();
            IsRunning = false;
            Logger.Info("事件服务已停止。");
        }
        catch (Exception ex)
        {
            Logger.Error($"停止事件服务失败：{ex.Message}");
        }
        finally
        {
            IsStopping = false;
            this.RaisePropertyChanged(nameof(ServerStatusText));
            this.RaisePropertyChanged(nameof(ServerStatusColor));
        }
    }
}
