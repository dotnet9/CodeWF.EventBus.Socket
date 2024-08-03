using System.Collections.Generic;
using EventBusDemo.Commands;

namespace EventBusDemo.Queries;

public class EmailQueryResponse
{
    public List<NewEmailCommand>? Emails { get; set; }

    public override string ToString()
    {
        return $"There are {Emails?.Count} emails";
    }
}