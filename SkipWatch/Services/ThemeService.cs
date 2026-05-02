using Microsoft.JSInterop;
using SkipWatch.Services.Interfaces;

namespace SkipWatch.Services;

public class ThemeService : IThemeService, IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask;
    private string _currentTheme = "dark";

    public event Action<string>? ThemeChanged;

    public ThemeService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
        _moduleTask = new(() => _jsRuntime.InvokeAsync<IJSObjectReference>(
            "import", "/js/theme.js").AsTask());
    }

    public async Task<string> GetThemeAsync()
    {
        if (!IsJavaScriptAvailable())
            return _currentTheme;

        try
        {
            var module = await _moduleTask.Value;
            var theme = await module.InvokeAsync<string>("getTheme");
            _currentTheme = Normalize(theme);
            return _currentTheme;
        }
        catch
        {
            return _currentTheme;
        }
    }

    public async Task SetThemeAsync(string theme)
    {
        _currentTheme = Normalize(theme);

        if (!IsJavaScriptAvailable())
        {
            ThemeChanged?.Invoke(_currentTheme);
            return;
        }

        try
        {
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("setTheme", _currentTheme);
        }
        catch
        {
            // Still notify even if persistence failed
        }

        ThemeChanged?.Invoke(_currentTheme);
    }

    private static string Normalize(string? theme) =>
        theme == "light" ? "light" : "dark";

    private bool IsJavaScriptAvailable()
    {
        try
        {
            return _jsRuntime is IJSInProcessRuntime ||
                   !_jsRuntime.GetType().Name.Contains("Unsupported");
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_moduleTask.IsValueCreated)
        {
            try
            {
                var module = await _moduleTask.Value;
                await module.DisposeAsync();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }
}
