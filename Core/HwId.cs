using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace OrbitTweak.Core;

/// <summary>
/// "hw2": a PC fingerprint from hardware only (CPU + mainboard + system serial/UUID out of the SMBIOS firmware table),
/// so it stays the same after a Windows reinstall. Never the MachineGuid, MAC addresses or disk serials.
/// Empty when the firmware has only placeholder serials ("To be filled by O.E.M." …): then the server never moves a key on it.
/// </summary>
internal static class HwId
{
    [DllImport("kernel32.dll")] private static extern uint GetSystemFirmwareTable(uint provider, uint id, byte[]? buffer, uint size);

    /// <summary>What hw2 is built from. Placeholders are already blanked.</summary>
    public sealed record Ids(string BoardMaker, string Board, string BoardSerial, string SysSerial, string Uuid);

    /// <summary>The SMBIOS structure table of this PC, null when it can not be read.</summary>
    public static byte[]? ReadSmbios()
    {
        try
        {
            const uint RSMB = 0x52534D42;   // 'RSMB'
            uint n = GetSystemFirmwareTable(RSMB, 0, null, 0);
            if (n <= 8) return null;
            var buf = new byte[n];
            if (GetSystemFirmwareTable(RSMB, 0, buf, n) != n) return null;
            // RawSMBIOSData: 4 header bytes, DWORD length, then the table
            int len = (int)Math.Min(BitConverter.ToUInt32(buf, 4), n - 8);
            return buf.AsSpan(8, len).ToArray();
        }
        catch { return null; }
    }

    /// <summary>Reads mainboard (type 2) and system (type 1: serial, UUID) from a raw SMBIOS table.</summary>
    public static Ids Parse(byte[] t)
    {
        string maker = "", board = "", boardSerial = "", sysSerial = "", uuid = "";
        bool sysSeen = false, boardSeen = false;
        int i = 0;
        while (i + 4 <= t.Length)
        {
            int type = t[i], len = t[i + 1];
            if (len < 4 || i + len > t.Length) break;
            // the strings follow the formatted part, each ends with 0, the set ends with an extra 0
            var strs = new List<string>();
            int j = i + len;
            if (j + 1 < t.Length && t[j] == 0 && t[j + 1] == 0) j += 2;
            else
                while (j < t.Length)
                {
                    int s = j;
                    while (j < t.Length && t[j] != 0) j++;
                    strs.Add(Encoding.ASCII.GetString(t, s, j - s));
                    j++;
                    if (j >= t.Length || t[j] == 0) { j++; break; }
                }
            string Str(int off) => off < len && t[i + off] is var k && k >= 1 && k <= strs.Count ? strs[k - 1] : "";
            if (type == 1 && !sysSeen)
            {
                sysSeen = true;
                sysSerial = Str(7);
                if (len >= 0x19) uuid = RealUuid(t.AsSpan(i + 8, 16)) ? Convert.ToHexString(t, i + 8, 16) : "";
            }
            else if (type == 2 && !boardSeen) { boardSeen = true; maker = Str(4); board = Str(5); boardSerial = Str(7); }
            else if (type == 127) break;
            i = j;
        }
        return new Ids(Clean(maker), Clean(board), Real(boardSerial) ? Clean(boardSerial) : "", Real(sysSerial) ? Clean(sysSerial) : "", uuid);
    }

    /// <summary>SHA-256 over CPU name + mainboard + serials; empty when no real serial or UUID is there.</summary>
    public static string Hash(Ids ids, string cpu, string boardMakerFallback = "", string boardFallback = "")
    {
        if (ids.BoardSerial == "" && ids.SysSerial == "" && ids.Uuid == "") return "";
        string maker = ids.BoardMaker != "" ? ids.BoardMaker : Clean(boardMakerFallback), board = ids.Board != "" ? ids.Board : Clean(boardFallback);
        var src = $"OrbitTweak-hw2|{Clean(cpu)}|{maker}|{board}|{ids.BoardSerial}|{ids.SysSerial}|{ids.Uuid}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(src))).ToLowerInvariant();
    }

    private static string Clean(string s) => System.Text.RegularExpressions.Regex.Replace(s ?? "", @"\s+", " ").Trim().ToUpperInvariant();

    private static readonly string[] Junk =
    {
        "TO BE FILLED", "FILLED BY", "FILL BY", "O.E.M", "OEM", "DEFAULT STRING", "NOT SPECIFIED", "NOT APPLICABLE", "NOT AVAILABLE", "SERIAL NUMBER",
        "SYSTEM SERIAL", "BASE BOARD", "BASEBOARD", "CHASSIS", "SYSTEM PRODUCT", "NONE", "N/A", "UNKNOWN", "INVALID", "EMPTY", "DEFAULT"
    };

    /// <summary>True for a serial that really tells one PC from another (not a firmware placeholder).</summary>
    public static bool Real(string s)
    {
        var v = Clean(s);
        if (v.Length < 5 || v.Distinct().Count() < 3) return false;   // "0", "SN", "00000000", "FFFFFFFF", "XXXX-XXXX"
        if ("01234567890".Contains(v)) return false;                   // "123456789", "0123456789", "1234567890"
        return !Junk.Any(v.Contains);
    }

    /// <summary>UUID placeholders: all bytes the same (00…, FF…) or all tiny (03000200-0400-0500-0006-000700080009).</summary>
    public static bool RealUuid(ReadOnlySpan<byte> u)
    {
        bool same = true, tiny = true;
        foreach (var b in u) { if (b != u[0]) same = false; if (b >= 0x10) tiny = false; }
        return !same && !tiny;
    }
}
