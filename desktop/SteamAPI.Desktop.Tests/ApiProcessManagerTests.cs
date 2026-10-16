using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SteamAPI.Desktop.Services;
using Xunit;

namespace SteamAPI.Desktop.Tests;

[CollectionDefinition("ApiProcessManager", DisableParallelization = true)]
public class ApiProcessManagerTestCollection
{
}

[Collection("ApiProcessManager")]
public class ApiProcessManagerTests : IDisposable
{
    public ApiProcessManagerTests()
    {
        WaitForPortToRelease(5271, 3000);
    }

    public void Dispose()
    {
        WaitForPortToRelease(5271, 3000);
    }

    private static void WaitForPortToRelease(int port, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                using var client = new TcpClient();
                var result = client.BeginConnect("127.0.0.1", port, null, null);
                var success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(150));
                if (success && client.Connected)
                {
                    client.EndConnect(result);
                    Thread.Sleep(100);
                    continue;
                }
                return;
            }
            catch
            {
                return;
            }
        }
    }

    [Fact]
    public async Task StartAsync_WhenExecutableDoesNotExist_SetsStateToFailedAndStopsCleanly()
    {
        using var manager = new ApiProcessManager();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe");

        var started = await manager.StartAsync(nonExistentPath);

        Assert.False(started);
        Assert.Equal(ApiProcessState.Failed, manager.State);
        Assert.Null(manager.ProcessId);
        Assert.NotNull(manager.LastError);

        await manager.StopAsync();
        Assert.Equal(ApiProcessState.Stopped, manager.State);
    }

    [Fact]
    public async Task StartAsync_WhenChildExitsImmediately_CleansUpAndSetsFailedState()
    {
        string? exitPath = null;
        if (OperatingSystem.IsWindows())
        {
            var hostnamePath = Path.Combine(Environment.SystemDirectory, "hostname.exe");
            if (File.Exists(hostnamePath))
            {
                exitPath = hostnamePath;
            }
        }
        else
        {
            if (File.Exists("/usr/bin/true")) exitPath = "/usr/bin/true";
            else if (File.Exists("/bin/true")) exitPath = "/bin/true";
        }

        if (exitPath == null)
        {
            return;
        }

        using var manager = new ApiProcessManager();

        var started = await manager.StartAsync(exitPath).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(started);
        Assert.Equal(ApiProcessState.Failed, manager.State);
        Assert.Null(manager.ProcessId);
        Assert.NotNull(manager.LastError);
    }

    [Fact]
    public async Task StartAsync_WhenPortOccupied_FailsGracefullyWithoutSendingHttpRequests()
    {
        TcpListener? dummyListener = null;
        var bytesReceived = 0;
        using var listenerCts = new CancellationTokenSource();

        try
        {
            dummyListener = new TcpListener(IPAddress.Loopback, 5271);
            dummyListener.Start();

            // Background worker to accept connections on dummyListener and count any received bytes
            _ = Task.Run(async () =>
            {
                while (!listenerCts.IsCancellationRequested)
                {
                    try
                    {
                        using var client = await dummyListener.AcceptTcpClientAsync(listenerCts.Token);
                        var stream = client.GetStream();
                        var buffer = new byte[1024];
                        int read;
                        while ((read = await stream.ReadAsync(buffer, listenerCts.Token)) > 0)
                        {
                            Interlocked.Add(ref bytesReceived, read);
                        }
                    }
                    catch
                    {
                        break;
                    }
                }
            });
        }
        catch (SocketException)
        {
            // Port 5271 already occupied in the environment; proceed to test against it
        }

        try
        {
            using var manager = new ApiProcessManager();
            var started = await manager.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(started);
            Assert.Equal(ApiProcessState.Failed, manager.State);
            Assert.Null(manager.ProcessId);
            Assert.NotNull(manager.LastError);
            Assert.Contains("Port 5271 is already in use by another application", manager.LastError);

            // Crucial verification: zero HTTP payload/bytes were sent to the occupied listener
            Assert.Equal(0, Volatile.Read(ref bytesReceived));

            // Verify the external listener was not terminated or crashed
            if (dummyListener != null)
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync("127.0.0.1", 5271).WaitAsync(TimeSpan.FromSeconds(2));
                Assert.True(probe.Connected);
            }
        }
        finally
        {
            listenerCts.Cancel();
            dummyListener?.Stop();
        }
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_ReturnsTrueIdempotently()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var manager = new ApiProcessManager();
        var started1 = await manager.StartAsync().WaitAsync(TimeSpan.FromSeconds(12));
        Assert.True(started1, $"Expected SteamAPI to start, LastError: {manager.LastError}");
        Assert.Equal(ApiProcessState.Running, manager.State);

        var started2 = await manager.StartAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(started2);
        Assert.Equal(ApiProcessState.Running, manager.State);

        await manager.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ApiProcessState.Stopped, manager.State);
    }

    [Fact]
    public async Task StartAsync_ConcurrentCalls_SafeStartupAndShutdown()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var manager = new ApiProcessManager();
        var t1 = manager.StartAsync();
        var t2 = manager.StartAsync();

        var results = await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(15));
        // At least one returned true or both resolved to the running instance
        Assert.True(results[0] || results[1]);
        Assert.Equal(ApiProcessState.Running, manager.State);
        Assert.NotNull(manager.ProcessId);

        await manager.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ApiProcessState.Stopped, manager.State);
    }

    [Fact]
    public async Task StopAsync_DuringRunningState_DoesNotDeadlockWithProcessExitedEvent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // Repeat 3 cycles against the real companion SteamAPI binary to stress-test StopAsync() vs Process.Exited
        for (int i = 0; i < 3; i++)
        {
            using var manager = new ApiProcessManager();
            var started = await manager.StartAsync().WaitAsync(TimeSpan.FromSeconds(12));
            Assert.True(started, $"Expected SteamAPI to start on iteration {i}, LastError: {manager.LastError}");
            Assert.Equal(ApiProcessState.Running, manager.State);

            var pid = manager.ProcessId;
            Assert.NotNull(pid);

            // StopAsync must complete without AB-BA deadlock and terminate the child process
            await manager.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ApiProcessState.Stopped, manager.State);
            Assert.Null(manager.ProcessId);

            // Verify child process is terminated
            try
            {
                using var proc = Process.GetProcessById(pid.Value);
                Assert.True(proc.WaitForExit(2000));
            }
            catch (ArgumentException)
            {
                // Process already exited completely
            }
        }
    }

    [Fact]
    public async Task StopAsync_WhileStarting_CancelsStartupAndTerminatesChildImmediately()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var manager = new ApiProcessManager();
        var startTask = manager.StartAsync();

        // Give Process.Start a brief moment to spawn the process before cancelling
        await Task.Delay(150);

        await manager.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var startResult = await startTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(startResult);
        Assert.Equal(ApiProcessState.Stopped, manager.State);
        Assert.Null(manager.ProcessId);
    }

    [Fact]
    public async Task Dispose_TerminatesOwnedProcessCleanly()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        int? pid;
        using (var manager = new ApiProcessManager())
        {
            var started = await manager.StartAsync().WaitAsync(TimeSpan.FromSeconds(12));
            Assert.True(started, $"Expected SteamAPI to start, LastError: {manager.LastError}");
            pid = manager.ProcessId;
            Assert.NotNull(pid);
        }

        // After Dispose, process must be terminated
        try
        {
            using var proc = Process.GetProcessById(pid.Value);
            Assert.True(proc.WaitForExit(3000));
        }
        catch (ArgumentException)
        {
            // Already exited
        }
    }
}
