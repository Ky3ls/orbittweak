using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OrbitTweak.Core;

internal static class Native
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                     ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO { public int cbSize; public long i64Size; public long i64NumItems; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint Low; public int High; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TOKEN_PRIVILEGES { public uint Count; public LUID Luid; public uint Attr; }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES state, int len, IntPtr prev, IntPtr retLen);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    // ---------- CPU ----------
    private static long _pIdle, _pKernel, _pUser;

    public static double CpuUsage()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        long di = idle - _pIdle, dk = kernel - _pKernel, du = user - _pUser;
        _pIdle = idle; _pKernel = kernel; _pUser = user;
        long total = dk + du;
        if (total <= 0) return 0;
        return Math.Clamp((total - di) * 100.0 / total, 0, 100);
    }

    // ---------- RAM ----------
    public static (ulong total, ulong avail, uint load) Memory()
    {
        var m = new MEMORYSTATUSEX();
        return GlobalMemoryStatusEx(m) ? (m.ullTotalPhys, m.ullAvailPhys, m.dwMemoryLoad) : (0, 0, 0);
    }

    private static bool EnablePrivilege(string name)
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0020 | 0x0008, out var token)) return false;
        try
        {
            if (!LookupPrivilegeValue(null, name, out var luid)) return false;
            var tp = new TOKEN_PRIVILEGES { Count = 1, Luid = luid, Attr = 0x2 };
            return AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero)
                   && Marshal.GetLastWin32Error() == 0;
        }
        finally { CloseHandle(token); }
    }

    /// <summary>Trims working sets of all processes and (optionally) purges the standby list. Returns freed bytes.</summary>
    public static long CleanMemory(bool purgeStandby)
    {
        var before = (long)Memory().avail;
        EnablePrivilege("SeIncreaseQuotaPrivilege");
        EnablePrivilege("SeDebugPrivilege");
        foreach (var p in Process.GetProcesses())
        {
            try { if (p.Id > 4) EmptyWorkingSet(p.Handle); }
            catch { /* protected / exited */ }
            finally { p.Dispose(); }
        }
        if (purgeStandby && EnablePrivilege("SeProfileSingleProcessPrivilege"))
        {
            int cmd = 4; // MemoryPurgeStandbyList
            NtSetSystemInformation(80, ref cmd, sizeof(int)); // SystemMemoryListInformation
        }
        Thread.Sleep(400);
        return Math.Max(0, (long)Memory().avail - before);
    }

    // ---------- Recycle bin ----------
    public static long RecycleBinSize()
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        return SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }

    public static void EmptyRecycleBin() => SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4);
}
