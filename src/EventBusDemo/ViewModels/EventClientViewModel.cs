using CodeWF.EventBus.Socket;
using CodeWF.Log.Core;
using EventBusDemo.Commands;
using EventBusDemo.Models;
using EventBusDemo.Queries;
using EventBusDemo.Services;
using Prism.Commands;
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

    public bool CanConnect
    {
        get => !IsConnecting;
    }

    public bool CanDisconnect
    {
        get => !IsConnecting;
    }

    public string ConnectStatusText
    {
        get
        {
            return _connectStatus switch
            {
                ConnectStatus.Connected => "已连接",
                ConnectStatus.IsConnecting => "连接中",
                _ => "未连接"
            };
        }
    }

    public string ConnectStatusColor
    {
        get
        {
            return _connectStatus switch
            {
                ConnectStatus.Connected => "Green",
                ConnectStatus.IsConnecting => "Yellow",
                _ => "Red"
            };
        }
    }



    public bool IsSubscribeSendEmailCommand
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSubscribeUpdateTimeCommand
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSubscribeEmailQuery
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSubscribeTimeQuery
    {
        get;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }



    public async Task ConnectServerAsync()
    {
        if (_eventClient?.ConnectStatus == ConnectStatus.Connected)
        {
            Logger.Info("事件服务已连接！");
            return;
        }

        IsConnecting = true;
        ConnectStatus = ConnectStatus.IsConnecting;
        this.RaisePropertyChanged(nameof(ConnectStatusText));
        this.RaisePropertyChanged(nameof(ConnectStatusColor));

        _eventClient ??= new EventClient();
        try
        {
            var addressArray = Address!.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            await _eventClient.ConnectAsync(addressArray[0], int.Parse(addressArray[1]));
            ConnectStatus = ConnectStatus.Connected;
            Logger.Info("事件服务已连接！");
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

    public async Task DisconnectAsync()
    {
        if (_eventClient == null)
        {
            ConnectStatus = ConnectStatus.Disconnected;
            this.RaisePropertyChanged(nameof(ConnectStatusText));
            this.RaisePropertyChanged(nameof(ConnectStatusColor));
            return;
        }

        this.RaisePropertyChanged(nameof(ConnectStatusText));
        this.RaisePropertyChanged(nameof(ConnectStatusColor));

        try
        {
            _eventClient.Disconnect();
            _eventClient = null;
            ConnectStatus = ConnectStatus.Disconnected;
            Logger.Warn("已断开与事件服务的连接");
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
    }

    public async Task SubscribeOrUnsubscribeSendEmailCommand()
    {
        if (!CheckIfEventConnected(true)) return;

        if (!IsSubscribeSendEmailCommand)
            _eventClient?.Unsubscribe<NewEmailCommand>(EventNames.SendEmailCommand, ReceiveNewEmailCommand);
        else
            _eventClient?.Subscribe<NewEmailCommand>(EventNames.SendEmailCommand, ReceiveNewEmailCommand);
    }

    public async Task SubscribeOrUnsubscribeUpdateTimeCommand()
    {
        if (!CheckIfEventConnected(true)) return;

        if (!IsSubscribeUpdateTimeCommand)
            _eventClient?.Unsubscribe<long>(EventNames.UpdateTimeCommand, ReceiveUpdateTimeCommand);
        else
            _eventClient?.Subscribe<long>(EventNames.UpdateTimeCommand, ReceiveUpdateTimeCommand);
    }

    public async Task SubscribeOrUnsubscribeEmailQuery()
    {
        if (!CheckIfEventConnected(true)) return;

        if (!IsSubscribeEmailQuery)
            _eventClient?.Unsubscribe<EmailQuery>(EventNames.EmailQuery, ReceiveEmailQuery);
        else
            _eventClient?.Subscribe<EmailQuery>(EventNames.EmailQuery, ReceiveEmailQuery);
    }

    public async Task SubscribeOrUnsubscribeTimeQuery()
    {
        if (!CheckIfEventConnected(true)) return;

        if (!IsSubscribeTimeQuery)
            _eventClient?.Unsubscribe<string>(EventNames.TimeQuery, ReceiveTimeQuery);
        else
            _eventClient?.Subscribe<string>(EventNames.TimeQuery, ReceiveTimeQuery);
    }

    public async Task PublishNewEmailCommand()
    {
        if (!CheckIfEventConnected(true)) return;

        var emailCommand = EmailManager.GenerateRandomNewEmailNotification();
        if (_eventClient!.Publish(EventNames.SendEmailCommand,
                emailCommand,
                out var errorMessage))
        {
            Logger.Info($"发布 {EventNames.SendEmailCommand}: {emailCommand}");
        }
        else
        {
            Logger.Error(
                $"发布 {EventNames.SendEmailCommand} 失败: [{errorMessage}]");
        }
    }

    public async Task PublishUpdateTimeCommand()
    {
        if (!CheckIfEventConnected(true)) return;

        var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
        if (_eventClient!.Publish(EventNames.UpdateTimeCommand,
                timestamp,
                out var errorMessage))
        {
            Logger.Info($"发布 {EventNames.UpdateTimeCommand}: {timestamp}");
        }
        else
        {
            Logger.Error(
                $"发布 {EventNames.UpdateTimeCommand} 失败: [{errorMessage}]");
        }
    }

    public async Task QueryEmailQuery()
    {
        if (!CheckIfEventConnected(true)) return;

        try
        {
            var query = new EmailQuery { Subject = "账户" };
            var result = await _eventClient!.QueryAsync<EmailQuery, EmailQueryResponse>(EventNames.EmailQuery,
                query, 3000);
            if (string.IsNullOrWhiteSpace(result.ErrorMessage) && result.Result != null)
            {
                Logger.Info($"查询 {EventNames.EmailQuery} 结果: {result.Result}");
            }
            else
            {
                Logger.Error(
                    $"查询 {EventNames.EmailQuery} 失败: [{result.ErrorMessage}]");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"查询 {EventNames.EmailQuery} 失败: [{ex.Message}]");
        }
    }

    public async Task QueryTimeQuery()
    {
        if (!CheckIfEventConnected(true)) return;

        try
        {
            var query = "我需要新时间";
            var result = await _eventClient!.QueryAsync<string, string>(EventNames.TimeQuery, query, 3000);
            if (string.IsNullOrWhiteSpace(result.ErrorMessage) && result.Result != null)
            {
                Logger.Info($"查询 {EventNames.TimeQuery} 结果: {result.Result}");
            }
            else
            {
                Logger.Error(
                    $"查询 {EventNames.TimeQuery} 失败: [{result.ErrorMessage}]");
            }
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"查询 {EventNames.TimeQuery} 失败: [{ex.Message}]");
        }
    }


    private void ReceiveNewEmailCommand(NewEmailCommand command)
    {
        Logger.Info($"收到 {EventNames.SendEmailCommand}: {command}");
    }

    private void ReceiveUpdateTimeCommand(long command)
    {
        Logger.Info($"收到 {EventNames.UpdateTimeCommand}: {command}");
    }

    private void ReceiveEmailQuery(EmailQuery request)
    {
        Logger.Info($"收到查询请求 [{EventNames.EmailQuery}]: {request}");
        var response = new EmailQueryResponse { Emails = EmailManager.QueryEmail(request.Subject) };
        if (_eventClient!.Publish(EventNames.EmailQuery, response,
                out var errorMessage))
        {
            Logger.Info($"响应查询结果 [{EventNames.EmailQuery}]: {response}");
        }
        else
        {
            Logger.Error($"响应查询失败 [{EventNames.EmailQuery}]: {errorMessage}");
        }
    }

    private void ReceiveTimeQuery(string request)
    {
        Logger.Info($"收到查询请求 [{EventNames.TimeQuery}]: {request}");
        var response = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff");
        if (_eventClient!.Publish(EventNames.TimeQuery, response,
                out var errorMessage))
        {
            Logger.Info($"响应查询结果 [{EventNames.TimeQuery}]: {response}");
        }
        else
        {
            Logger.Error($"响应查询失败 [{EventNames.TimeQuery}]: {errorMessage}");
        }
    }

    private bool CheckIfEventConnected(bool showMsg = false)
    {
        if (_eventClient is { ConnectStatus: ConnectStatus.Connected }) return true;
        if (showMsg) Logger.Warn("发送事件前请先连接事件服务");

        return false;
    }
}