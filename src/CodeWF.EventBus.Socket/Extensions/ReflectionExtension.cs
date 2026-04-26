using System.Diagnostics.CodeAnalysis;

namespace CodeWF.EventBus.Socket.Extensions;

public static class ReflectionExtension
{
    [return: MaybeNull]
    public static T Property<T>(this object obj, string propertyName, [AllowNull] T defaultValue = default!)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        if (string.IsNullOrEmpty(propertyName)) throw new ArgumentNullException(nameof(propertyName));

        var propertyInfo = obj.GetType().GetProperty(propertyName);
        if (propertyInfo == null) return defaultValue;

        var value = propertyInfo.GetValue(obj);
        if (value == null) return defaultValue;

        if (value is T typedValue) return typedValue;

        try
        {
            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
            return (T)Convert.ChangeType(value, targetType);
        }
        catch
        {
            return defaultValue;
        }
    }
}
