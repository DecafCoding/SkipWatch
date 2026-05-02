using Microsoft.AspNetCore.Components;

namespace SkipWatch.Features.Channels.Components;

public partial class Channels : ComponentBase
{
    private ChannelList? ChannelListComponent;
    private int TrackedCount;

    private async Task HandleChannelAdded()
    {
        if (ChannelListComponent is not null)
            await ChannelListComponent.RefreshAsync();
    }

    private Task HandleChannelsChanged(int count)
    {
        TrackedCount = count;
        StateHasChanged();
        return Task.CompletedTask;
    }
}
