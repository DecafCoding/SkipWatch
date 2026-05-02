using SkipWatch.Services.Models;

namespace SkipWatch.Services.Interfaces;

public interface IMessageCenterService
{
    event Action<MessageState?>? MessageChanged;

    Task ShowSuccessAsync(string message);
    Task ShowErrorAsync(string message);
    Task ShowWarningAsync(string message);
    Task ShowInfoAsync(string message);
    Task ShowApiLimitAsync(string apiName, DateTime? resetTime = null);

    List<MessageState> GetRecentMessages();
}
