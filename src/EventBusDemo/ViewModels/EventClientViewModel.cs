using CodeWF.EventBus.Socket;
using CodeWF.Log.Core;
using EventBusDemo.Commands;
using EventBusDemo.Models;
using EventBusDemo.Queries;
using EventBusDemo.Services;
using ReactiveUI;
using System;
using System.Threading.Tasks;

namespace EventBusDemo.ViewModels;

public class EventClientViewModel : ViewModelBase
{
    private IEventClient? _eventClient;
    private ConnectStatus _connectStatus = ConnectStatus.Disconnected;
    private bool _isConnecting;

    public EventClientViewModel(ApplicationConfig config)
    {
        Address = config.GetHost();
        Title = "EventBus客户端";
    }

    public ConnectStatus ConnectStatus
    {
        get => _connectStatus;
        set => this.RaiseAndSetIfChanged(ref _connectStatus, value);
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            this.RaiseAndSetIfChanged(ref _isConnecting, value);
            this.RaisePropertyChanged(nameof(CanConnect));
            this.RaisePropertyChanged(nameof(CanDisconnect));
        }
    }

    public bool CanConnect => !IsConnecting;

    public bool CanDisconnect => !IsConnecting;

    public string ConnectStatusText =>
        _connectStatus switch
        {
            ConnectStatus.Connected => "已连接",
            ConnectStatus.IsConnecting => "连接中",
            _ => "未连接"
        };

    public string ConnectStatusColor =>
        _connectStatus switch
        {
            ConnectStatus.Connected => "Green",
            ConnectStatus.IsConnecting => "Yellow",
            _ => "Red"
        };

    public bool IsSubscribeInventoryAlertCommand
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSubscribeStoreStatusCommand
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSubscribeSalesSnapshotQuery
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSubscribeServiceClockQuery
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public async Task ConnectServerAsync()
    {
        if (_eventClient?.ConnectStatus == ConnectStatus.Connected)
        {
            Logger.Info("事件服务已连接。");
            return;
        }

        IsConnecting = true;
        ConnectStatus = ConnectStatus.IsConnecting;
        this.RaisePropertyChanged(nameof(ConnectStatusText));
        this.RaisePropertyChanged(nameof(ConnectStatusColor));

        _eventClient ??= new EventClient();
        try
        {
            if (!EventBusAddressParser.TryParse(Address, out var host, out var port))
            {
                ConnectStatus = ConnectStatus.Disconnected;
                Logger.Warn("事件服务地址格式无效，请使用 host:port、[::1]:port 或 tcp://host:port，例如 127.0.0.1:5329");
                return;
            }

            var connected = await _eventClient.ConnectAsync(host, port);
            ConnectStatus = _eventClient.ConnectStatus;
            if (connected)
            {
                Logger.Info("事件服务连接成功。");
            }
            else
            {
                Logger.Warn("事件服务尚未完成连接，请检查服务状态后重试。");
            }
        }
        catch (Exception ex)
        {
            ConnectStatus = ConnectStatus.Disconnected;
            Logger.Error($"连接事件服务失败：{ex.Message}");
        }
        finally
        {
            IsConnecting = false;
            this.RaisePropertyChanged(nameof(ConnectStatusText));
            this.RaisePropertyChanged(nameof(ConnectStatusColor));
        }
    }

    public Task DisconnectAsync()
    {
        if (_eventClient == null)
        {
            ConnectStatus = ConnectStatus.Disconnected;
            this.RaisePropertyChanged(nameof(ConnectStatusText));
            this.RaisePropertyChanged(nameof(ConnectStatusColor));
            return Task.CompletedTask;
        }

        this.RaisePropertyChanged(nameof(ConnectStatusText));
        this.RaisePropertyChanged(nameof(ConnectStatusColor));

        try
        {
            _eventClient.Disconnect();
            _eventClient = null;
            ConnectStatus = ConnectStatus.Disconnected;
            Logger.Warn("已断开与事件服务的连接。");
        }
        catch (Exception ex)
        {
            Logger.Error($"断开连接失败：{ex.Message}");
        }
        finally
        {
            this.RaisePropertyChanged(nameof(ConnectStatusText));
            this.RaisePropertyChanged(nameof(ConnectStatusColor));
        }

        return Task.CompletedTask;
    }

    public Task SubscribeOrUnsubscribeInventoryAlertCommand()
    {
        if (!CheckIfEventConnected(true))
        {
            return Task.CompletedTask;
        }

        if (!IsSubscribeInventoryAlertCommand)
        {
            _eventClient?.Unsubscribe<InventoryAlertCommand>(
                EventNames.InventoryAlertCommand,
                ReceiveInventoryAlertCommand);
        }
        else
        {
            _eventClient?.Subscribe<InventoryAlertCommand>(
                EventNames.InventoryAlertCommand,
                ReceiveInventoryAlertCommand);
        }

        return Task.CompletedTask;
    }

    public Task SubscribeOrUnsubscribeStoreStatusCommand()
    {
        if (!CheckIfEventConnected(true))
        {
            return Task.CompletedTask;
        }

        if (!IsSubscribeStoreStatusCommand)
        {
            _eventClient?.Unsubscribe<StoreStatusCommand>(
                EventNames.StoreStatusCommand,
                ReceiveStoreStatusCommand);
        }
        else
        {
            _eventClient?.Subscribe<StoreStatusCommand>(
                EventNames.StoreStatusCommand,
                ReceiveStoreStatusCommand);
        }

        return Task.CompletedTask;
    }

    public Task SubscribeOrUnsubscribeSalesSnapshotQuery()
    {
        if (!CheckIfEventConnected(true))
        {
            return Task.CompletedTask;
        }

        if (!IsSubscribeSalesSnapshotQuery)
        {
            _eventClient?.Unsubscribe<SalesSnapshotQuery>(
                EventNames.SalesSnapshotQuery,
                ReceiveSalesSnapshotQuery);
        }
        else
        {
            _eventClient?.Subscribe<SalesSnapshotQuery>(
                EventNames.SalesSnapshotQuery,
                ReceiveSalesSnapshotQuery);
        }

        return Task.CompletedTask;
    }

    public Task SubscribeOrUnsubscribeServiceClockQuery()
    {
        if (!CheckIfEventConnected(true))
        {
            return Task.CompletedTask;
        }

        if (!IsSubscribeServiceClockQuery)
        {
            _eventClient?.Unsubscribe<ServiceClockQuery>(
                EventNames.ServiceClockQuery,
                ReceiveServiceClockQuery);
        }
        else
        {
            _eventClient?.Subscribe<ServiceClockQuery>(
                EventNames.ServiceClockQuery,
                ReceiveServiceClockQuery);
        }

        return Task.CompletedTask;
    }

    public Task PublishInventoryAlertCommand()
    {
        if (!CheckIfEventConnected(true))
        {
            return Task.CompletedTask;
        }

        var command = RetailOperationsScenarioFactory.GenerateInventoryAlert();
        if (_eventClient!.Publish(EventNames.InventoryAlertCommand, command, out var errorMessage))
        {
            Logger.Info($"发布 {EventNames.InventoryAlertCommand}：{command}");
        }
        else
        {
            Logger.Error($"发布 {EventNames.InventoryAlertCommand} 失败：[{errorMessage}]");
        }

        return Task.CompletedTask;
    }

    public Task PublishStoreStatusCommand()
    {
        if (!CheckIfEventConnected(true))
        {
            return Task.CompletedTask;
        }

        var command = RetailOperationsScenarioFactory.GenerateStoreStatus();
        if (_eventClient!.Publish(EventNames.StoreStatusCommand, command, out var errorMessage))
        {
            Logger.Info($"发布 {EventNames.StoreStatusCommand}：{command}");
        }
        else
        {
            Logger.Error($"发布 {EventNames.StoreStatusCommand} 失败：[{errorMessage}]");
        }

        return Task.CompletedTask;
    }

    public async Task QuerySalesSnapshot()
    {
        if (!CheckIfEventConnected(true))
        {
            return;
        }

        try
        {
            var query = new SalesSnapshotQuery
            {
                Region = "华东",
                BusinessDate = DateTime.Today.ToString("yyyy-MM-dd")
            };
            var result = await _eventClient!.QueryAsync<SalesSnapshotQuery, SalesSnapshotQueryResponse>(
                EventNames.SalesSnapshotQuery,
                query,
                3000);

            if (string.IsNullOrWhiteSpace(result.ErrorMessage) && result.Result != null)
            {
                Logger.Info($"查询 {EventNames.SalesSnapshotQuery} 结果：{result.Result}");
            }
            else
            {
                Logger.Error($"查询 {EventNames.SalesSnapshotQuery} 失败：[{result.ErrorMessage}]");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"查询 {EventNames.SalesSnapshotQuery} 失败：[{ex.Message}]");
        }
    }

    public async Task QueryServiceClock()
    {
        if (!CheckIfEventConnected(true))
        {
            return;
        }

        try
        {
            var query = new ServiceClockQuery
            {
                ServiceName = "StoreOpsGateway"
            };
            var result = await _eventClient!.QueryAsync<ServiceClockQuery, ServiceClockQueryResponse>(
                EventNames.ServiceClockQuery,
                query,
                3000);

            if (string.IsNullOrWhiteSpace(result.ErrorMessage) && result.Result != null)
            {
                Logger.Info($"查询 {EventNames.ServiceClockQuery} 结果：{result.Result}");
            }
            else
            {
                Logger.Error($"查询 {EventNames.ServiceClockQuery} 失败：[{result.ErrorMessage}]");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"查询 {EventNames.ServiceClockQuery} 失败：[{ex.Message}]");
        }
    }

    private void ReceiveInventoryAlertCommand(InventoryAlertCommand command)
    {
        Logger.Info($"收到 {EventNames.InventoryAlertCommand}：{command}");
    }

    private void ReceiveStoreStatusCommand(StoreStatusCommand command)
    {
        Logger.Info($"收到 {EventNames.StoreStatusCommand}：{command}");
    }

    private void ReceiveSalesSnapshotQuery(SalesSnapshotQuery request)
    {
        Logger.Info($"收到查询请求 [{EventNames.SalesSnapshotQuery}]：{request}");

        // 直接在查询处理器里 Publish，客户端会自动回填当前查询的 QueryTaskId。
        var response = RetailOperationsScenarioFactory.BuildSalesSnapshot(request);
        if (_eventClient!.Publish(EventNames.SalesSnapshotQuery, response, out var errorMessage))
        {
            Logger.Info($"响应查询结果 [{EventNames.SalesSnapshotQuery}]：{response}");
        }
        else
        {
            Logger.Error($"响应查询失败 [{EventNames.SalesSnapshotQuery}]：{errorMessage}");
        }
    }

    private void ReceiveServiceClockQuery(ServiceClockQuery request)
    {
        Logger.Info($"收到查询请求 [{EventNames.ServiceClockQuery}]：{request}");

        var response = RetailOperationsScenarioFactory.BuildServiceClock(request);
        if (_eventClient!.Publish(EventNames.ServiceClockQuery, response, out var errorMessage))
        {
            Logger.Info($"响应查询结果 [{EventNames.ServiceClockQuery}]：{response}");
        }
        else
        {
            Logger.Error($"响应查询失败 [{EventNames.ServiceClockQuery}]：{errorMessage}");
        }
    }

    private bool CheckIfEventConnected(bool showMsg = false)
    {
        if (_eventClient is { ConnectStatus: ConnectStatus.Connected })
        {
            return true;
        }

        if (showMsg)
        {
            Logger.Warn("发送事件前请先连接事件服务。");
        }

        return false;
    }
}
