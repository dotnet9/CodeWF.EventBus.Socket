using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Interactivity;
using EventBusDemo.ViewModels;

namespace EventBusDemo.Views;

public partial class EventServerView : Window
{
    public EventServerView()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        var vm = DataContext as ViewModelBase;
        if (vm is not { NotificationManager: null }) return;
        var topLevel = GetTopLevel(this);
        vm.NotificationManager =
            new WindowNotificationManager(topLevel) { MaxItems = 3 };
    }
}