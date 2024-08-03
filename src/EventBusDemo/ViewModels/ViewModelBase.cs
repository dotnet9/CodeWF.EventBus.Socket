using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using ReactiveUI;

namespace EventBusDemo.ViewModels;

public class ViewModelBase : ReactiveObject
{
    private string? _address;
    private string? _title;

    public string? Title
    {
        get => _title;
        set => this.RaiseAndSetIfChanged(ref _title, value);
    }

    public string? Address
    {
        get => _address;
        set => this.RaiseAndSetIfChanged(ref _address, value);
    }

    public WindowNotificationManager? NotificationManager { get; set; }

    public void Notification(string message)
    {
        Dispatcher.UIThread.Invoke(() => { NotificationManager?.Show(message); });
    }
}