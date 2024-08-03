using System;
using CodeWF.EventBus.Socket;
using CodeWF.LogViewer.Avalonia.Log4Net;
using EventBusDemo.Commands;
using EventBusDemo.Models;
using EventBusDemo.Queries;
using EventBusDemo.Services;
using ReactiveUI;

namespace EventBusDemo.ViewModels;

public class EventClientViewModel : ViewModelBase
{
    private IEventClient? _eventClient;


    private bool _isSubscribeSendEmailCommand;
    private bool _isSubscribeUpdateTimeCommand;
    private bool _isSubscribeEmailQuery;
    private bool _isSubscribeTimeQuery;

    public EventClientViewModel(ApplicationConfig config)
    {
        Address = config.GetHost();
        Title = "EventBus Client";
    }

    public bool IsSubscribeSendEmailCommand
    {
        get => _isSubscribeSendEmailCommand;
        set => this.RaiseAndSetIfChanged(ref _isSubscribeSendEmailCommand, value);
    }

    public bool IsSubscribeUpdateTimeCommand
    {
        get => _isSubscribeUpdateTimeCommand;
        set => this.RaiseAndSetIfChanged(ref _isSubscribeUpdateTimeCommand, value);
    }

    public bool IsSubscribeEmailQuery
    {
        get => _isSubscribeEmailQuery;
        set => this.RaiseAndSetIfChanged(ref _isSubscribeEmailQuery, value);
    }

    public bool IsSubscribeTimeQuery
    {
        get => _isSubscribeTimeQuery;
        set => this.RaiseAndSetIfChanged(ref _isSubscribeTimeQuery, value);
    }

    public void ConnectServer()
    {
        if (_eventClient?.ConnectStatus == ConnectStatus.Connected)
        {
            LogFactory.Instance.Log.Info("The event service has been connected!");
            return;
        }

        _eventClient ??= new EventClient();
        var addressArray = Address!.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
        _eventClient.Connect(addressArray[0], int.Parse(addressArray[1]));
        LogFactory.Instance.Log.Info(
            "Connecting to event service, please retrieve the connection status through ConnectStatus later!");
    }

    public void Disconnect()
    {
        _eventClient?.Disconnect();
        _eventClient = null;
        LogFactory.Instance.Log.Warn("Disconnected from event service");
    }

    public void SubscribeOrUnsubscribeSendEmailCommand()
    {
        if (!CheckIfEventConnected(true)) return;

        if (!IsSubscribeSendEmailCommand)
            _eventClient?.Unsubscribe<NewEmailCommand>(EventNames.SendEmailCommand, ReceiveNewEmailCommand);
        else
            _eventClient?.Subscribe<NewEmailCommand>(EventNames.SendEmailCommand, ReceiveNewEmailCommand);
    }

    public void SubscribeOrUnsubscribeUpdateTimeCommand()
    {
        if (!CheckIfEventConnected(true)) return;

        if (!IsSubscribeUpdateTimeCommand)
            _eventClient?.Unsubscribe<long>(EventNames.UpdateTimeCommand, ReceiveUpdateTimeCommand);
        else
            _eventClient?.Subscribe<long>(EventNames.UpdateTimeCommand, ReceiveUpdateTimeCommand);
    }

    public void SubscribeOrUnsubscribeEmailQuery()
    {
        if (!CheckIfEventConnected(true)) return;

        if (!IsSubscribeEmailQuery)
            _eventClient?.Unsubscribe<EmailQuery>(EventNames.EmailQuery, ReceiveEmailQuery);
        else
            _eventClient?.Subscribe<EmailQuery>(EventNames.EmailQuery, ReceiveEmailQuery);
    }

    public void SubscribeOrUnsubscribeTimeQuery()
    {
        if (!CheckIfEventConnected(true)) return;

        if (!IsSubscribeTimeQuery)
            _eventClient?.Unsubscribe<string>(EventNames.TimeQuery, ReceiveTimeQuery);
        else
            _eventClient?.Subscribe<string>(EventNames.TimeQuery, ReceiveTimeQuery);
    }

    public void PublishNewEmailCommand()
    {
        if (!CheckIfEventConnected(true)) return;

        if (_eventClient!.Publish(EventNames.SendEmailCommand,
                EmailManager.GenerateRandomNewEmailNotification(),
                out var errorMessage))
            LogFactory.Instance.Log.Info($"Publish {EventNames.SendEmailCommand}");
        else
            LogFactory.Instance.Log.Error(
                $"Publish {EventNames.SendEmailCommand} failed: [{errorMessage}]");
    }

    public void PublishUpdateTimeCommand()
    {
        if (!CheckIfEventConnected(true)) return;

        if (_eventClient!.Publish(EventNames.UpdateTimeCommand,
                DateTimeOffset.Now.ToUnixTimeSeconds(),
                out var errorMessage))
            LogFactory.Instance.Log.Info($"Publish {EventNames.UpdateTimeCommand}");
        else
            LogFactory.Instance.Log.Error(
                $"Publish {EventNames.UpdateTimeCommand} failed: [{errorMessage}]");
    }

    public void QueryEmailQuery()
    {
        if (!CheckIfEventConnected(true)) return;

        var response = _eventClient!.Query<EmailQuery, EmailQueryResponse>(EventNames.EmailQuery,
            new EmailQuery { Subject = "Account" },
            out var errorMessage);
        if (string.IsNullOrWhiteSpace(errorMessage) && response != null)
            LogFactory.Instance.Log.Info($"Query {EventNames.EmailQuery}, result: {response}");
        else
            LogFactory.Instance.Log.Error(
                $"Query {EventNames.EmailQuery} failed: [{errorMessage}]");
    }

    public void QueryTimeQuery()
    {
        if (!CheckIfEventConnected(true)) return;

        var response =
            _eventClient!.Query<string, String>(EventNames.TimeQuery, "I need new time", out var errorMessage);
        if (string.IsNullOrWhiteSpace(errorMessage) && response != null)
            LogFactory.Instance.Log.Info($"Query {EventNames.TimeQuery}, result: {response}");
        else
            LogFactory.Instance.Log.Error(
                $"Query {EventNames.TimeQuery} failed: [{errorMessage}]");
    }


    private void ReceiveNewEmailCommand(NewEmailCommand command)
    {
        LogFactory.Instance.Log.Info($"Received {EventNames.SendEmailCommand} is [{command}]");
    }

    private void ReceiveUpdateTimeCommand(long command)
    {
        LogFactory.Instance.Log.Info($"Received {EventNames.UpdateTimeCommand} is [{command}]");
    }

    private void ReceiveEmailQuery(EmailQuery request)
    {
        LogFactory.Instance.Log.Info($"Received query request [{EventNames.EmailQuery}]: [{request}]");
        var response = new EmailQueryResponse { Emails = EmailManager.QueryEmail(request.Subject) };
        if (_eventClient!.Publish(EventNames.EmailQuery, response,
                out var errorMessage))
            LogFactory.Instance.Log.Info($"Response query result: {response}");
        else
            LogFactory.Instance.Log.Error($"Response query failed: {errorMessage}");
    }

    private void ReceiveTimeQuery(string request)
    {
        LogFactory.Instance.Log.Info($"Received query request [{EventNames.TimeQuery}]: [{request}]");
        var response = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff");
        if (_eventClient!.Publish(EventNames.TimeQuery, response,
                out var errorMessage))
            LogFactory.Instance.Log.Info($"Response query result: {response}");
        else
            LogFactory.Instance.Log.Error($"Response query failed: {errorMessage}");
    }

    private bool CheckIfEventConnected(bool showMsg = false)
    {
        if (_eventClient is { ConnectStatus: ConnectStatus.Connected }) return true;
        if (showMsg) LogFactory.Instance.Log.Warn("Please connect to the event service before sending the event");

        return false;
    }
}