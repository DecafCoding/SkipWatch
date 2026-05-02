using Microsoft.AspNetCore.Components;
using SkipWatch.Services.Interfaces;
using SkipWatch.Services.Models;

namespace SkipWatch.Components.Shared;

public partial class MessageSidebar : IDisposable
{
    [Inject]
    private IMessageCenterService MessageCenterService { get; set; } = default!;

    private List<MessageState> _messages = new();

    protected override void OnInitialized()
    {
        MessageCenterService.MessageChanged += OnMessageChanged;
        _messages = MessageCenterService.GetRecentMessages();
    }

    private void OnMessageChanged(MessageState? newMessage)
    {
        _messages = MessageCenterService.GetRecentMessages();
        InvokeAsync(StateHasChanged);
    }

    private static string GetTextColorClass(MessageType type) => type switch
    {
        MessageType.Success => "text-success",
        MessageType.Error => "text-danger",
        MessageType.Warning => "text-warning",
        MessageType.Info => "text-info",
        MessageType.ApiLimit => "text-warning",
        _ => "text-secondary"
    };

    public void Dispose()
    {
        MessageCenterService.MessageChanged -= OnMessageChanged;
    }
}
