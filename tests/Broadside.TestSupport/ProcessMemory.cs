using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Broadside.TestSupport;

/// <summary>
/// The memory the process holds privately: what it has allocated and touched (managed heap, native allocations, pooled buffers),
/// not the pages of files it has mapped, which belong to the file cache and are reclaimable. The measure for the issue #45 bound
/// on process memory when a large file is read.
/// </summary>
/// <remarks>
/// Each platform names it differently: Windows "private bytes" (<see cref="Process.PrivateMemorySize64"/>); Linux the anonymous
/// resident memory (<c>RssAnon</c> in <c>/proc/self/status</c>); macOS the physical footprint (<c>phys_footprint</c> of
/// <c>TASK_VM_INFO</c>, what Activity Monitor shows as Memory; <see cref="Process.PrivateMemorySize64"/> is always 0 there).
/// None of them counts clean file-backed pages of a mapped file.
/// </remarks>
public static class ProcessMemory
{
    private const int TaskVmInfo = 22;
    private const int PhysFootprintIndex = 18;

    /// <summary>Returns the process's private memory in bytes, or <see langword="null"/> on a platform where it cannot be read.</summary>
    /// <returns>The bytes.</returns>
    public static long? PrivateBytes()
    {
        if (OperatingSystem.IsWindows())
        {
            using var process = Process.GetCurrentProcess();
            return process.PrivateMemorySize64;
        }

        if (OperatingSystem.IsLinux())
        {
            foreach (string line in File.ReadLines("/proc/self/status"))
            {
                if (line.StartsWith("RssAnon:", StringComparison.Ordinal))
                {
                    string kilobytes = line["RssAnon:".Length..].Trim().Split(' ')[0];
                    return long.Parse(kilobytes, CultureInfo.InvariantCulture) * 1024;
                }
            }

            return null;
        }

        return OperatingSystem.IsMacOS() ? PhysicalFootprint() : null;
    }

    /// <summary>Reads <c>task_vm_info.phys_footprint</c> (mach/task_info.h, revision 1 and later).</summary>
    private static long? PhysicalFootprint()
    {
        IntPtr library = NativeLibrary.Load("/usr/lib/libSystem.dylib");
        try
        {
            // mach_task_self() is a macro reading the mach_task_self_ variable.
            int task = Marshal.ReadInt32(NativeLibrary.GetExport(library, "mach_task_self_"));
            var taskInfo = Marshal.GetDelegateForFunctionPointer<TaskInfo>(NativeLibrary.GetExport(library, "task_info"));
            long[] info = new long[64];
            int count = info.Length * 2;
            return taskInfo(task, TaskVmInfo, info, ref count) == 0 && count >= (PhysFootprintIndex + 1) * 2 ? info[PhysFootprintIndex] : null;
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    /// <summary><c>kern_return_t task_info(task_name_t, task_flavor_t, task_info_t, mach_msg_type_number_t *)</c>; the count is in 32-bit units.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TaskInfo(int task, int flavor, [In, Out] long[] info, ref int count);
}
