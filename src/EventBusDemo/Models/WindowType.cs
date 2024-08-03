using System.ComponentModel;

namespace EventBusDemo.Models;

internal enum WindowType
{
    [Description("Event management end")] Manager,
    [Description("Event Server")] Server,
    [Description("Event Client")] Client
}