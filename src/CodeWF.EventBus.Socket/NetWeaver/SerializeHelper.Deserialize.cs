namespace CodeWF.EventBus.Socket.NetWeaver;

public partial class SerializeHelper
{
    public static T Deserialize<T>(this byte[] buffer) where T : new()
    {
        return DeserializeObject<T>(buffer, PacketHeadLen);
    }

    public static T DeserializeObject<T>(this byte[] buffer, int readIndex = 0) where T : new()
    {
        using (var stream = new MemoryStream(buffer, readIndex, buffer.Length - readIndex))
        {
            using (var reader = new BinaryReader(stream))
            {
                var data = new T();
                DeserializeProperties(reader, data);
                return data;
            }
        }
    }

    public static object DeserializeObject(this byte[] buffer, Type type, int readIndex = 0)
    {
        using (var stream = new MemoryStream(buffer, readIndex, buffer.Length - readIndex))
        {
            using (var reader = new BinaryReader(stream))
            {
                var data = Activator.CreateInstance(type);
                DeserializeProperties(reader, data);
                return data;
            }
        }
    }

    private static void DeserializeProperties<T>(BinaryReader reader, T data)
    {
        var properties = GetProperties(data.GetType());
        foreach (var property in properties)
        {
            if (property.GetCustomAttribute(typeof(NetIgnoreMemberAttribute)) is NetIgnoreMemberAttribute _)
                continue;

            var value = DeserializeValue(reader, property.PropertyType);
            property.SetValue(data, value);
        }
    }

    private static object DeserializeValue(BinaryReader reader, Type propertyType)
    {
        object value;
        if (propertyType.IsPrimitive || propertyType == typeof(string) || propertyType == typeof(byte[]))
            value = DeserializeBaseValue(reader, propertyType);
        else if (ComplexTypeNames.Contains(propertyType.Name))
            value = DeserializeComplexValue(reader, propertyType);
        else
            value = DeserializeObject(reader, propertyType);

        return value;
    }

    private static object DeserializeBaseValue(BinaryReader reader, Type propertyType)
    {
        if (propertyType == typeof(byte)) return reader.ReadByte();

        if (propertyType == typeof(byte[])) return reader.ReadBytes(reader.ReadInt32());

        if (propertyType == typeof(short)) return reader.ReadInt16();

        if (propertyType == typeof(ushort)) return reader.ReadUInt16();

        if (propertyType == typeof(int)) return reader.ReadInt32();

        if (propertyType == typeof(uint)) return reader.ReadUInt32();

        if (propertyType == typeof(long)) return reader.ReadInt64();

        if (propertyType == typeof(float)) return reader.ReadSingle();

        if (propertyType == typeof(double)) return reader.ReadDouble();

        if (propertyType == typeof(decimal)) return reader.ReadDecimal();

        if (propertyType == typeof(string)) return reader.ReadString();

        throw new Exception($"Unsupported data type: {propertyType.Name}");
    }

    private static object DeserializeComplexValue(BinaryReader reader, Type propertyType)
    {
        var count = reader.ReadInt32();
        var genericArguments = propertyType.GetGenericArguments();
        var complexObj = Activator.CreateInstance(propertyType);
        var addMethod = propertyType.GetMethod("Add");

        for (var i = 0; i < count; i++)
        {
            var key = DeserializeValue(reader, genericArguments[0]);
            if (genericArguments.Length == 1)
            {
                addMethod.Invoke(complexObj, new[] { key });
            }
            else if (genericArguments.Length == 2)
            {
                var value = DeserializeValue(reader, genericArguments[1]);
                addMethod.Invoke(complexObj, new[] { key, value });
            }
        }

        return complexObj;
    }

    private static object DeserializeObject(BinaryReader reader, Type type)
    {
        var data = Activator.CreateInstance(type);
        DeserializeProperties(reader, data);
        return data;
    }
}