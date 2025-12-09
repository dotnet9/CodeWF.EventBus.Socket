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

    public EventClientViewModel(ApplicationConfig config)
    {
        Address = config.GetHost();
        Title = "EventBus Client";
    }

    public bool IsSubscribeSendEmailCommand
    {
        get ;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSubscribeUpdateTimeCommand
    {
        get ;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSubscribeEmailQuery
    {
        get ;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public bool IsSubscribeTimeQuery
    {
        get ;
        set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public async Task ConnectServerAsync()
    {
        if (_eventClient?.ConnectStatus == ConnectStatus.Connected)
        {
            Logger.Info("The event service has been connected!");
            return;
        }

        _eventClient ??= new EventClient();
        var addressArray = Address!.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
        await _eventClient.ConnectAsync(addressArray[0], int.Parse(addressArray[1]));
        Logger.Info(
            "Connecting to event service, please retrieve the connection status through ConnectStatus later!");
    }

    public async Task DisconnectAsync()
    {
        _eventClient?.Disconnect();
        _eventClient = null;
        Logger.Warn("Disconnected from event service");
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

        if (_eventClient!.Publish(EventNames.SendEmailCommand,
                EmailManager.GenerateRandomNewEmailNotification(),
                out var errorMessage))
            Logger.Info($"Publish {EventNames.SendEmailCommand}");
        else
            Logger.Error(
                $"Publish {EventNames.SendEmailCommand} failed: [{errorMessage}]");
    }

    public async Task PublishUpdateTimeCommand()
    {
        if (!CheckIfEventConnected(true)) return;

        if (_eventClient!.Publish(EventNames.UpdateTimeCommand,
                DateTimeOffset.Now.ToUnixTimeSeconds(),
                out var errorMessage))
            Logger.Info($"Publish {EventNames.UpdateTimeCommand}");
        else
            Logger.Error(
                $"Publish {EventNames.UpdateTimeCommand} failed: [{errorMessage}]");
    }

    public async Task QueryEmailQuery()
    {
        if (!CheckIfEventConnected(true)) return;

        try
        {
            var result = await _eventClient!.QueryAsync<EmailQuery, EmailQueryResponse>(EventNames.EmailQuery,
                new EmailQuery { Subject = "Account" }, 3000);
            if (string.IsNullOrWhiteSpace(result.ErrorMessage) && result.Result != null)
                Logger.Info($"Query {EventNames.EmailQuery}, result: {result.Result}");
            else
                Logger.Error(
                    $"Query {EventNames.EmailQuery} failed: [{result.ErrorMessage}]");
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"Query {EventNames.EmailQuery} failed: [{ex.Message}]");
        }
    }

    public async Task QueryTimeQuery()
    {
        if (!CheckIfEventConnected(true)) return;

        try
        {
            var result =
                await _eventClient!.QueryAsync<string, string>(EventNames.TimeQuery, "I need new time", 3000);
            if (string.IsNullOrWhiteSpace(result.ErrorMessage) && result.Result != null)
                Logger.Info($"Query {EventNames.TimeQuery}, result: {result.Result}");
            else
                Logger.Error(
                    $"Query {EventNames.TimeQuery} failed: [{result.ErrorMessage}]");
        }
        catch (Exception ex)
        {
            Logger.Error(
                $"Query {EventNames.TimeQuery} failed: [{ex.Message}]");
        }
    }


    private void ReceiveNewEmailCommand(NewEmailCommand command)
    {
        Logger.Info($"Received {EventNames.SendEmailCommand} is [{command}]");
    }

    private void ReceiveUpdateTimeCommand(long command)
    {
        Logger.Info($"Received {EventNames.UpdateTimeCommand} is [{command}]");
    }

    private void ReceiveEmailQuery(EmailQuery request)
    {
        Logger.Info($"Received query request [{EventNames.EmailQuery}]: [{request}]");
        var response = new EmailQueryResponse { Emails = EmailManager.QueryEmail(request.Subject) };
        if (_eventClient!.Publish(EventNames.EmailQuery, response,
                out var errorMessage))
            Logger.Info($"Response query result: {response}");
        else
            Logger.Error($"Response query failed: {errorMessage}");
    }

    private void ReceiveTimeQuery(string request)
    {
        Logger.Info($"Received query request [{EventNames.TimeQuery}]: [{request}]");
        var response = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss fff");
        if (_eventClient!.Publish(EventNames.TimeQuery, response,
                out var errorMessage))
            Logger.Info($"Response query result: {response}");
        else
            Logger.Error($"Response query failed: {errorMessage}");
    }

    private bool CheckIfEventConnected(bool showMsg = false)
    {
        if (_eventClient is { ConnectStatus: ConnectStatus.Connected }) return true;
        if (showMsg) Logger.Warn("Please connect to the event service before sending the event");

        return false;
    }
}