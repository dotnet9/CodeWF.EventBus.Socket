namespace CodeWF.EventBus.Socket.Extensions;

public static class CommonExtension
{
    public static string GetString<T>(this T instance)
    {
        var type = typeof(T);
        if (type.IsValueType || instance is string)
        {
            return $"{instance}";
        }

        return System.Text.Json.JsonSerializer.Serialize(instance);
    }

    public static T? GetInstance<T>(this string str)
    {
        var instanceType = typeof(T);
        if (instanceType.IsEnum)
        {
            if (Enum.IsDefined(instanceType, str))
            {
                return (T)Enum.Parse(instanceType, str, true);
            }

            throw new Exception($"convert \"str\" to {instanceType.FullName} fail");
        }

        if (instanceType.IsPrimitive || instanceType == typeof(string))
        {
            return (T)Convert.ChangeType(str, instanceType);
        }

        return System.Text.Json.JsonSerializer.Deserialize<T>(str);
    }

    public static object? GetInstance(this string str, Type instanceType)
    {
        if (instanceType.IsEnum)
        {
            return Enum.Parse(instanceType, str, true);
        }

        if (instanceType.IsPrimitive || instanceType == typeof(string))
        {
            return Convert.ChangeType(str, instanceType);
        }

        return System.Text.Json.JsonSerializer.Deserialize(str, instanceType);
    }
}