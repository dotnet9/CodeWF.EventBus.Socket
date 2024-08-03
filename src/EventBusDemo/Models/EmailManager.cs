using System;
using System.Collections.Generic;
using System.Linq;
using EventBusDemo.Commands;

namespace EventBusDemo.Models;

public static class EmailManager
{
    private static readonly List<string> Subjects =
    [
        "Important Update",
        "Your Order Confirmation",
        "Password Reset Request",
        "Account Verification",
        "New Message from Support"
    ];

    private static readonly List<string> Contents =
    [
        "Please see the attached document for the latest updates.",
        "Your order #123456 has been shipped.",
        "We have received a request to reset your password. Please follow the link below.",
        "Please verify your email address by clicking the link below.",
        "We have received a new message for you from our support team."
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