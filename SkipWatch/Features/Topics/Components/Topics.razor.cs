using Microsoft.AspNetCore.Components;

namespace SkipWatch.Features.Topics.Components;

public partial class Topics : ComponentBase
{
    private TopicList? TopicListComponent;
    private int TrackedCount;

    private async Task HandleTopicAdded()
    {
        if (TopicListComponent is not null)
            await TopicListComponent.RefreshAsync();
    }

    private Task HandleTopicsChanged(int count)
    {
        TrackedCount = count;
        StateHasChanged();
        return Task.CompletedTask;
    }
}
