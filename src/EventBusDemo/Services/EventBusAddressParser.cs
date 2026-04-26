using System;
using System.Globalization;

namespace EventBusDemo.Services;

internal static class EventBusAddressParser
{
    public static bool TryParse(string? address, out string host, out int port)
    {
        host = string.Empty;
        port = default;

        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        var normalizedAddress = address.Trim();
        if (TryParseUriAddress(normalizedAddress, out host, out port))
        {
            return true;
        }

        return TryParseHostAndPort(normalizedAddress, out host, out port);
    }

    // 兼容 tcp://127.0.0.1:5329、tcp://[::1]:5329 这类更标准的地址格式。
    private static bool TryParseUriAddress(string address, out string host, out int port)
    {
        host = string.Empty;
        port = default;

        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Port is <= 0 or > 65535)
        {
            return false;
        }

        host = uri.Host;
        port = uri.Port;
        return !string.IsNullOrWhiteSpace(host);
    }

    // 兼容 127.0.0.1:5329、localhost:5329、[::1]:5329 这类输入。
    private static bool TryParseHostAndPort(string address, out string host, out int port)
    {
        host = string.Empty;
        port = default;

        string hostPart;
        string portPart;

        if (address.StartsWith('['))
        {
            var closingBracketIndex = address.IndexOf(']');
            if (closingBracketIndex <= 0 ||
                closingBracketIndex + 1 >= address.Length ||
                address[closingBracketIndex + 1] != ':')
            {
                return false;
            }

            hostPart = address[1..closingBracketIndex];
            portPart = address[(closingBracketIndex + 2)..];
        }
        else
        {
            var separatorIndex = address.LastIndexOf(':');
            if (separatorIndex <= 0 || separatorIndex == address.Length - 1)
            {
                return false;
            }

            hostPart = address[..separatorIndex];
            portPart = address[(separatorIndex + 1)..];
        }

        if (!int.TryParse(portPart, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
            port is <= 0 or > 65535)
        {
            return false;
        }

        host = hostPart.Trim();
        return !string.IsNullOrWhiteSpace(host);
    }
}
