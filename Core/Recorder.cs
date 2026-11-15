using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Streams;
using WinRT;

namespace OrbitTweak.Core;

// =====================================================================
//  BUILT-IN REPLAY RECORDER
//  Records the game window like OBS / Discord do (Windows Graphics Capture + the GPU video encoder)
//  and keeps only the last seconds in memory. When a clip is wanted, the last seconds are written
//  to an .mp4 in the Videos folder. Nothing is injected into the game.
// =====================================================================

/// <summary>Direct3D 11 device shared by the screen capture and the video encoder.</summary>
internal sealed unsafe class D3DDevice : IDisposable
{
    private static Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    private static Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private static Guid IID_IDXGISurface = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");
    private static Guid IID_ID3D10Multithread = new("9B7E4E00-342C-4106-A19F-4F2704F689F0");

    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int D3D11CreateDevice(IntPtr adapter, int driverType, IntPtr software, uint flags, IntPtr levels, uint levelCount, uint sdk, out IntPtr device, out int level, out IntPtr context);
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
    [DllImport("d3d11.dll", ExactSpelling = true)]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr graphicsSurface);

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess { IntPtr GetInterface([In] ref Guid iid); }

    [StructLayout(LayoutKind.Sequential)]
    private struct TEXTURE2D_DESC { public uint Width, Height, MipLevels, ArraySize; public int Format; public uint SampleCount, SampleQuality; public int Usage; public uint BindFlags, CPUAccessFlags, MiscFlags; }

    private IntPtr _device, _context, _multithread;
    public IDirect3DDevice WinRt { get; private set; } = null!;

    public static D3DDevice Create()
    {
        const uint BGRA = 0x20, VIDEO = 0x800;
        int hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, BGRA | VIDEO, IntPtr.Zero, 0, 7, out var dev, out _, out var ctx);
        if (hr < 0) hr = D3D11CreateDevice(IntPtr.Zero, 1, IntPtr.Zero, BGRA, IntPtr.Zero, 0, 7, out dev, out _, out ctx);
        Marshal.ThrowExceptionForHR(hr);
        var d = new D3DDevice { _device = dev, _context = ctx };
        // the encoder uses this device from its own threads
        if (Marshal.QueryInterface(ctx, ref IID_ID3D10Multithread, out var mt) >= 0)
        {
            d._multithread = mt;
            ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)(*(void***)mt)[5])(mt, 1); // SetMultithreadProtected
        }
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(dev, ref IID_IDXGIDevice, out var dxgi));
        try
        {
            Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var insp));
            try { d.WinRt = MarshalInterface<IDirect3DDevice>.FromAbi(insp); }
            finally { Marshal.Release(insp); }
        }
        finally { Marshal.Release(dxgi); }
        return d;
    }

    /// <summary>Copies a captured frame into its own texture (the capture reuses its buffers).</summary>
    public IDirect3DSurface Copy(IDirect3DSurface src)
    {
        var access = src.As<IDirect3DDxgiInterfaceAccess>();
        var iid = IID_ID3D11Texture2D;
        var srcTex = access.GetInterface(ref iid);
        IntPtr dst = IntPtr.Zero;
        try
        {
            TEXTURE2D_DESC desc;
            ((delegate* unmanaged[Stdcall]<IntPtr, TEXTURE2D_DESC*, void>)(*(void***)srcTex)[10])(srcTex, &desc); // GetDesc
            desc.MipLevels = 1; desc.ArraySize = 1; desc.SampleCount = 1; desc.SampleQuality = 0;
            desc.Usage = 0; desc.BindFlags = 0x8 | 0x20; desc.CPUAccessFlags = 0; desc.MiscFlags = 0;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, TEXTURE2D_DESC*, IntPtr, IntPtr*, int>)(*(void***)_device)[5])(_device, &desc, IntPtr.Zero, &dst); // CreateTexture2D
            Marshal.ThrowExceptionForHR(hr);
            if (_multithread != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, void>)(*(void***)_multithread)[3])(_multithread); // Enter
            try { ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)(*(void***)_context)[47])(_context, dst, srcTex); } // CopyResource
            finally { if (_multithread != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, void>)(*(void***)_multithread)[4])(_multithread); } // Leave
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(dst, ref IID_IDXGISurface, out var surf));
            try
            {
                Marshal.ThrowExceptionForHR(CreateDirect3D11SurfaceFromDXGISurface(surf, out var insp));
                try { return MarshalInterface<IDirect3DSurface>.FromAbi(insp); }
                finally { Marshal.Release(insp); }
            }
            finally { Marshal.Release(surf); }
        }
        finally
        {
            if (dst != IntPtr.Zero) Marshal.Release(dst);
            Marshal.Release(srcTex);
        }
    }

    public void Dispose()
    {
        try { WinRt?.Dispose(); } catch { }
        if (_multithread != IntPtr.Zero) { Marshal.Release(_multithread); _multithread = IntPtr.Zero; }
        if (_context != IntPtr.Zero) { Marshal.Release(_context); _context = IntPtr.Zero; }
        if (_device != IntPtr.Zero) { Marshal.Release(_device); _device = IntPtr.Zero; }
    }
}

/// <summary>
/// System sound ("what you hear") as 48 kHz stereo 16-bit PCM via WASAPI loopback.
/// Windows delivers nothing while no sound plays, so silence is filled in to keep the timeline complete.
/// </summary>
internal sealed class LoopbackAudio
{
    public const int Rate = 48000, Channels = 2, BytesPerFrame = 4;

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint flags, long bufferDuration, long periodicity, IntPtr format, IntPtr session);
        [PreserveSig] int GetBufferSize(out uint frames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long def, out long min);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }
    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePos, out ulong qpcPos);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WAVEFORMATEX { public ushort Tag, Channels; public uint Rate, AvgBytes; public ushort BlockAlign, Bits, Size; }

    /// <summary>Called on the audio thread with interleaved 16-bit stereo samples.</summary>
    public event Action<byte[]>? Data;
    private Thread? _t;
    private volatile bool _run;

    public void Start()
    {
        if (_t != null) return;
        _run = true;
        _t = new Thread(Loop) { IsBackground = true, Name = "OrbitAudio", Priority = ThreadPriority.AboveNormal };
        _t.Start();
    }

    public void Stop()
    {
        _run = false;
        _t?.Join(1500);
        _t = null;
    }

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        long sent = 0; // frames handed out since start
        void Emit(byte[] pcm) { if (pcm.Length == 0) return; sent += pcm.Length / BytesPerFrame; try { Data?.Invoke(pcm); } catch { } }
        void FillSilence()
        {
            long want = clock.ElapsedTicks * Rate / Stopwatch.Frequency;
            if (want - sent > Rate / 25) Emit(new byte[(int)Math.Min(want - sent - Rate / 100, Rate) * BytesPerFrame]);
        }

        while (_run)
        {
            IAudioClient? client = null; IAudioCaptureClient? cap = null;
            IntPtr fmtPtr = IntPtr.Zero, mix = IntPtr.Zero;
            try
            {
                var en = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                if (en.GetDefaultAudioEndpoint(0, 0, out var dev) < 0 || dev == null) throw new InvalidOperationException("no output device");
                var iid = typeof(IAudioClient).GUID;
                Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23, IntPtr.Zero, out var o));
                client = (IAudioClient)o;
                // ask Windows to convert to our format; fall back to the device format
                var want = new WAVEFORMATEX { Tag = 1, Channels = Channels, Rate = Rate, Bits = 16, BlockAlign = BytesPerFrame, AvgBytes = Rate * BytesPerFrame };
                fmtPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WAVEFORMATEX>());
                Marshal.StructureToPtr(want, fmtPtr, false);
                int srcRate = Rate, srcCh = Channels, srcBits = 16; bool srcFloat = false;
                const uint LOOPBACK = 0x00020000, AUTOCONVERT = 0x80000000, SRC_QUALITY = 0x08000000;
                int hr = client.Initialize(0, LOOPBACK | AUTOCONVERT | SRC_QUALITY, 2_000_000, 0, fmtPtr, IntPtr.Zero);
                if (hr < 0)
                {
                    Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23, IntPtr.Zero, out o));
                    client = (IAudioClient)o;
                    Marshal.ThrowExceptionForHR(client.GetMixFormat(out mix));
                    var mf = Marshal.PtrToStructure<WAVEFORMATEX>(mix);
                    srcRate = (int)mf.Rate; srcCh = mf.Channels; srcBits = mf.Bits;
                    srcFloat = mf.Tag == 3 || (mf.Tag == 0xFFFE && mf.Bits == 32);
                    Marshal.ThrowExceptionForHR(client.Initialize(0, LOOPBACK, 2_000_000, 0, mix, IntPtr.Zero));
                }
                var cid = typeof(IAudioCaptureClient).GUID;
                Marshal.ThrowExceptionForHR(client.GetService(ref cid, out var co));
                cap = (IAudioCaptureClient)co;
                Marshal.ThrowExceptionForHR(client.Start());
                int step = Math.Max(1, srcRate / Rate), srcFrame = srcCh * srcBits / 8;

                while (_run)
                {
                    Thread.Sleep(10);
                    while (true)
                    {
                        Marshal.ThrowExceptionForHR(cap.GetNextPacketSize(out var n));
                        if (n == 0) break;
                        Marshal.ThrowExceptionForHR(cap.GetBuffer(out var p, out var frames, out var flags, out _, out _));
                        byte[] pcm;
                        if ((flags & 2) != 0) pcm = new byte[frames / step * BytesPerFrame];
                        else if (srcRate == Rate && srcCh == Channels && srcBits == 16 && !srcFloat)
                        {
                            pcm = new byte[frames * BytesPerFrame];
                            Marshal.Copy(p, pcm, 0, pcm.Length);
                        }
                        else pcm = Convert(p, (int)frames, srcFrame, srcCh, srcBits, srcFloat, step);
                        cap.ReleaseBuffer(frames);
                        Emit(pcm);
                    }
                    FillSilence();
                }
                client.Stop();
            }
            catch
            {
                // device changed or no device: keep the timeline alive with silence and try again
                for (int i = 0; i < 50 && _run; i++) { Thread.Sleep(10); FillSilence(); }
            }
            finally
            {
                if (fmtPtr != IntPtr.Zero) Marshal.FreeHGlobal(fmtPtr);
                if (mix != IntPtr.Zero) Marshal.FreeCoTaskMem(mix);
                if (cap != null) Marshal.ReleaseComObject(cap);
                if (client != null) Marshal.ReleaseComObject(client);
            }
        }
    }

    /// <summary>Device format → 16-bit stereo; "step" drops frames for 96/192 kHz devices.</summary>
    private static unsafe byte[] Convert(IntPtr src, int frames, int srcFrame, int ch, int bits, bool isFloat, int step)
    {
        int outFrames = frames / step;
        var dst = new byte[outFrames * BytesPerFrame];
        byte* s = (byte*)src;
        fixed (byte* d0 = dst)
        {
            short* d = (short*)d0;
            for (int i = 0; i < outFrames; i++)
            {
                byte* f = s + (long)i * step * srcFrame;
                for (int c = 0; c < 2; c++)
                {
                    byte* v = f + Math.Min(c, ch - 1) * (bits / 8);
                    double x = isFloat ? *(float*)v
                        : bits == 16 ? *(short*)v / 32768.0
                        : bits == 24 ? ((v[0] | (v[1] << 8) | ((sbyte)v[2] << 16)) / 8388608.0)
                        : bits == 32 ? *(int*)v / 2147483648.0 : 0;
                    d[i * 2 + c] = (short)Math.Clamp(x * 32767.0, -32768, 32767);
                }
            }
        }
        return dst;
    }
}

internal sealed class ReplayRecorder
{
    public sealed record Options(int Fps = 60, int MaxHeight = 1080, int Mbps = 12, bool Audio = true);

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();

    /// <summary>One finished or running piece of the recording (a small stand-alone mp4 in memory).</summary>
    private sealed class Segment
    {
        private readonly object _l = new();
        private readonly Queue<(IDirect3DSurface s, TimeSpan t)> _video = new();
        private readonly Queue<(byte[] pcm, TimeSpan t)> _audio = new();
        private MediaStreamSourceSampleRequest? _vReq, _aReq;
        private MediaStreamSourceSampleRequestDeferral? _vDef, _aDef;
        private bool _ended;
        private long _audioFrames;
        private readonly TimeSpan _frame;
        private PrepareTranscodeResult? _prep;
        private MediaStreamSource? _src;   // kept so nothing of the running encoder is cleaned up early
        private MediaTranscoder? _tc;
        public InMemoryRandomAccessStream Stream = new();
        public byte[]? Data;               // the finished mp4
        public Task? Done;
        public TimeSpan Start, Length;     // position on the recorder clock, length of the video
        public bool Started, Failed;
        public bool HasAudio;
        public int Width, Height;
        public int Pins;                   // > 0 while a clip is being written from this piece

        public Segment(int fps) { _frame = TimeSpan.FromSeconds(1.0 / fps); }

        public async Task<bool> Prepare(int srcW, int srcH, int outW, int outH, Options o)
        {
            Width = outW; Height = outH; HasAudio = o.Audio;
            var vd = new VideoStreamDescriptor(VideoEncodingProperties.CreateUncompressed(MediaEncodingSubtypes.Bgra8, (uint)srcW, (uint)srcH));
            MediaStreamSource src;
            if (o.Audio)
            {
                var ap = AudioEncodingProperties.CreatePcm(LoopbackAudio.Rate, LoopbackAudio.Channels, 16);
                src = new MediaStreamSource(vd, new AudioStreamDescriptor(ap));
            }
            else src = new MediaStreamSource(vd);
            src.BufferTime = TimeSpan.Zero;
            src.Starting += (_, a) => a.Request.SetActualStartPosition(TimeSpan.Zero);
            src.SampleRequested += OnSample;

            var p = new MediaEncodingProfile();
            p.Container.Subtype = "MPEG4";
            p.Video.Subtype = "H264";
            p.Video.Width = (uint)outW; p.Video.Height = (uint)outH;
            p.Video.Bitrate = (uint)(o.Mbps * 1_000_000);
            p.Video.FrameRate.Numerator = (uint)o.Fps; p.Video.FrameRate.Denominator = 1;
            p.Video.PixelAspectRatio.Numerator = 1; p.Video.PixelAspectRatio.Denominator = 1;
            if (o.Audio) p.Audio = AudioEncodingProperties.CreateAac(LoopbackAudio.Rate, LoopbackAudio.Channels, 192_000);

            var tc = new MediaTranscoder { HardwareAccelerationEnabled = true, AlwaysReencode = true };
            _src = src; _tc = tc;
            _prep = await tc.PrepareMediaStreamSourceTranscodeAsync(src, Stream, p);
            if (!_prep.CanTranscode) { Failed = true; return false; }
            return true;
        }

        /// <summary>After the encoder is done: take the mp4 out of the Windows stream and close the stream.</summary>
        public async Task Seal()
        {
            uint size = (uint)Stream.Size;
            var bytes = new byte[size];
            using (var input = Stream.GetInputStreamAt(0))
            using (var reader = new DataReader(input))
            {
                await reader.LoadAsync(size);
                reader.ReadBytes(bytes);
            }
            Data = bytes;
            Stream.Dispose();
            // let go of the finished encoder right away: it holds threads, GPU memory and one of the few
            // encoder sessions the graphics card offers
            if (_src != null) _src.SampleRequested -= OnSample;
            _prep = null; _src = null; _tc = null;
        }

        public void Begin(TimeSpan start)
        {
            Start = start; Started = true;
            Done = _prep!.TranscodeAsync().AsTask().ContinueWith(t => { if (t.IsFaulted) { Failed = true; _ = t.Exception; } });
        }

        private void OnSample(MediaStreamSource sender, MediaStreamSourceSampleRequestedEventArgs e)
        {
            bool video = e.Request.StreamDescriptor is VideoStreamDescriptor;
            lock (_l)
            {
                if (video)
                {
                    if (_video.Count > 0) e.Request.Sample = Video(_video.Dequeue());
                    else if (_ended) e.Request.Sample = null;
                    else { _vReq = e.Request; _vDef = e.Request.GetDeferral(); }
                }
                else
                {
                    if (_audio.Count > 0) e.Request.Sample = Audio(_audio.Dequeue());
                    else if (_ended) e.Request.Sample = null;
                    else { _aReq = e.Request; _aDef = e.Request.GetDeferral(); }
                }
            }
        }

        private MediaStreamSample Video((IDirect3DSurface s, TimeSpan t) f)
        {
            var smp = MediaStreamSample.CreateFromDirect3D11Surface(f.s, f.t);
            smp.Duration = _frame;
            f.s.Dispose(); // the sample keeps its own reference to the texture
            return smp;
        }

        private static MediaStreamSample Audio((byte[] pcm, TimeSpan t) a)
        {
            var smp = MediaStreamSample.CreateFromBuffer(a.pcm.AsBuffer(), a.t);
            smp.Duration = TimeSpan.FromSeconds(a.pcm.Length / (double)(LoopbackAudio.Rate * LoopbackAudio.BytesPerFrame));
            return smp;
        }

        public void PushVideo(IDirect3DSurface s, TimeSpan now)
        {
            lock (_l)
            {
                if (_ended || !Started) { s.Dispose(); return; }
                var t = now - Start; if (t < TimeSpan.Zero) t = TimeSpan.Zero;
                if (t <= Length && Length > TimeSpan.Zero) t = Length + TimeSpan.FromTicks(1);
                Length = t;
                if (_vDef != null)
                {
                    _vReq!.Sample = Video((s, t)); _vDef.Complete(); _vReq = null; _vDef = null;
                }
                else
                {
                    _video.Enqueue((s, t));
                    while (_video.Count > 8) _video.Dequeue().s.Dispose(); // encoder too slow: drop the oldest
                }
            }
        }

        public void PushAudio(byte[] pcm)
        {
            lock (_l)
            {
                if (_ended || !Started || !HasAudio) return;
                var t = TimeSpan.FromSeconds(_audioFrames / (double)LoopbackAudio.Rate);
                _audioFrames += pcm.Length / LoopbackAudio.BytesPerFrame;
                if (_aDef != null) { _aReq!.Sample = Audio((pcm, t)); _aDef.Complete(); _aReq = null; _aDef = null; }
                else
                {
                    _audio.Enqueue((pcm, t));
                    while (_audio.Count > 400) _audio.Dequeue();
                }
            }
        }

        /// <summary>No more samples: the encoder writes the file end.</summary>
        public void End()
        {
            lock (_l)
            {
                if (_ended) return;
                _ended = true;
                if (_vDef != null) { _vReq!.Sample = null; _vDef.Complete(); _vReq = null; _vDef = null; }
                if (_aDef != null) { _aReq!.Sample = null; _aDef.Complete(); _aReq = null; _aDef = null; }
                // samples still waiting are delivered first; the next request after that gets "null"
            }
        }

        public void Dispose()
        {
            lock (_l) { while (_video.Count > 0) _video.Dequeue().s.Dispose(); _audio.Clear(); }
            try { Stream.Dispose(); } catch { }
            try { if (_src != null) _src.SampleRequested -= OnSample; } catch { }
            _prep = null; _src = null; _tc = null;
            Data = null;
        }
    }

    public event Action<string>? Log;
    public bool Running => _run;
    public string? Error { get; private set; }
    /// <summary>Seconds of video that could be saved right now.</summary>
    public double Buffered
    {
        get
        {
            lock (_segs)
            {
                var cur = _cur;
                double s = cur is { Started: true } ? cur.Length.TotalSeconds : 0;
                return s + _segs.Sum(x => x.Length.TotalSeconds);
            }
        }
    }
    public (int w, int h) Size { get; private set; }

    /// <summary>Length of one piece of the recording (shorter values are only used by tests).</summary>
    public int SegmentSeconds { get; set; } = 20;
    private double _keepSeconds = 60;
    private Options _o = new();
    private volatile bool _run;
    private Thread? _pump;
    private D3DDevice? _d3d;
    private GraphicsCaptureItem? _item;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private LoopbackAudio? _audio;
    private readonly Stopwatch _clock = new();
    private readonly List<Segment> _segs = new();   // finished pieces, oldest first
    private volatile Segment? _cur;
    private Task<Segment?>? _next;
    private volatile bool _rotate;
    private SizeInt32 _size;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private IntPtr _hwnd;

    public static bool Supported { get { try { return GraphicsCaptureSession.IsSupported(); } catch { return false; } } }

    private static GraphicsCaptureItem CreateItem(IntPtr hwnd, bool monitor)
    {
        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var iid = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        var ptr = monitor ? interop.CreateForMonitor(MonitorFromWindow(hwnd == IntPtr.Zero ? GetDesktopWindow() : hwnd, 1), ref iid) : interop.CreateForWindow(hwnd, ref iid);
        try { return GraphicsCaptureItem.FromAbi(ptr); }
        finally { Marshal.Release(ptr); }
    }

    /// <summary>Hides the yellow capture border on Windows 11 (not available on Windows 10).</summary>
    private static unsafe void NoBorder(GraphicsCaptureSession s)
    {
        try
        {
            var iid = new Guid("f2cdd966-22ae-5ea1-9596-3a289344c3be"); // IGraphicsCaptureSession3
            var unk = MarshalInspectable<GraphicsCaptureSession>.FromManaged(s);
            try
            {
                if (Marshal.QueryInterface(unk, ref iid, out var p) < 0) return;
                try { ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)(*(void***)p)[7])(p, 0); } // put_IsBorderRequired(false)
                finally { Marshal.Release(p); }
            }
            finally { Marshal.Release(unk); }
        }
        catch { }
    }

    /// <summary>Starts recording the given window (hwnd = 0: the primary screen). keepSeconds = how far back clips can reach.</summary>
    public async Task<bool> Start(IntPtr hwnd, Options o, double keepSeconds, bool monitor = false)
    {
        if (_run) return true;
        Error = null; _o = o; _hwnd = hwnd; _keepSeconds = Math.Max(SegmentSeconds, keepSeconds);
        try
        {
            if (!Supported) throw new InvalidOperationException("Bildschirmaufnahme wird von dieser Windows-Version nicht unterstützt (mind. Windows 10 1903).");
            _d3d = D3DDevice.Create();
            _item = CreateItem(hwnd, monitor || hwnd == IntPtr.Zero);
            _size = _item.Size;
            if (_size.Width < 64 || _size.Height < 64) throw new InvalidOperationException("Fenster ist zu klein oder minimiert.");
            _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(_d3d.WinRt, DirectXPixelFormat.B8G8R8A8UIntNormalized, 2, _size);
            _session = _pool.CreateCaptureSession(_item);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) { try { _session.IsCursorCaptureEnabled = false; } catch { } }
            NoBorder(_session);
            _item.Closed += (_, _) => { Error = "Fenster wurde geschlossen"; _run = false; };
            _clock.Restart();
            var first = await NewSegment();
            if (first == null) throw new InvalidOperationException("Der Video-Encoder konnte nicht gestartet werden.");
            _run = true;
            _session.StartCapture();
            first.Begin(_clock.Elapsed);
            _cur = first;
            if (o.Audio)
            {
                _audio = new LoopbackAudio();
                _audio.Data += pcm => _cur?.PushAudio(pcm);
                _audio.Start();
            }
            _pumpAlive = true;
            _pump = new Thread(Pump) { IsBackground = true, Name = "OrbitRecorder", Priority = ThreadPriority.AboveNormal };
            _pump.Start();
            Log?.Invoke($"Aufnahme läuft: {Size.w}×{Size.h}, {o.Fps} FPS, {o.Mbps} Mbit/s{(o.Audio ? ", mit Ton" : "")}");
            return true;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            await Stop();
            return false;
        }
    }

    private async Task<Segment?> NewSegment()
    {
        int sw = _size.Width & ~1, sh = _size.Height & ~1;
        int oh = _o.MaxHeight > 0 && sh > _o.MaxHeight ? _o.MaxHeight : sh;
        int ow = (int)Math.Round(sw * (oh / (double)sh)) & ~1;
        Size = (ow, oh);
        var s = new Segment(_o.Fps);
        try { if (await s.Prepare(_size.Width, _size.Height, ow, oh, _o)) return s; }
        catch (Exception ex) { Error = ex.Message; }
        s.Dispose();
        return null;
    }

    private void Pump()
    {
        long tick = Stopwatch.Frequency / _o.Fps, next = _clock.ElapsedTicks;
        Direct3D11CaptureFrame? held = null;
        try
        {
            while (_run)
            {
                long wait = next - _clock.ElapsedTicks;
                if (wait > 0) Thread.Sleep((int)Math.Max(1, wait * 1000 / Stopwatch.Frequency));
                next += tick;
                if (_clock.ElapsedTicks - next > tick * 4) next = _clock.ElapsedTicks;

                Direct3D11CaptureFrame? f;
                while ((f = _pool!.TryGetNextFrame()) != null) { held?.Dispose(); held = f; }
                var cur = _cur;
                if (held == null || cur == null) continue;
                if (held.ContentSize.Width != _size.Width || held.ContentSize.Height != _size.Height)
                {
                    // window size changed: start over with the new size
                    Error = "resize"; _run = false; break;
                }
                var now = _clock.Elapsed;
                try { cur.PushVideo(_d3d!.Copy(held.Surface), now); }
                catch (Exception ex) { Error = ex.Message; _run = false; break; }
                if (cur.Failed) { Error = "Encoder-Fehler"; _run = false; break; }

                // prepare the next piece shortly before it is needed, then switch without a gap
                var age = (now - cur.Start).TotalSeconds;
                if (_next == null && (age >= SegmentSeconds - 2 || _rotate)) _next = NewSegment();
                if (_next is { IsCompleted: true } && (age >= SegmentSeconds || _rotate))
                {
                    var n = _next.Result; _next = null; _rotate = false;
                    if (n == null) { Error ??= "Encoder-Fehler"; _run = false; break; }
                    n.Begin(now);
                    _cur = n;
                    cur.End();
                    Finish(cur);
                }
            }
        }
        finally
        {
            held?.Dispose();
            // Whatever ended the recording (game closed or minimized, window resized, Stop): the piece that was being
            // recorded is finished like the others, so its last seconds can still be saved until Stop() clears the buffer.
            var last = _cur;
            if (last is { Started: true }) { _cur = null; try { last.End(); Finish(last); } catch { } }
            _pumpAlive = false;
        }
    }

    private volatile bool _pumpAlive;
    private int _finishing; // pieces that have ended but are not in the list of finished pieces yet

    private async void Finish(Segment s)
    {
        Interlocked.Increment(ref _finishing);
        try
        {
            if (s.Done != null) await s.Done;
            if (!s.Failed) await s.Seal();
        }
        catch { s.Failed = true; }
        lock (_segs)
        {
            if (!s.Failed && s.Data != null && s.Length > TimeSpan.Zero) _segs.Add(s); else s.Dispose();
            // forget what is older than the clip length
            var edge = _clock.Elapsed - TimeSpan.FromSeconds(_keepSeconds);
            while (_segs.Count > 0 && _segs[0].Pins == 0 && _segs[0].Start + _segs[0].Length < edge) { _segs[0].Dispose(); _segs.RemoveAt(0); }
        }
        Interlocked.Decrement(ref _finishing);
        // release the finished encoder now instead of whenever the GC feels like it
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private int _stopping;

    public async Task Stop()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 1) return; // already being stopped
        try { await StopCore(); }
        finally { _stopping = 0; }
    }

    private async Task StopCore()
    {
        _run = false;
        var p = _pump; _pump = null;
        if (p != null && p != Thread.CurrentThread) await Task.Run(() => p.Join(2000));
        _audio?.Stop(); _audio = null;
        var cur = _cur; _cur = null;
        if (cur != null) { cur.End(); try { if (cur.Done != null) await Task.WhenAny(cur.Done, Task.Delay(3000)); } catch { } cur.Dispose(); }
        if (_next != null) { try { (await _next)?.Dispose(); } catch { } _next = null; }
        try { _session?.Dispose(); } catch { }
        try { _pool?.Dispose(); } catch { }
        _session = null; _pool = null; _item = null;
        _d3d?.Dispose(); _d3d = null;
        // a clip that is being written right now still needs the finished pieces: let it end first
        bool free = false;
        try { free = await _saveLock.WaitAsync(TimeSpan.FromSeconds(25)); } catch { }
        try { lock (_segs) { foreach (var s in _segs) s.Dispose(); _segs.Clear(); } }
        finally { if (free) _saveLock.Release(); }
        _clock.Stop();
    }

    /// <summary>
    /// Saves the last "before" seconds plus the next "after" seconds as an mp4. Returns the file path, or null with Error set.
    /// </summary>
    public async Task<string?> Save(double before, double after, string folder, string name)
    {
        // The recording may have just ended (the game was closed or minimized right after a kill): what is in the
        // buffer can still be saved until Stop() clears it.
        bool left; lock (_segs) left = _segs.Count > 0;
        if (!_run && !left && !_pumpAlive && Volatile.Read(ref _finishing) == 0) { Error ??= "Aufnahme läuft nicht"; return null; }
        var mark = _clock.Elapsed;
        var from = mark - TimeSpan.FromSeconds(before);
        var to = mark + TimeSpan.FromSeconds(after);
        if (from < TimeSpan.Zero) from = TimeSpan.Zero;
        await _saveLock.WaitAsync();
        var temp = Path.Combine(Path.GetTempPath(), "OrbitTweak-rec-" + Guid.NewGuid().ToString("N")[..8]);
        var parts = new List<Segment>();
        string? part = null;
        try
        {
            // wait for the "after" part, then close the running piece so everything up to "to" is on hand
            while (_run && _clock.Elapsed < to) await Task.Delay(50);
            _rotate = true;
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (true)
            {
                lock (_segs)
                {
                    parts = _segs.Where(s => s.Start + s.Length > from && s.Start < to).ToList();
                    bool complete = parts.Count > 0 && parts[^1].Start + parts[^1].Length >= to - TimeSpan.FromMilliseconds(250);
                    // recording ended: take what there is, once the last piece has arrived in the list
                    bool ended = !_run && !_pumpAlive && Volatile.Read(ref _finishing) == 0;
                    if (complete || DateTime.UtcNow > deadline || ended) { foreach (var s in parts) s.Pins++; break; } // keep them until the clip is written
                }
                await Task.Delay(60);
            }
            if (parts.Count == 0) { Error = "Noch nichts aufgenommen"; return null; }

            Directory.CreateDirectory(temp);
            Directory.CreateDirectory(folder);
            var comp = new MediaComposition();
            for (int i = 0; i < parts.Count; i++)
            {
                var file = Path.Combine(temp, $"part{i}.mp4");
                await File.WriteAllBytesAsync(file, parts[i].Data ?? throw new InvalidOperationException("Aufnahme wurde gerade beendet"));
                var clip = await MediaClip.CreateFromFileAsync(await StorageFile.GetFileFromPathAsync(file));
                var len = clip.OriginalDuration;
                var head = from - parts[i].Start; var tail = parts[i].Start + parts[i].Length - to;
                if (head > TimeSpan.Zero && head < len) clip.TrimTimeFromStart = head;
                if (tail > TimeSpan.Zero && tail < len - clip.TrimTimeFromStart) clip.TrimTimeFromEnd = tail;
                comp.Clips.Add(clip);
            }
            var safe = string.Concat(name.Split(Path.GetInvalidFileNameChars()));
            var target = Path.Combine(folder, safe + ".mp4");
            for (int n = 2; File.Exists(target); n++) target = Path.Combine(folder, $"{safe} ({n}).mp4");
            // written under another name first: a clip only shows up as "….mp4" once it is complete, whatever happens meanwhile
            var dir = await StorageFolder.GetFolderFromPathAsync(folder);
            part = target + ".part";
            foreach (var old in Directory.EnumerateFiles(folder, "*.mp4.part"))
                try { if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddMinutes(-10)) File.Delete(old); } catch { } // left behind by an earlier, cut-off save
            var outFile = await dir.CreateFileAsync(Path.GetFileName(part), CreationCollisionOption.ReplaceExisting);

            var p = MediaEncodingProfile.CreateMp4(VideoEncodingQuality.HD1080p);
            p.Video.Width = (uint)parts[0].Width; p.Video.Height = (uint)parts[0].Height;
            p.Video.Bitrate = (uint)(_o.Mbps * 1_000_000);
            p.Video.FrameRate.Numerator = (uint)_o.Fps; p.Video.FrameRate.Denominator = 1;
            if (!_o.Audio) p.Audio = null;
            // (MediaTrimmingPreference.Fast was tried: it encodes the clip again just the same and is hardly quicker)
            var res = await comp.RenderToFileAsync(outFile, MediaTrimmingPreference.Precise, p);
            if (res != TranscodeFailureReason.None)
            {
                Error = "Clip konnte nicht geschrieben werden (" + res + ")";
                return null;
            }
            File.Move(part, target, true);
            part = null;
            return target;
        }
        catch (Exception ex) { Error = ex.Message; return null; }
        finally
        {
            if (part != null) try { File.Delete(part); } catch { }
            lock (_segs) foreach (var s in parts) s.Pins--;
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            _saveLock.Release();
        }
    }

}
