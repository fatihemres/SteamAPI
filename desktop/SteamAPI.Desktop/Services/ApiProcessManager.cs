using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace SteamAPI.Desktop.Services;

public class ApiProcessManager : IApiProcessManager
{
    private Process? _process;
    private IntPtr _jobHandle = IntPtr.Zero;
    private CancellationTokenSource? _startupCts;
    private readonly object _lock = new();
    private bool _isStopping;

    public ApiProcessState State { get; private set; } = ApiProcessState.Stopped;
    public string? LastError { get; private set; }

    public int? ProcessId
    {
        get
        {
            lock (_lock)
            {
                return _process != null && !SafeHasExited(_process) ? TryGetProcessId(_process) : null;
            }
        }
    }

    public string BoundUrl { get; private set; } = "http://127.0.0.1:5271";
    public event Action<ApiProcessState>? OnStateChanged;

    public async Task<bool> StartAsync(string? executablePath = null, CancellationToken cancellationToken = default)
    {
        CancellationToken linkedToken;
        lock (_lock)
        {
            if (State == ApiProcessState.Running && _process != null && !SafeHasExited(_process))
            {
                return true;
            }

            if (State == ApiProcessState.Starting)
            {
                return false;
            }

            _isStopping = false;
            try { _startupCts?.Cancel(); } catch { }
            try { _startupCts?.Dispose(); } catch { }
            _startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linkedToken = _startupCts.Token;
            State = ApiProcessState.Starting;
        }

        OnStateChanged?.Invoke(ApiProcessState.Starting);

        Process? proc = null;
        try
        {
            // Check if port is already in use by another application
            if (await IsPortInUseAsync("127.0.0.1", 5271, linkedToken).ConfigureAwait(false))
            {
                LastError = "Port 5271 is already in use by another application. Companion SteamAPI cannot be started. Please free port 5271 or configure an external API URL in Settings.";
                CleanupOwnedResources();
                SetState(ApiProcessState.Failed);
                return false;
            }

            var resolvedPath = ResolveApiExecutablePath(executablePath);
            if (resolvedPath == null)
            {
                LastError = "Could not locate SteamAPI executable or DLL. Please start SteamAPI manually or configure path in Settings.";
                SetState(ApiProcessState.Failed);
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                WorkingDirectory = Path.GetDirectoryName(resolvedPath) ?? AppContext.BaseDirectory
            };

            if (resolvedPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.FileName = "dotnet";
                startInfo.Arguments = $"\"{resolvedPath}\" --urls http://127.0.0.1:5271";
            }
            else
            {
                startInfo.FileName = resolvedPath;
                startInfo.Arguments = "--urls http://127.0.0.1:5271";
            }

            proc = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            proc.Exited += OnProcessExited;
            proc.Start();

            bool abortImmediately = false;
            lock (_lock)
            {
                _process = proc;
                if (_isStopping || linkedToken.IsCancellationRequested)
                {
                    abortImmediately = true;
                }
                else if (OperatingSystem.IsWindows())
                {
                    if (_jobHandle == IntPtr.Zero)
                    {
                        _jobHandle = WindowsJobObject.CreateJobWithKillOnClose();
                    }
                    WindowsJobObject.AssignProcess(_jobHandle, proc);
                }
            }

            if (abortImmediately)
            {
                CleanupOwnedResources();
                SetState(ApiProcessState.Stopped, notifyListeners: false);
                return false;
            }

            // Wait for port to become active (up to 10 seconds)
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(500, linkedToken).ConfigureAwait(false);
                if (await IsPortInUseAsync("127.0.0.1", 5271, linkedToken).ConfigureAwait(false))
                {
                    if (_isStopping)
                    {
                        return false;
                    }
                    SetState(ApiProcessState.Running);
                    return true;
                }

                if (SafeHasExited(proc))
                {
                    if (_isStopping)
                    {
                        return false;
                    }
                    var exitCode = TryGetExitCode(proc);
                    LastError = exitCode.HasValue
                        ? $"SteamAPI failed to start (exited with code {exitCode.Value})."
                        : "SteamAPI failed to start.";
                    CleanupOwnedResources();
                    SetState(ApiProcessState.Failed);
                    return false;
                }
            }

            if (_isStopping)
            {
                return false;
            }
            LastError = "SteamAPI process started but did not bind to port 5271 within 10 seconds.";
            CleanupOwnedResources();
            SetState(ApiProcessState.Failed);
            return false;
        }
        catch (OperationCanceledException)
        {
            CleanupOwnedResources();
            SetState(ApiProcessState.Stopped, notifyListeners: false);
            return false;
        }
        catch (Exception ex)
        {
            if (proc != null && _process == null)
            {
                TerminateAndDisposeProcess(proc);
            }
            CleanupOwnedResources();
            if (!_isStopping)
            {
                LastError = $"Failed to start child API process: {ex.Message}";
                SetState(ApiProcessState.Failed);
            }
            return false;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process exitedProcess) return;

        bool shouldNotifyStopped = false;
        lock (_lock)
        {
            if (_isStopping || !ReferenceEquals(_process, exitedProcess))
            {
                return;
            }

            var exitCode = TryGetExitCode(exitedProcess);
            LastError = exitCode.HasValue
                ? $"SteamAPI child process exited unexpectedly with code {exitCode.Value}."
                : "SteamAPI child process exited unexpectedly.";
            State = ApiProcessState.Stopped;
            shouldNotifyStopped = true;
        }

        if (shouldNotifyStopped)
        {
            OnStateChanged?.Invoke(ApiProcessState.Stopped);
        }
    }

    public Task StopAsync()
    {
        CleanupOwnedResources();
        SetState(ApiProcessState.Stopped);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    private void CleanupOwnedResources()
    {
        Process? procToStop;
        IntPtr jobToClose;
        CancellationTokenSource? ctsToCancel;

        lock (_lock)
        {
            _isStopping = true;
            procToStop = _process;
            _process = null;
            jobToClose = _jobHandle;
            _jobHandle = IntPtr.Zero;
            ctsToCancel = _startupCts;
            _startupCts = null;
        }

        try { ctsToCancel?.Cancel(); } catch { }
        try { ctsToCancel?.Dispose(); } catch { }

        if (procToStop != null)
        {
            TerminateAndDisposeProcess(procToStop);
        }

        if (jobToClose != IntPtr.Zero)
        {
            WindowsJobObject.CloseJob(jobToClose);
        }
    }

    private void TerminateAndDisposeProcess(Process proc)
    {
        try { proc.Exited -= OnProcessExited; } catch { }
        try
        {
            if (!SafeHasExited(proc))
            {
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(3000);
            }
        }
        catch
        {
            // Ignore kill/wait errors on shutdown
        }
        finally
        {
            try { proc.Dispose(); } catch { }
        }
    }

    private static bool SafeHasExited(Process proc)
    {
        try { return proc.HasExited; }
        catch { return true; }
    }

    private static int? TryGetExitCode(Process proc)
    {
        try { return proc.ExitCode; }
        catch { return null; }
    }

    private static int? TryGetProcessId(Process proc)
    {
        try { return proc.Id; }
        catch { return null; }
    }

    private void SetState(ApiProcessState state, bool notifyListeners = true)
    {
        bool changed;
        lock (_lock)
        {
            changed = State != state;
            State = state;
        }
        if (changed && notifyListeners)
        {
            OnStateChanged?.Invoke(state);
        }
    }

    private static async Task<bool> IsPortInUseAsync(string host, int port, CancellationToken cancellationToken)
    {
        try
        {
            using var tcpClient = new TcpClient();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromMilliseconds(400));
            await tcpClient.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveApiExecutablePath(string? customPath)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            return File.Exists(customPath) ? customPath : null;
        }

        var baseDir = AppContext.BaseDirectory;

        // Check packaged / sibling directories
        var candidates = new[]
        {
            Path.Combine(baseDir, "api", "SteamAPI.exe"),
            Path.Combine(baseDir, "api", "SteamAPI.dll"),
            Path.Combine(baseDir, "SteamAPI.exe"),
            Path.Combine(baseDir, "SteamAPI.dll"),
            // Development fallback relative to solution
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "SteamAPI", "bin", "Debug", "net10.0", "SteamAPI.dll")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "SteamAPI", "bin", "Release", "net10.0", "SteamAPI.dll"))
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }

        return null;
    }

    #region Win32 Job Object Interop

    private static class WindowsJobObject
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInformationClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private const int JobObjectExtendedLimitInformation = 9;
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [StructLayout(LayoutKind.Sequential)]
        private struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryLimit;
            public UIntPtr PeakJobMemoryLimit;
        }

        public static IntPtr CreateJobWithKillOnClose()
        {
            if (!OperatingSystem.IsWindows()) return IntPtr.Zero;

            try
            {
                var hJob = CreateJobObject(IntPtr.Zero, null);
                if (hJob == IntPtr.Zero) return IntPtr.Zero;

                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

                var length = (uint)Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                var ptr = Marshal.AllocHGlobal((int)length);
                try
                {
                    Marshal.StructureToPtr(info, ptr, false);
                    if (!SetInformationJobObject(hJob, JobObjectExtendedLimitInformation, ptr, length))
                    {
                        CloseHandle(hJob);
                        return IntPtr.Zero;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }

                return hJob;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        public static bool AssignProcess(IntPtr hJob, Process process)
        {
            if (!OperatingSystem.IsWindows() || hJob == IntPtr.Zero) return false;
            try
            {
                return AssignProcessToJobObject(hJob, process.Handle);
            }
            catch
            {
                return false;
            }
        }

        public static void CloseJob(IntPtr hJob)
        {
            if (!OperatingSystem.IsWindows() || hJob == IntPtr.Zero) return;
            try
            {
                CloseHandle(hJob);
            }
            catch { }
        }
    }

    #endregion
}
