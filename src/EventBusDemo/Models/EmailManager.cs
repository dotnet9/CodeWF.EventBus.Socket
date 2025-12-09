using System;
using System.Collections.Generic;
using System.Linq;
using EventBusDemo.Commands;

namespace EventBusDemo.Models;

public static class EmailManager
{
    private static readonly List<string> Subjects =
    [
        "重要更新",
        "您的订单确认",
        "密码重置请求",
        "账户验证",
        "来自支持的新消息"
    ];

    private static readonly List<string> Contents =
    [
        "请查看附件文档获取最新更新。",
        "您的订单 #123456 已发货。",
        "我们已收到重置密码的请求。请点击下方链接。",
        "请点击下方链接验证您的邮箱地址。",
        "我们已收到支持团队给您的新消息。"
    ];

    public static NewEmailCommand GenerateRandomNewEmailNotification()
    {
        var subject = Subjects[Random.Shared.Next(Subjects.Count)];
        var content = Contents[Random.Shared.Next(Contents.Count)];
        var sendTime = DateTime.Now.AddDays(-7 + Random.Shared.NextDouble() * 7);

        return new NewEmailCommand
        {
            Subject = subject,
            Content = content,
            SendTime = sendTime.ToFileTimeUtc()
        };
    }

    public static List<NewEmailCommand>? QueryEmail(string? subject)
    {
        var existSubjects = Subjects.Where(item => subject == null || item.Contains(subject));
        return existSubjects.Select(item => new NewEmailCommand
        {
            Subject = item,
            Content = Contents[Random.Shared.Next(Contents.Count)],
            SendTime = DateTime.Now.AddDays(-7 + Random.Shared.NextDouble() * 7).ToFileTimeUtc()
        }).ToList();
    }
}