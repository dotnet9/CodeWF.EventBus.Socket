using CodeWF.EventBus.Socket.NetWeaver;

namespace CodeWF.EventBus.Socket.Test;

public class NetWeaverUnitTest
{
    [Fact]
    public void Test_SerializeAndDeserializeInt_Success()
    {
        var value = 2;

        var buffer = value.SerializeObject();
        var newValue = buffer.DeserializeObject(typeof(int));

        Assert.Equal(value, newValue);
    }


    [Fact]
    public void Test_SerializeAndDeserializeString_Success()
    {
        var value = "https://codewf.com";

        var buffer = value.SerializeObject(typeof(string));
        var newValue = buffer.DeserializeObject(typeof(string));

        Assert.Equal(value, newValue);
    }


    [Fact]
    public void Test_SerializeAndDeserializeList_Success()
    {
        var value = new List<string> { "https://codewf.com" };

        var buffer = value.SerializeObject();
        var newValue = buffer.DeserializeObject(typeof(List<string>)) as List<string>;

        Assert.NotNull(newValue);
        Assert.True(newValue.Count > 0);
        Assert.Equal(value.Count, newValue.Count);
        Assert.Equal(value[0], newValue[0]);
    }

    [Fact]
    public void Test_SerializeAndDeserializeDictionary_Success()
    {
        var value = new Dictionary<string, string> { { "1", "https://codewf.com" } };

        var buffer = value.SerializeObject();
        var newValue = buffer.DeserializeObject(typeof(Dictionary<string, string>)) as Dictionary<string, string>;

        Assert.NotNull(newValue);
        Assert.True(newValue.Count > 0);
        Assert.Equal(value.Count, newValue.Count);
        foreach (var kvp in value)
        {
            Assert.True(newValue.ContainsKey(kvp.Key));
            Assert.True(newValue[kvp.Key] == kvp.Value);
        }
    }

    [Fact]
    public void Test_SerializeAndDeserializeObject_Success()
    {
        var obj = new Student()
        {
            Id = 1,
            Name = "codewf"
        };

        var buffer = SerializeHelper.SerializeObject(obj);
        var newObj = buffer.DeserializeObject<Student>();

        Assert.Equal(obj.Id, newObj.Id);
        Assert.Equal(obj.Name, newObj.Name);
    }

    [Fact]
    public void Test_SerializeAndDeserializeObjectWithBytes_Success()
    {
        var cls = new Class() { Id = 1 };
        var stu = new Student()
        {
            Id = 1,
            Name = "codewf"
        };
        cls.Buffer = SerializeHelper.SerializeObject(stu);
        var clsBuffer = SerializeHelper.SerializeObject(cls);
        var newCls = clsBuffer.DeserializeObject<Class>();
        var newStu = newCls.Buffer!.DeserializeObject<Student>();

        Assert.Equal(stu.Id, newStu.Id);
        Assert.Equal(stu.Name, newStu.Name);
    }
}

public class Student
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

public class Class
{
    public int Id { get; set; }

    public byte[]? Buffer { get; set; }
}