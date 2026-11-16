using System.IO;
using System.Text;

namespace OrbitTweak.Core;

/// <summary>
/// Reads the table of contents of an RPF7 archive. Only an unencrypted TOC (OPEN / NONE / CFXP) can be read;
/// for NG and AES archives the header still tells the encryption, the name tells the rest.
/// </summary>
internal sealed class RpfToc
{
    public const uint Magic = 0x52504637, OPEN = 0x4E45504F, CFXP = 0x50584643, AES = 0x0FFFFFF9, NG = 0x0FEFFFFF;
    /// <summary>First four bytes of a loose game resource (.ytd/.ydr/.yft …).</summary>
    public const uint Rsc7 = 0x37435352;

    public sealed class E
    {
        public string Name = ""; public bool Dir, Resource;
        public int First, Count;            // directory: children
        public long Offset;                 // absolute byte offset of the data in the stream
        public uint StoredSize, Size;       // binary: StoredSize 0 = not compressed, Size = real size
        public uint Sys, Gfx;               // resource page flags
        public bool Encrypted;              // binary entry flag (1 = AES/NG-encrypted data)
        public List<E> Kids = new();
        public RpfToc? Nested;              // nested .rpf whose TOC could be read
        public uint NestedEnc;              // encryption word of a nested .rpf
    }

    public uint Encryption;
    public E Root = new() { Dir = true };
    public bool Readable => Encryption is OPEN or 0 or CFXP;

    public static bool IsRpf(string path)
    {
        try { using var f = File.OpenRead(path); ReadEncryption(f, 0); return true; }
        catch { return false; }
    }

    public static uint ReadEncryption(Stream s, long start)
    {
        var h = new byte[16]; s.Position = start;
        if (s.Read(h, 0, 16) != 16 || BitConverter.ToUInt32(h, 0) != Magic) throw new InvalidDataException("kein RPF7-Archiv");
        return BitConverter.ToUInt32(h, 12);
    }

    public static RpfToc Read(Stream s, long start = 0, int depth = 0)
    {
        var t = new RpfToc { Encryption = ReadEncryption(s, start) };
        if (!t.Readable) return t;
        var h = new byte[16]; s.Position = start; s.ReadExactly(h);
        int count = BitConverter.ToInt32(h, 4), nameLen = BitConverter.ToInt32(h, 8);
        if (count <= 0 || count > 500_000 || nameLen < 0 || nameLen > 0x100000) throw new InvalidDataException("RPF-Archiv ist beschädigt");
        var toc = new byte[count * 16]; s.ReadExactly(toc);
        var names = new byte[nameLen]; s.ReadExactly(names);
        string Name(int off)
        {
            if (off < 0 || off >= nameLen) return "";
            int end = Array.IndexOf(names, (byte)0, off);
            return Encoding.ASCII.GetString(names, off, (end < 0 ? nameLen : end) - off);
        }
        var all = new E[count];
        for (int i = 0; i < count; i++)
        {
            var e = new E(); int o = i * 16;
            if (BitConverter.ToUInt32(toc, o + 4) == 0x7FFFFF00)
            {
                e.Dir = true; e.Name = Name((int)BitConverter.ToUInt32(toc, o));
                e.First = (int)BitConverter.ToUInt32(toc, o + 8); e.Count = (int)BitConverter.ToUInt32(toc, o + 12);
            }
            else
            {
                ulong v = BitConverter.ToUInt64(toc, o);
                e.Name = Name((int)(v & 0xFFFF));
                e.StoredSize = (uint)(v >> 16) & 0xFFFFFF;
                uint block = (uint)(v >> 40) & 0xFFFFFF;
                e.Resource = (block & 0x800000) != 0;
                e.Offset = start + (long)(block & 0x7FFFFF) * 512;
                if (e.Resource) { e.Sys = BitConverter.ToUInt32(toc, o + 8); e.Gfx = BitConverter.ToUInt32(toc, o + 12); e.Size = e.StoredSize; }
                else { e.Size = BitConverter.ToUInt32(toc, o + 8); e.Encrypted = BitConverter.ToUInt32(toc, o + 12) == 1; }
            }
            all[i] = e;
        }
        foreach (var d in all.Where(x => x.Dir))
            for (int k = d.First; k < d.First + d.Count && k < count; k++) if (k > 0) d.Kids.Add(all[k]);
        t.Root = all[0];
        // a nested archive stored without compression has its own header at its offset
        if (depth < 3)
            foreach (var f in all.Where(x => !x.Dir && !x.Resource && x.StoredSize == 0 && x.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase)))
            {
                try { f.NestedEnc = ReadEncryption(s, f.Offset); f.Nested = Read(s, f.Offset, depth + 1); } catch { }
            }
        return t;
    }

    public IEnumerable<(string path, E e)> Files(E? d = null, string prefix = "", int depth = 0)
    {
        if (depth > 24) yield break;                              // a broken archive could point a folder at itself
        foreach (var k in (d ?? Root).Kids)
            if (k.Dir) foreach (var x in Files(k, prefix + k.Name + "/", depth + 1)) yield return x;
            else yield return (prefix + k.Name, k);
    }

    /// <summary>Reads a small binary entry (assembly.xml, setup2.xml …).</summary>
    public static byte[]? ReadSmall(Stream s, E e, int max = 1 << 20)
    {
        if (e.Dir || e.Resource || e.StoredSize != 0 || e.Encrypted || e.Size > max) return null;
        var b = new byte[e.Size]; s.Position = e.Offset; s.ReadExactly(b);
        return b;
    }
}

/// <summary>
/// Writes an OPEN RPF7 archive straight to disk: only the TOC is kept in memory, file data is copied in 1 MB steps,
/// so packing a 300 MB pack needs no huge arrays. Byte-identical to <see cref="RpfWriter"/> for the same input.
/// </summary>
internal sealed class StreamRpfWriter
{
    private sealed class Node
    {
        public string Name = ""; public List<Node>? Kids;
        public Func<Stream>? Open; public long Length;
        public bool Resource; public uint Sys, Gfx; public int First; public long Block;
    }
    private readonly Node _root = new() { Kids = new() };

    private Node Dir(string path)
    {
        var n = _root;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var c = n.Kids!.FirstOrDefault(x => x.Name == part && x.Kids != null);
            if (c == null) { c = new Node { Name = part, Kids = new() }; n.Kids!.Add(c); }
            n = c;
        }
        return n;
    }

    public void AddBytes(string dir, string name, byte[] data) => Dir(dir).Kids!.Add(new Node { Name = name, Open = () => new MemoryStream(data, false), Length = data.Length });
    public void AddFile(string dir, string name, string path) => Dir(dir).Kids!.Add(new Node { Name = name, Open = () => File.OpenRead(path), Length = new FileInfo(path).Length });

    /// <summary>A loose game resource: the file starts with the 16-byte RSC7 header holding the page flags FiveM asks for.</summary>
    public void AddResourceFile(string dir, string name, string path)
    {
        var h = new byte[16];
        using (var f = File.OpenRead(path)) f.ReadExactly(h);
        if (BitConverter.ToUInt32(h, 0) != RpfToc.Rsc7) throw new InvalidDataException(name + " ist keine Spiel-Ressource");
        Dir(dir).Kids!.Add(new Node { Name = name, Open = () => File.OpenRead(path), Length = new FileInfo(path).Length, Resource = true, Sys = BitConverter.ToUInt32(h, 8), Gfx = BitConverter.ToUInt32(h, 12) });
    }

    /// <summary>Adds a file from disk: as resource entry when it is an RSC7 resource, otherwise as plain file.</summary>
    public void AddAuto(string dir, string name, string path)
    {
        if (IsRsc7(path)) AddResourceFile(dir, name, path); else AddFile(dir, name, path);
    }

    public static bool IsRsc7(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var h = new byte[4];
            return f.Read(h, 0, 4) == 4 && BitConverter.ToUInt32(h, 0) == RpfToc.Rsc7;
        }
        catch { return false; }
    }

    public void Write(Stream o)
    {
        // every folder's children sit next to each other, sorted by name (the game searches them by name)
        var list = new List<Node> { _root };
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Kids is not { } ch) continue;
            ch.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            list[i].First = list.Count; list.AddRange(ch);
        }
        var names = new MemoryStream(); var nameOff = new Dictionary<Node, int>();
        foreach (var n in list) { nameOff[n] = (int)names.Length; names.Write(Encoding.ASCII.GetBytes(n.Name)); names.WriteByte(0); }
        while (names.Length % 16 != 0) names.WriteByte(0);
        if (names.Length > 0xFFFF) throw new InvalidOperationException("Zu viele Dateien für ein Archiv");

        long block = (16 + list.Count * 16L + names.Length + 511) / 512;
        foreach (var n in list.Where(n => n.Open != null))
        {
            if (n.Resource && n.Length >= 0xFFFFFF) throw new InvalidOperationException(n.Name + " ist größer als 16 MB, das kann FiveM in einem Paket nicht laden");
            if (!n.Resource && n.Length > uint.MaxValue) throw new InvalidOperationException(n.Name + " ist größer als 4 GB");
            n.Block = block; block += (n.Length + 511) / 512;
        }
        if (block > 0x7FFFFF) throw new InvalidOperationException("Archiv wäre größer als 4 GB");

        var toc = new byte[16 + list.Count * 16];
        void U32(int at, uint v) => BitConverter.TryWriteBytes(toc.AsSpan(at), v);
        U32(0, RpfToc.Magic); U32(4, (uint)list.Count); U32(8, (uint)names.Length); U32(12, RpfToc.OPEN);
        for (int i = 0; i < list.Count; i++)
        {
            var n = list[i]; int e = 16 + i * 16;
            if (n.Kids != null) { U32(e, (uint)nameOff[n]); U32(e + 4, 0x7FFFFF00); U32(e + 8, (uint)n.First); U32(e + 12, (uint)n.Kids.Count); }
            else if (n.Resource)
            {
                ulong v = (uint)nameOff[n] | ((ulong)n.Length << 16) | ((ulong)(n.Block | 0x800000) << 40);
                BitConverter.TryWriteBytes(toc.AsSpan(e), v); U32(e + 8, n.Sys); U32(e + 12, n.Gfx);
            }
            else
            {
                ulong v = (uint)nameOff[n] | ((ulong)n.Block << 40);           // stored size 0 = not compressed
                BitConverter.TryWriteBytes(toc.AsSpan(e), v); U32(e + 8, (uint)n.Length); U32(e + 12, 0);
            }
        }
        o.Write(toc); names.Position = 0; names.CopyTo(o);
        var buf = new byte[1 << 20];
        foreach (var n in list.Where(n => n.Open != null))
        {
            Pad(o, n.Block * 512);
            using var src = n.Open!();
            long left = n.Length; int r;
            while (left > 0 && (r = src.Read(buf, 0, (int)Math.Min(buf.Length, left))) > 0) { o.Write(buf, 0, r); left -= r; }
            if (left != 0) throw new IOException(n.Name + " hat sich beim Lesen verändert");
        }
        Pad(o, block * 512);
    }

    /// <summary>Writes to "target.tmp" first and moves it into place, so a failed build never leaves half a file.</summary>
    public void Save(string target)
    {
        var tmp = target + ".tmp";
        try
        {
            using (var o = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20)) Write(o);
            File.Move(tmp, target, true);
        }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }

    private static void Pad(Stream o, long to)
    {
        if (o.Position > to) throw new InvalidOperationException("RPF-Layout stimmt nicht");
        if (o.Position < to) o.Write(new byte[to - o.Position]);
    }
}
