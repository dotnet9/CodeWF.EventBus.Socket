namespace CodeWF.EventBus.Socket.Extensions;

public static class ReflectionExtension
{
    public static T Property<T>(this object obj, string propertyName, T defaultValue = default)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

        var propertyInfo = obj.GetType().GetProperty(propertyName);
        if (propertyInfo == null) return defaultValue;

        var value = propertyInfo.GetValue(obj);

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch (InvalidCastException)
        {
            return defaultValue;
        }
    }
}