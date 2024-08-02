namespace EventBusDemo.Services;

public class ApplicationConfig
{
    private string? _host;

    public void SetHost(string? host)
    {
        _host = host;
    }

    public string? GetHost()
    {
        return _host;
    }
}