namespace SkipWatch.Services.Models;

public class MessageState
{
    public string Text { get; set; } = string.Empty;
    public MessageType Type { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Context { get; set; }

    public MessageState(string text, MessageType type, string? context = null)
    {
        Text = text;
        Type = type;
        Context = context;
    }

    public string GetFullMessage() =>
        string.IsNullOrEmpty(Context) ? Text : $"{Text} {Context}";
}

public enum MessageType
{
    Success,
    Error,
    Warning,
    Info,
    ApiLimit
}
