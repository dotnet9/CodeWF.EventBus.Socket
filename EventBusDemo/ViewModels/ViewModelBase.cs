using ReactiveUI;
using System;
using System.Collections.ObjectModel;

namespace EventBusDemo.ViewModels;

public class ViewModelBase : ReactiveObject
{
    public ObservableCollection<string> Logs { get; } = [];

    public void AddLog(string msg)
    {
        Logs.Add($"{DateTime.Now:yyyy-MM-dd HH:mm:ss fff}: {msg}");
    }
}