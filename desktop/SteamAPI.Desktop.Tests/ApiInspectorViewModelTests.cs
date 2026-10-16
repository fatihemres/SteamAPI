using SteamAPI.Desktop.Models;
using SteamAPI.Desktop.Services;
using SteamAPI.Desktop.ViewModels;
using Xunit;

namespace SteamAPI.Desktop.Tests;

public class ApiInspectorViewModelTests
{
    private class FakeClipboard : IClipboardService
    {
        public string? CopiedText { get; private set; }
        public Task SetTextAsync(string text)
        {
            CopiedText = text;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void AddRecord_InsertsNewestAtBeginningAndSelectsIt()
    {
        var clip = new FakeClipboard();
        var vm = new ApiInspectorViewModel(clip);

        var record1 = new ApiInspectionRecord { RequestUrl = "http://localhost/1" };
        var record2 = new ApiInspectionRecord { RequestUrl = "http://localhost/2" };

        vm.AddRecord(record1);
        vm.AddRecord(record2);

        Assert.Equal(2, vm.Records.Count);
        Assert.Equal("http://localhost/2", vm.Records[0].RequestUrl);
        Assert.Equal(record2, vm.SelectedRecord);
    }

    [Fact]
    public void AddRecord_CapsHistoryAtFiftyItems()
    {
        var clip = new FakeClipboard();
        var vm = new ApiInspectorViewModel(clip);

        for (int i = 0; i < 60; i++)
        {
            vm.AddRecord(new ApiInspectionRecord { RequestUrl = $"http://localhost/{i}" });
        }

        Assert.Equal(50, vm.Records.Count);
        // The most recent record is 59
        Assert.Equal("http://localhost/59", vm.Records[0].RequestUrl);
        // The oldest preserved is 10 (0..9 evicted)
        Assert.Equal("http://localhost/10", vm.Records[49].RequestUrl);
    }

    [Fact]
    public void ClearHistory_EmptiesRecordsAndResetsSelection()
    {
        var clip = new FakeClipboard();
        var vm = new ApiInspectorViewModel(clip);

        vm.AddRecord(new ApiInspectionRecord { RequestUrl = "http://localhost/1" });
        Assert.Single(vm.Records);

        vm.ClearHistory();

        Assert.Empty(vm.Records);
        Assert.Null(vm.SelectedRecord);
    }

    [Fact]
    public async Task CopyCommands_CopyJsonAndUrlToClipboard()
    {
        var clip = new FakeClipboard();
        var vm = new ApiInspectorViewModel(clip);

        var record = new ApiInspectionRecord
        {
            RequestUrl = "http://localhost:5271/steam",
            RequestBodyJson = "{\"game\":\"portal\"}",
            ResponseBodyJson = "[{\"name\":\"Portal\"}]"
        };
        vm.AddRecord(record);

        await vm.CopyRequestJsonAsync();
        Assert.Equal("{\"game\":\"portal\"}", clip.CopiedText);

        await vm.CopyResponseJsonAsync();
        Assert.Equal("[{\"name\":\"Portal\"}]", clip.CopiedText);

        await vm.CopyEndpointUrlAsync();
        Assert.Equal("http://localhost:5271/steam", clip.CopiedText);
    }
}
