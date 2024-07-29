using CodeWF.EventBus.Socket;
using EventBusDemo.Models;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls.Notifications;

namespace EventBusDemo.ViewModels
{
    public class EventClientViewModel : ViewModelBase
    {

        private IEventClient? _eventClient;
        private const string EventSubject = "event.email.new";

        public EventClientViewModel()
        {
            Title = $"EventBus client, process ID is {Process.GetCurrentProcess().Id}";
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

        public void Subscribe()
        {
            if (CheckIfEventConnected(true))
            {
                _eventClient?.Subscribe<NewEmailNotification>(EventSubject, ReceiveNewEmail);
            }
        }

        public void Unsubscribe()
        {
            if (CheckIfEventConnected(true))
            {
                _eventClient?.Unsubscribe<NewEmailNotification>(EventSubject, ReceiveNewEmail);
            }
        }

        public void SendEvent()
        {
            if (!CheckIfEventConnected(true))
            {
                return;
            }

            if (_eventClient!.Publish(EventSubject, NewEmailNotification.GenerateRandomNewEmailNotification(),
                    out var errorMessage))
            {
                AddLog("Successfully sent event");
            }
            else
            {
                AddLog($"Sending event failed, the error message is [{errorMessage}]");
            }
        }

        private void ReceiveNewEmail(NewEmailNotification message)
        {
            AddLog($"Received event is [{message}]");
        }

        private bool CheckIfEventConnected(bool showMsg = false)
        {
            if (_eventClient is { ConnectStatus: ConnectStatus.Connected }) return true;
            if (showMsg)
            {
                AddLog("Please connect to the event service before sending the event");
            }

            return false;
        }
    }
}