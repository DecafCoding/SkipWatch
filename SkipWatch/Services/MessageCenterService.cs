using SkipWatch.Services.Interfaces;
using SkipWatch.Services.Models;

namespace SkipWatch.Services;

public class MessageCenterService : IMessageCenterService
{
    private readonly List<MessageState> _messages = new();
    private readonly object _lock = new();

    public event Action<MessageState?>? MessageChanged;

    public Task ShowSuccessAsync(string message)
    {
        SetMessage(message, MessageType.Success);
        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(string message)
    {
        SetMessage(message, MessageType.Error);
        return Task.CompletedTask;
    }

    public Task ShowWarningAsync(string message)
    {
        SetMessage(message, MessageType.Warning);
        return Task.CompletedTask;
    }

    public Task ShowInfoAsync(string message)
    {
        SetMessage(message, MessageType.Info);
        return Task.CompletedTask;
    }

    public Task ShowApiLimitAsync(string apiName, DateTime? resetTime = null)
    {
        var message = $"API limit reached for {apiName}.";
        string? context = null;

        if (resetTime.HasValue)
        {
            var timeUntilReset = resetTime.Value - DateTime.UtcNow;
            if (timeUntilReset.TotalMinutes > 60)
                context = $"Resets in {timeUntilReset.Hours}h {timeUntilReset.Minutes}m.";
            else if (timeUntilReset.TotalMinutes > 1)
                context = $"Resets in {(int)timeUntilReset.TotalMinutes} minutes.";
            else if (timeUntilReset.TotalSeconds > 0)
                context = "Resets in less than 1 minute.";
            else
                context = "Limit should reset momentarily.";
        }

        SetMessage(message, MessageType.ApiLimit, context);
        return Task.CompletedTask;
    }

    public List<MessageState> GetRecentMessages()
    {
        lock (_lock)
        {
            return new List<MessageState>(_messages);
        }
    }

    private void SetMessage(string text, MessageType type, string? context = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        MessageState newMessage;
        lock (_lock)
        {
            newMessage = new MessageState(text.Trim(), type, context);
            _messages.Insert(0, newMessage);
            if (_messages.Count > 5)
                _messages.RemoveAt(5);
        }

        MessageChanged?.Invoke(newMessage);
    }
}
