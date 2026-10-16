using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamAPI.Desktop.Models;
using SteamAPI.Desktop.Services;

namespace SteamAPI.Desktop.ViewModels;

public partial class GameExplorerViewModel : ViewModelBase
{
    private readonly ISteamApiClient _apiClient;
    private readonly IDemoDataService _demoDataService;
    private readonly ISettingsService _settingsService;
    private readonly IClipboardService _clipboardService;
    private readonly ILauncherService _launcherService;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private SteamCulture _selectedCulture;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasSearched;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _errorDetail;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private SteamGameListing? _selectedListing;

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private string? _copiedFeedbackMessage;

    public IReadOnlyList<SteamCulture> Cultures => SteamCulture.SupportedCultures;
    public ObservableCollection<SteamGameListing> Listings { get; } = new();

    public GameExplorerViewModel(
        ISteamApiClient apiClient,
        IDemoDataService demoDataService,
        ISettingsService settingsService,
        IClipboardService clipboardService,
        ILauncherService launcherService)
    {
        _apiClient = apiClient;
        _demoDataService = demoDataService;
        _settingsService = settingsService;
        _clipboardService = clipboardService;
        _launcherService = launcherService;

        _selectedCulture = Cultures.FirstOrDefault(c => c.Id == 2) ?? Cultures[0];
        _isDemoMode = _settingsService.CurrentSettings.DemoMode;
    }

    public bool CanSearch => !IsSearching && !string.IsNullOrWhiteSpace(SearchQuery) && SearchQuery.Length <= 200;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    public async Task SearchAsync()
    {
        var query = SearchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            ErrorMessage = "Please enter a game title to search.";
            return;
        }

        if (query.Length > 200)
        {
            ErrorMessage = "The search query cannot exceed 200 characters.";
            return;
        }

        try { _searchCts?.Cancel(); } catch { }
        try { _searchCts?.Dispose(); } catch { }
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        var ct = cts.Token;

        IsSearching = true;
        ErrorMessage = null;
        ErrorDetail = null;
        StatusMessage = "Searching Steam catalog...";
        CopiedFeedbackMessage = null;

        try
        {
            ApiResponse<IReadOnlyList<SteamGameListing>> response;

            if (IsDemoMode)
            {
                response = await _demoDataService.SearchSyntheticGamesAsync(query, SelectedCulture.Id, ct);
            }
            else
            {
                response = await _apiClient.SearchGamesAsync(query, SelectedCulture.Id, ct);
            }

            if (ct.IsCancellationRequested) return;

            Listings.Clear();

            if (response.IsSuccess && response.Data != null)
            {
                foreach (var item in response.Data)
                {
                    Listings.Add(item);
                }

                HasSearched = true;
                if (Listings.Count == 0)
                {
                    StatusMessage = $"No matching games found on Steam for \"{query}\" ({SelectedCulture.CountryCode}).";
                }
                else
                {
                    var demoBadge = IsDemoMode ? " [OFFLINE DEMO]" : "";
                    StatusMessage = $"Found {Listings.Count} game{(Listings.Count == 1 ? "" : "s")} in {response.DurationMs} ms{demoBadge}";
                    SelectedListing = Listings.FirstOrDefault();
                }
            }
            else
            {
                HasSearched = true;
                ErrorMessage = response.ErrorMessage ?? "Search request failed.";
                ErrorDetail = response.ErrorDetail;
                StatusMessage = $"Error ({response.StatusCode}): {response.ErrorMessage}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Search cancelled.";
        }
        catch (Exception ex)
        {
            ErrorMessage = "An unexpected error occurred.";
            ErrorDetail = ex.Message;
            StatusMessage = "Search failed.";
        }
        finally
        {
            IsSearching = false;
        }
    }

    public void CancelPendingOperations()
    {
        var cts = Interlocked.Exchange(ref _searchCts, null);
        try { cts?.Cancel(); } catch { }
        try { cts?.Dispose(); } catch { }
    }

    [RelayCommand]
    public void ClearSearch()
    {
        CancelPendingOperations();
        SearchQuery = string.Empty;
        ErrorMessage = null;
        ErrorDetail = null;
        StatusMessage = null;
        Listings.Clear();
        SelectedListing = null;
        HasSearched = false;
        CopiedFeedbackMessage = null;
    }

    [RelayCommand]
    public void SelectListing(SteamGameListing? listing)
    {
        SelectedListing = listing;
        CopiedFeedbackMessage = null;
    }

    [RelayCommand]
    public void CloseDetails()
    {
        SelectedListing = null;
    }

    [RelayCommand]
    public async Task OpenInSteamAsync(SteamGameListing? listing)
    {
        var target = listing ?? SelectedListing;
        if (target != null && !string.IsNullOrWhiteSpace(target.AppId))
        {
            var uri = new Uri($"steam://store/{target.AppId}");
            await _launcherService.LaunchUriAsync(uri);
        }
    }

    [RelayCommand]
    public async Task OpenStoreLinkAsync(SteamGameListing? listing)
    {
        var target = listing ?? SelectedListing;
        if (target != null && Uri.TryCreate(target.StoreLink, UriKind.Absolute, out var uri))
        {
            await _launcherService.LaunchUriAsync(uri);
        }
    }

    [RelayCommand]
    public async Task CopyAppIdAsync(SteamGameListing? listing)
    {
        var target = listing ?? SelectedListing;
        if (target != null && !string.IsNullOrWhiteSpace(target.AppId))
        {
            await _clipboardService.SetTextAsync(target.AppId);
            ShowCopiedFeedback($"Copied App ID: {target.AppId}");
        }
    }

    [RelayCommand]
    public async Task CopyStoreLinkAsync(SteamGameListing? listing)
    {
        var target = listing ?? SelectedListing;
        if (target != null && !string.IsNullOrWhiteSpace(target.StoreLink))
        {
            await _clipboardService.SetTextAsync(target.StoreLink);
            ShowCopiedFeedback("Copied Steam Store Link");
        }
    }

    public void UpdateDemoMode(bool isDemo)
    {
        IsDemoMode = isDemo;
        if (HasSearched && !string.IsNullOrWhiteSpace(SearchQuery))
        {
            // Re-run search under new mode
            _ = SearchAsync();
        }
    }

    private void ShowCopiedFeedback(string message)
    {
        CopiedFeedbackMessage = message;
    }
}
