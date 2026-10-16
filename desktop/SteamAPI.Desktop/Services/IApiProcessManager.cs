namespace SteamAPI.Desktop.Services;

public enum ApiProcessState
{
    Stopped,
    Starting,
    Running,
    Failed
}

public interface IApiProcessManager : IDisposable
{
    ApiProcessState State { get; }
    string? LastError { get; }
    int? ProcessId { get; }
    string BoundUrl { get; }
    event Action<ApiProcessState>? OnStateChanged;

    Task<bool> StartAsync(string? executablePath = null, CancellationToken cancellationToken = default);
    Task StopAsync();
}
