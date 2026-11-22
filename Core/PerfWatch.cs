using System.Runtime.InteropServices;

namespace OrbitTweak.Core;

/// <summary>
/// The counters of the Engpass-Analyse (2.31), read with PDH the way the Task Manager reads them: one query with three
/// wildcard counters (3D engines per process, dedicated VRAM per adapter, each logical processor), opened when a game
/// runs and closed when it stops. Read only, nothing touches the game: performance counters are Windows' own numbers.
/// One look every 2 s on a pool thread; a look while the game is not in front reads nothing at all.
/// </summary>
internal sealed unsafe class PerfWatch : IDisposable
{
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint PdhOpenQueryW(string? source, IntPtr user, out IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr user, out IntPtr counter);
    [DllImport("pdh.dll", ExactSpelling = true)] private static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);
    [DllImport("pdh.dll", ExactSpelling = true)] private static extern uint PdhRemoveCounter(IntPtr counter);
    [DllImport("pdh.dll", ExactSpelling = true)] private static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

    private const uint PDH_MORE_DATA = 0x800007D2, PDH_NO_DATA = 0x800007D5;
    private const uint PDH_FMT_DOUBLE = 0x00000200, PDH_FMT_NOCAP100 = 0x00008000;

    [StructLayout(LayoutKind.Sequential)] private struct FmtValue { public uint CStatus; public double Value; }
    /// <summary>PDH_FMT_COUNTERVALUE_ITEM_W: the instance name (it points into the same buffer) and its value.</summary>
    [StructLayout(LayoutKind.Sequential)] private struct Item { public IntPtr Name; public FmtValue V; }

    /// <summary>Said on the page when Windows gives no counters at all.</summary>
    public const string NoCounters = "Windows liefert gerade keine Leistungswerte. Meist sind die Leistungsindikatoren beschädigt: „lodctr /R“ in einer Eingabeaufforderung als Administrator repariert sie.";

    /// <summary>The game process this query looks at.</summary>
    public int Pid { get; }
    /// <summary>Why there are no values (German, for the page); null while all is well.</summary>
    public string? Error { get; private set; }

    private readonly object _lock = new();
    private IntPtr _q, _gpu, _mem, _cpu;
    private bool _opened, _primed, _gone, _gpuPart;
    private int _fails, _gpuEmpty;
    private IntPtr _buf; private uint _bufLen;
    private Dictionary<string, double>? _vram;   // dedicated VRAM in MB per adapter LUID, as the counters write it
    private readonly List<(string, double)> _items = new();

    public PerfWatch(int pid) { Pid = pid; }

    /// <summary>
    /// One look. front false: nothing is read, and the next look only primes the counters again (a rate over the time the
    /// game was in the background would be wrong). Returns null when this look brings no values.
    /// </summary>
    public PerfSample? Sample(bool front)
    {
        lock (_lock)
        {
            if (_gone) return null;
            if (!front) { _primed = false; return null; }
            if (!_opened) Open();
            if (_q == IntPtr.Zero) return null;
            uint st = PdhCollectQueryData(_q);
            if (st != 0 && st != PDH_NO_DATA)
            {
                _primed = false;
                if (++_fails >= 5) Error = NoCounters;   // ten seconds without values: said on the page
                return null;
            }
            _fails = 0; Error = null;
            if (!_primed) { _primed = true; return null; }   // rate counters need two looks

            var cpu = PerfWatchLogic.CpuLoad(Read(_cpu));
            if (cpu is not { } c) return null;
            double? gpu = null; bool own = false; string? luid = null;
            if (_gpu != IntPtr.Zero)
            {
                var items = Read(_gpu);
                if (items.Count > 0) _gpuEmpty = 0;
                // the narrow path ("*engtype_3D") was taken but finds nothing: some systems may not match part of a name,
                // then the whole object is read (the 3D engines are picked out in PerfWatchLogic either way)
                if (items.Count == 0 && _gpuPart && ++_gpuEmpty >= 3 && PdhAddEnglishCounterW(_q, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out var all) == 0)
                {
                    PdhRemoveCounter(_gpu); _gpu = all; _gpuPart = false; _primed = false;
                }
                var (o, t, l) = PerfWatchLogic.GpuLoad(items, Pid);
                gpu = o ?? t; own = o != null; luid = l;
            }
            double? vramMb = null, vramUsed = null;
            if (_mem != IntPtr.Zero && PerfWatchLogic.AdapterUse(Read(_mem), luid) is { } m)
            {
                double total = _vram != null && _vram.TryGetValue(m.luid, out var mb) ? mb : 0;
                // the adapter of the counters not among DXGI's (unusual): the card Windows picks for games, if it is the one in use
                if (total <= 0 && _vram is { Count: 1 }) total = _vram.Values.First();
                if (total > 0) { vramMb = total; vramUsed = m.bytes / (1024.0 * 1024.0); }
            }
            var (ramTotal, ramAvail, _) = Native.Memory();
            const double MB = 1024.0 * 1024.0;
            return new PerfSample(gpu, own, c.core, c.total, vramMb, vramUsed, ramTotal / MB, ramTotal > ramAvail ? (ramTotal - ramAvail) / MB : 0);
        }
    }

    private void Open()
    {
        _opened = true;
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out _q) != 0) { _q = IntPtr.Zero; Error = NoCounters; return; }
            // "Processor Information" knows processor groups (more than 64 threads); the older object as a fallback
            if (PdhAddEnglishCounterW(_q, @"\Processor Information(*)\% Processor Time", IntPtr.Zero, out _cpu) != 0
                && PdhAddEnglishCounterW(_q, @"\Processor(*)\% Processor Time", IntPtr.Zero, out _cpu) != 0)
            {
                PdhCloseQuery(_q); _q = IntPtr.Zero; Error = NoCounters; return;
            }
            // GPU counters exist with WDDM 2 drivers (Windows 10 1709 and later); only the 3D engines are needed
            _gpuPart = PdhAddEnglishCounterW(_q, @"\GPU Engine(*engtype_3D)\Utilization Percentage", IntPtr.Zero, out _gpu) == 0;
            if (!_gpuPart && PdhAddEnglishCounterW(_q, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _gpu) != 0) _gpu = IntPtr.Zero;
            if (PdhAddEnglishCounterW(_q, @"\GPU Adapter Memory(*)\Dedicated Usage", IntPtr.Zero, out _mem) != 0) _mem = IntPtr.Zero;
            if (_mem != IntPtr.Zero) _vram = AdapterVram();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            _q = IntPtr.Zero; Error = NoCounters;
        }
    }

    /// <summary>The values of one wildcard counter from the last collect, only those PDH calls valid.</summary>
    private List<(string, double)> Read(IntPtr counter)
    {
        _items.Clear();
        if (counter == IntPtr.Zero) return _items;
        const uint fmt = PDH_FMT_DOUBLE | PDH_FMT_NOCAP100;
        uint size = _bufLen, n;
        uint st = PdhGetFormattedCounterArrayW(counter, fmt, ref size, out n, _buf);
        if (st == PDH_MORE_DATA)
        {
            // too small: asked with size 0, PDH tells the size it needs (with a buffer that is too small it may not).
            // The buffer is kept for the next looks; it only grows.
            size = 0;
            if (PdhGetFormattedCounterArrayW(counter, fmt, ref size, out n, IntPtr.Zero) != PDH_MORE_DATA || size == 0 || size > 64 << 20) return _items;
            if (_buf != IntPtr.Zero) Marshal.FreeHGlobal(_buf);
            _bufLen = size + 4096;   // room for a few more instances next time
            _buf = Marshal.AllocHGlobal((int)_bufLen);
            size = _bufLen;
            st = PdhGetFormattedCounterArrayW(counter, fmt, ref size, out n, _buf);
        }
        if (st != 0 || _buf == IntPtr.Zero) return _items;   // no values this time (no instance yet, counter gone)
        int stride = Marshal.SizeOf<Item>();
        for (int i = 0; i < n; i++)
        {
            var it = Marshal.PtrToStructure<Item>(_buf + i * stride);
            if (it.V.CStatus > 1) continue;   // only PDH_CSTATUS_VALID_DATA and PDH_CSTATUS_NEW_DATA
            _items.Add((Marshal.PtrToStringUni(it.Name) ?? "", it.V.Value));
        }
        return _items;
    }

    /// <summary>DedicatedVideoMemory in MB of every hardware adapter, by its LUID ("0x00000000_0x0000d1b6").</summary>
    private static Dictionary<string, double> AdapterVram()
    {
        var map = new Dictionary<string, double>();
        try
        {
            var iid1 = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");   // IDXGIFactory1
            if (CreateDXGIFactory1(ref iid1, out var f) < 0 || f == IntPtr.Zero) return map;
            try
            {
                var enum1 = (delegate* unmanaged<IntPtr, uint, IntPtr*, int>)(*(IntPtr**)f)[12];   // IDXGIFactory1::EnumAdapters1
                byte* desc = stackalloc byte[320];                                                  // DXGI_ADAPTER_DESC1 (312 bytes)
                for (uint i = 0; i < 16; i++)
                {
                    IntPtr a;
                    if (enum1(f, i, &a) < 0 || a == IntPtr.Zero) break;
                    try
                    {
                        var getDesc1 = (delegate* unmanaged<IntPtr, byte*, int>)(*(IntPtr**)a)[10];     // IDXGIAdapter1::GetDesc1
                        if (getDesc1(a, desc) < 0 || (*(uint*)(desc + 304) & 2) != 0) continue;         // Flags: DXGI_ADAPTER_FLAG_SOFTWARE
                        double mb = *(ulong*)(desc + 272) / (1024.0 * 1024.0);                          // DedicatedVideoMemory (SIZE_T)
                        var luid = PerfWatchLogic.Luid(*(uint*)(desc + 296), *(int*)(desc + 300));      // AdapterLuid: LowPart, HighPart
                        if (mb > 0) map[luid] = mb;
                    }
                    finally { Marshal.Release(a); }
                }
            }
            finally { Marshal.Release(f); }
        }
        catch { }
        return map;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_gone) return;
            _gone = true;
            try { if (_q != IntPtr.Zero) PdhCloseQuery(_q); } catch { }
            _q = IntPtr.Zero;
            if (_buf != IntPtr.Zero) { Marshal.FreeHGlobal(_buf); _buf = IntPtr.Zero; _bufLen = 0; }
        }
    }
}
