using System;
using System.Collections.Generic;

namespace EventBusDemo.Models;

internal class NewEmailNotification
{
    public string? Subject { get; set; }
    public string? Content { get; set; }
    public long SendTime { get; set; }

    public override string ToString()
    {
        return
            $"{nameof(Subject)}: {Subject}, {nameof(Content)}: {Content}, {nameof(SendTime)}: {DateTimeOffset.FromFileTime(SendTime):yyyy-MM-dd HH:mm:ss fff}";
    }

    public static NewEmailNotification GenerateRandomNewEmailNotification()
    {
        var subjects = new List<string>
        {
            "Important Update",
            "Your Order Confirmation",
            "Password Reset Request",
            "Account Verification",
            "New Message from Support"
        };

        var contents = new List<string>
        {
            "Please see the attached document for the latest updates.",
            "Your order #123456 has been shipped.",
            "We have received a request to reset your password. Please follow the link below.",
            "Please verify your email address by clicking the link below.",
            "We have received a new message for you from our support team."
        };

        var subject = subjects[Random.Shared.Next(subjects.Count)];
        var content = contents[Random.Shared.Next(contents.Count)];
        var sendTime = DateTime.Now.AddDays(-7 + Random.Shared.NextDouble() * 7);

        return new NewEmailNotification
        {
            Subject = subject,
            Content = content,
            SendTime = sendTime.ToFileTimeUtc()
        };
    }
}