using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamAPI.Desktop.Models;
using SteamAPI.Desktop.Services;

namespace SteamAPI.Desktop.ViewModels;

public partial class ApiInspectorViewModel : ViewModelBase
{
    private readonly IClipboardService _clipboardService;

    [ObservableProperty]
    private ApiInspectionRecord? _selectedRecord;

    [ObservableProperty]
    private string? _copyFeedbackMessage;

    public ObservableCollection<ApiInspectionRecord> Records { get; } = new();

    public ApiInspectorViewModel(IClipboardService clipboardService, ISteamApiClient? apiClient = null)
    {
        _clipboardService = clipboardService;

        if (apiClient != null)
        {
            apiClient.OnInspectionRecordCreated += AddRecord;
        }
    }

    public void AddRecord(ApiInspectionRecord record)
    {
        if (Application.Current != null && !Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AddRecordCore(record));
            return;
        }

        AddRecordCore(record);
    }

    private void AddRecordCore(ApiInspectionRecord record)
    {
        // Insert newest record at top
        Records.Insert(0, record);

        // Keep maximum 50 records in memory
        if (Records.Count > 50)
        {
            Records.RemoveAt(Records.Count - 1);
        }

        // Automatically select the newest record if none selected or if previously on top
        SelectedRecord = record;
        CopyFeedbackMessage = null;
    }

    [RelayCommand]
    public async Task CopyRequestJsonAsync()
    {
        if (SelectedRecord != null && !string.IsNullOrWhiteSpace(SelectedRecord.RequestBodyJson))
        {
            await _clipboardService.SetTextAsync(SelectedRecord.RequestBodyJson);
            ShowFeedback("Request JSON copied to clipboard");
        }
    }

    [RelayCommand]
    public async Task CopyResponseJsonAsync()
    {
        if (SelectedRecord != null && !string.IsNullOrWhiteSpace(SelectedRecord.ResponseBodyJson))
        {
            await _clipboardService.SetTextAsync(SelectedRecord.ResponseBodyJson);
            ShowFeedback("Response JSON copied to clipboard");
        }
    }

    [RelayCommand]
    public async Task CopyEndpointUrlAsync()
    {
        if (SelectedRecord != null && !string.IsNullOrWhiteSpace(SelectedRecord.RequestUrl))
        {
            await _clipboardService.SetTextAsync(SelectedRecord.RequestUrl);
            ShowFeedback("Endpoint URL copied to clipboard");
        }
    }

    [RelayCommand]
    public void ClearHistory()
    {
        Records.Clear();
        SelectedRecord = null;
        CopyFeedbackMessage = null;
    }

    private void ShowFeedback(string msg)
    {
        CopyFeedbackMessage = msg;
    }
}
