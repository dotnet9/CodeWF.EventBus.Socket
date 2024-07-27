using CodeWF.EventBus.Socket.Extensions;

namespace CodeWF.EventBus.Socket.Test.Extensions
{
    public class CommonExtensionUnitTest
    {
        [Fact]
        public void Test_GetString_Success()
        {
            var i = 3;
            var str = "https://codewf.com";
            var student = new Student(32, "CodeWF");

            var strI = i.GetString();
            var strStr = str.GetString();
            var studentStr = student.GetString();

            Assert.Equal(i.ToString(), strI);
            Assert.Equal(str, strStr);
            Assert.Equal("{\"Id\":32,\"Name\":\"CodeWF\"}", studentStr);
        }

        [Fact]
        public void Test_GetInstance_Success()
        {
            var strI = "3";
            var strStr = "https://codewf.com";
            var studentStr = "{\"Id\":32,\"Name\":\"CodeWF\"}";

            var i = strI.GetInstance<int>();
            var str = strStr.GetInstance<string>();
            var student = studentStr.GetInstance<Student>();

            Assert.Equal(3, i);
            Assert.Equal(strStr, str);
            Assert.Equal(32, student!.Id);
            Assert.Equal("CodeWF", student!.Name);
        }
    }

    public class Student(int id, string name)
    {
        public int Id { get; } = id;
        public string? Name { get; } = name;
    }
}