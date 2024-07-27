using CodeWF.EventBus.Socket;
using ReactiveUI;
using System;
using System.Diagnostics;

namespace EventBusDemo.ViewModels
{
    public class EventServerViewModel : ViewModelBase
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


        private IEventServer? _eventServer;

        public EventServerViewModel()
        {
            Title = $"EventBus服务端，进程Id：{Process.GetCurrentProcess().Id}";
        }

        public void RunServer()
        {
            _eventServer ??= new EventServer();
            var addressArray = Address!.Split(new char[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
            _eventServer.Start(addressArray[0], int.Parse(addressArray[1]));
            RunTip = "已开启";
            AddLog("已开启事件服务");
        }

        public void Stop()
        {
            _eventServer?.Stop();
            _eventServer = null;
            RunTip = "未开启";
            AddLog("已停止事件服务");
        }
    }
}