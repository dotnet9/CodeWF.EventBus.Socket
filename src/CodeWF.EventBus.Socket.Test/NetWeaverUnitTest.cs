using CodeWF.EventBus.Socket.NetWeaver;

namespace CodeWF.EventBus.Socket.Test;

public class NetWeaverUnitTest
{
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