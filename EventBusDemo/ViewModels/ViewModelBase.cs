using Avalonia.Controls.Notifications;
using ReactiveUI;
using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace EventBusDemo.ViewModels;

public class ViewModelBase : ReactiveObject
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

    public WindowNotificationManager? NotificationManager { get; set; }
    public ObservableCollection<string> Logs { get; } = [];

    public void AddLog(string message)
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            Logs.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss fff}: {message}");
            RunTip = message;
            NotificationManager?.Show(RunTip);
        });
    }
}