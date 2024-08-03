namespace EventBusDemo.Queries;

public class EmailQuery
{
    public string? Subject { get; set; }

    public override string ToString()
    {
        return $"Need query {nameof(Subject)}: {Subject}";
    }
}