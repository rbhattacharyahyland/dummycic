namespace DummyCicServer.Models;

public sealed class EventDiscoveryResponse
{
    public string EventType { get; set; } = string.Empty;
    public List<EventAlias> EventAliases { get; set; } = new();
}

public sealed class EventAlias
{
    public string AliasId { get; set; } = string.Empty;
    public string AliasName { get; set; } = string.Empty;
    public List<RequestField> RequestFields { get; set; } = new();
    public List<ResponseField> ResponseFields { get; set; } = new();
    public PatternInfo Pattern { get; set; } = new();
}

public sealed class RequestField
{
    public string FieldName { get; set; } = string.Empty;
    public string FieldType { get; set; } = "string";
    public bool IsFilterAttribute { get; set; }
    public bool Required { get; set; }
}

public sealed class ResponseField
{
    public string FieldName { get; set; } = string.Empty;
    public string FieldType { get; set; } = "string";
}

public sealed class PatternInfo
{
    public string Pattern { get; set; } = "requestReply";
    public string SubscriberCardinality { get; set; } = "single";
    public bool RecipientRequired { get; set; }
    public List<string> AllowedRecipients { get; set; } = new();
}
