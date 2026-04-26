using System;
using System.Diagnostics;
using EventBusDemo.Services;
using Path = System.IO.Path;

namespace EventBusDemo.ViewModels;

public class EventManagerViewModel : ViewModelBase
{
    public EventManagerViewModel()
    {
        Title = "EventBus演示管理器";
        Address = "127.0.0.1:5329";
    }

    public void OpenEventServer()
    {
        OpenNewExe(true);
    }

    public void OpenEventClient()
    {
        OpenNewExe(false);
    }

    private void OpenNewExe(bool isServer)
    {
        if (!EventBusAddressParser.TryParse(Address, out _, out _))
        {
            return;
        }

        var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EventBusDemo.exe");
        // 通过命令行参数把角色和监听地址传给新进程，方便本地快速拉起多实例演示。
        Process.Start(exePath, ["-type", isServer ? "server" : "client", "-address", Address ?? "127.0.0.1:5329"]);
    }
}
