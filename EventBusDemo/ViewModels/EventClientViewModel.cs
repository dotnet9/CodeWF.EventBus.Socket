using CodeWF.EventBus.Socket.Client;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using EventBusDemo.Models;

namespace EventBusDemo.ViewModels
{
    public class EventClientViewModel : ViewModelBase
    {
        private string? _title;

        public string? Title
        {
            get => _title;
            set => this.RaiseAndSetIfChanged(ref _title, value);
        }

        private string? _address;

        public string? Address
        {
            get => _address;
            set => this.RaiseAndSetIfChanged(ref _address, value);
        }

        private string? _runTip;

        public string? RunTip
        {
            get => _runTip;
            set => this.RaiseAndSetIfChanged(ref _runTip, value);
        }

        public ObservableCollection<string> Logs { get; } = new();


        private IEventClient? _eventClient;
        private const string EventSubject = "event.email.new";

        public EventClientViewModel()
        {
            Title = $"EventBus客户端，进程Id：{Process.GetCurrentProcess().Id}";
        }

        public void ConnectServer()
        {
            _eventClient ??= new EventClient();
            var addressArray = Address!.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            if (_eventClient.Connect(addressArray[0], int.Parse(addressArray[1]), out var message))
            {
                RunTip = "已开启";
                AddLog("已连接事件服务");
            }
            else
            {
                RunTip = "未开启";
                AddLog($"连接事件服务失败：{message}");
            }
        }

        public void Disconnect()
        {
            _eventClient?.Disconnect();
            _eventClient = null;
            RunTip = "未开启";
            AddLog("已断开事件服务");
        }

        public void Subscribe()
        {
            _eventClient?.Subscribe<NewEmailNotification>(EventSubject, ReceiveNewEmail);
        }

        public void Unsubscribe()
        {
            _eventClient?.Unsubscribe<NewEmailNotification>(EventSubject, ReceiveNewEmail);
        }

        public void SendEvent()
        {
            _eventClient?.Publish(EventSubject, NewEmailNotification.GenerateRandomNewEmailNotification());
        }

        private void ReceiveNewEmail(NewEmailNotification message)
        {
            RunTip = $"收到事件：{message}";
            AddLog($"收到事件：{message}");
        }
    }
}