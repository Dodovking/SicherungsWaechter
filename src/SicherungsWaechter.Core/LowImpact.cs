using System.Runtime.InteropServices;

namespace SicherungsWaechter.Core;

/// <summary>Keeps background work gentle: Windows background priority (CPU, disk, memory), copy speed limit, short pauses while scanning.</summary>
public static class LowImpact
{
    public const long CopyBytesPerSecond = 40L * 1024 * 1024;
    public const int ScanBatch = 200;
    public static readonly TimeSpan ScanPause = TimeSpan.FromMilliseconds(15);
    public const int BatteryScanFactor = 4;

    public static Func<bool> OnBattery { get; set; } = () => false;

    private const int ThreadModeBackgroundBegin = 0x00010000;
    private const int ThreadModeBackgroundEnd = 0x00020000;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetThreadPriority(IntPtr thread, int priority);

    public static IDisposable Enter(bool enabled)
    {
        if (!enabled || !OperatingSystem.IsWindows()) return Scope.None;
        try { return SetThreadPriority(GetCurrentThread(), ThreadModeBackgroundBegin) ? new Scope() : Scope.None; }
        catch { return Scope.None; }
    }

    private sealed class Scope : IDisposable
    {
        public static readonly IDisposable None = new NoScope();
        public void Dispose()
        {
            try { SetThreadPriority(GetCurrentThread(), ThreadModeBackgroundEnd); } catch { }
        }
    }

    private sealed class NoScope : IDisposable
    {
        public void Dispose() { }
    }
}
