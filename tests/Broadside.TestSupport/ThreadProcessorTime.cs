using System.Runtime.InteropServices;

namespace Broadside.TestSupport;

/// <summary>
/// The processor time the calling thread has used, user and kernel together: unlike the wall clock, it does not grow while the
/// thread waits for a processor, so a time limit charged in it holds on a machine loaded by parallel tests (issue #280). The .NET
/// class library reports it only per process, so this calls the operating system: <c>clock_gettime(CLOCK_THREAD_CPUTIME_ID)</c> on
/// Linux and macOS, <c>GetThreadTimes</c> on Windows. Both are found by symbol lookup at run time (the C library through the main
/// program, which links it), so no library name is hard-coded.
/// </summary>
public static unsafe class ThreadProcessorTime
{
    /// <summary><c>CLOCK_THREAD_CPUTIME_ID</c> in Linux's <c>&lt;linux/time.h&gt;</c>.</summary>
    private const int LinuxThreadClock = 3;

    /// <summary><c>CLOCK_THREAD_CPUTIME_ID</c> in macOS's <c>&lt;time.h&gt;</c>.</summary>
    private const int MacThreadClock = 16;

    /// <summary>The pseudo handle <c>GetCurrentThread</c> returns on Windows.</summary>
    private static readonly nint CurrentThreadHandle = -2;

    private static readonly nint Function = Resolve();

    /// <summary>
    /// Gets the processor time the calling thread has used since it started, or <see langword="null"/> when the platform does not
    /// report it (any other operating system, a 32-bit process, or a failed call).
    /// </summary>
    public static TimeSpan? Current
    {
        get
        {
            if (Function == 0)
            {
                return null;
            }

            if (OperatingSystem.IsWindows())
            {
                long creation, exit, kernel, user;
                var getThreadTimes = (delegate* unmanaged<nint, long*, long*, long*, long*, int>)Function;

                // FILETIME counts 100-nanosecond intervals, the unit of TimeSpan ticks.
                return getThreadTimes(CurrentThreadHandle, &creation, &exit, &kernel, &user) != 0 ? TimeSpan.FromTicks(kernel + user) : null;
            }

            Timespec now;
            var clockGetTime = (delegate* unmanaged<int, Timespec*, int>)Function;
            return clockGetTime(OperatingSystem.IsMacOS() ? MacThreadClock : LinuxThreadClock, &now) == 0
                ? TimeSpan.FromSeconds(now.Seconds) + TimeSpan.FromTicks(now.Nanoseconds / 100)
                : null;
        }
    }

    private static nint Resolve()
    {
        if (OperatingSystem.IsWindows())
        {
            return NativeLibrary.TryLoad("kernel32.dll", out nint kernel32) && NativeLibrary.TryGetExport(kernel32, "GetThreadTimes", out nint export) ? export : 0;
        }

        // struct timespec is two 64-bit fields only in a 64-bit process.
        if ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) && Environment.Is64BitProcess)
        {
            return NativeLibrary.TryGetExport(NativeLibrary.GetMainProgramHandle(), "clock_gettime", out nint export) ? export : 0;
        }

        return 0;
    }

    /// <summary><c>struct timespec</c> of a 64-bit process.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public long Seconds;
        public long Nanoseconds;
    }
}
