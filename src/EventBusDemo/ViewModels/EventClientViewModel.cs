using CodeWF.EventBus.Socket;
using EventBusDemo.Models;
using EventBusDemo.Services;
using Prism.Events;
using ReactiveUI;
using System;
using System.Diagnostics;
using System.Runtime.Intrinsics.Arm;

namespace EventBusDemo.ViewModels
{
    public class EventClientViewModel : ViewModelBase
    {
        private IEventClient? _eventClient;

        public EventClientViewModel(ApplicationConfig config)
        {
            Address = config.GetHost();
            Title = $"EventBus Client, PID[{Process.GetCurrentProcess().Id}]";
        }


        private bool _isSubscribeSendEmailCommand;

        public bool IsSubscribeSendEmailCommand
        {
            get => _isSubscribeSendEmailCommand;
            set => this.RaiseAndSetIfChanged(ref _isSubscribeSendEmailCommand, value);
        }

        private bool _isSubscribeEmailQuery;

        public bool IsSubscribeEmailQuery
        {
            get => _isSubscribeEmailQuery;
            set => this.RaiseAndSetIfChanged(ref _isSubscribeEmailQuery, value);
        }

        public void ConnectServer()
        {
            if (_eventClient?.ConnectStatus == ConnectStatus.Connected)
            {
                AddLog("The event service has been connected!");
                return;
            }

            _eventClient ??= new EventClient();
            var addressArray = Address!.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            _eventClient.Connect(addressArray[0], int.Parse(addressArray[1]));
            AddLog("Connecting to event service, please retrieve the connection status through ConnectStatus later!");
        }

        public void Disconnect()
        {
            _eventClient?.Disconnect();
            _eventClient = null;
            AddLog("Disconnected from event service");
        }

        public void SubscribeOrUnsubscribeEvent(string eventName)
        {
            if (!CheckIfEventConnected(true))
            {
                return;
            }

            if (eventName == EventNames.SendEmailCommand)
            {
                _eventClient?.Unsubscribe<NewEmailNotification>(eventName, ReceiveSendEmailCommand);
                if (IsSubscribeSendEmailCommand)
                {
                    _eventClient?.Subscribe<NewEmailNotification>(eventName, ReceiveSendEmailCommand);
                }
            }
            else
            {
                _eventClient?.Unsubscribe<EmailQuery>(eventName, ReceiveEmailQuery);
                if (IsSubscribeSendEmailCommand)
                {
                    _eventClient?.Subscribe<EmailQuery>(eventName, ReceiveEmailQuery);
                }
            }
        }

        public void PublishOrQueryEvent(string eventName)
        {
            if (!CheckIfEventConnected(true))
            {
                return;
            }

            if (eventName == EventNames.SendEmailCommand)
            {
                if (_eventClient!.Publish(eventName, NewEmailNotification.GenerateRandomNewEmailNotification(),
                        out var errorMessage))
                {
                    AddLog("Successfully sent event");
                }
                else
                {
                    AddLog($"Sending event failed, the error message is [{errorMessage}]");
                }
            }
            else
            {
                var response = _eventClient!.Query<EmailQuery, NewEmailNotification>(eventName, new EmailQuery(),
                    out var errorMessage);
                if (string.IsNullOrWhiteSpace(errorMessage) && response != null)
                {
                    AddLog($"Successfully query: {response}");
                }
                else
                {
                    AddLog($"Query failed, the error message is [{errorMessage}]");
                }
            }
        }


        private void ReceiveSendEmailCommand(NewEmailNotification response)
        {
            AddLog($"Received {EventNames.SendEmailCommand} is [{response}]");
        }

        private void ReceiveEmailQuery(EmailQuery request)
        {
            AddLog($"Received request {EventNames.EmailQuery} is [{request}]");

            if (_eventClient!.Publish(EventNames.EmailQuery, NewEmailNotification.GenerateRandomNewEmailNotification(),
                    out var errorMessage))
            {
                AddLog("Successfully response query");
            }
            else
            {
                AddLog($"Response query event failed, the error message is [{errorMessage}]");
            }
        }

        private bool CheckIfEventConnected(bool showMsg = false)
        {
            if (_eventClient is { ConnectStatus: ConnectStatus.Connected }) return true;
            if (showMsg)
            {
                var msg = "Please connect to the event service before sending the event";
                AddLog(msg);
                Notification(msg);
            }

            return false;
        }
    }
}