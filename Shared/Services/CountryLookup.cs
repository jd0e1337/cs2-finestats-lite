using System.IO.Compression;
using System.Net;
using System.Net.Sockets;

namespace Finestats.Services;

// Local DB-IP Country Lite CSV (ip_start,ip_end,country), immutable after load.
// No network requests, player addresses, or lookup history are retained.
public sealed class CountryLookup
{
    private sealed record Block(UInt128 Start, UInt128 End, string Country);
    private readonly Block[] _v4, _v6;
    private CountryLookup(Block[] v4, Block[] v6) { _v4 = v4; _v6 = v6; }
    public static CountryLookup Load(string path, CancellationToken ct = default)
    {
        using var file = File.OpenRead(path);
        using Stream input = path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ? new GZipStream(file, CompressionMode.Decompress) : file;
        using var reader = new StreamReader(input);
        var v4 = new List<Block>(); var v6 = new List<Block>();
        while (reader.ReadLine() is string line)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = line.Split(',').Select(x => x.Trim().Trim('"')).ToArray();
            if (fields.Length != 3 || !IPAddress.TryParse(fields[0], out var start) || !IPAddress.TryParse(fields[1], out var end) || start.AddressFamily != end.AddressFamily || fields[2].Length != 2 || fields[2].Any(c => c is < 'A' or > 'Z'))
                throw new InvalidDataException("Invalid country CSV row.");
            var block = new Block(Number(start), Number(end), fields[2]);
            if (block.Start > block.End || v4.Count + v6.Count >= 2_000_000) throw new InvalidDataException("Invalid or oversized country database.");
            (start.AddressFamily == AddressFamily.InterNetwork ? v4 : v6).Add(block);
        }
        Block[] Prepare(List<Block> blocks)
        {
            blocks.Sort((a, b) => a.Start.CompareTo(b.Start));
            for (int i = 1; i < blocks.Count; i++) if (blocks[i].Start <= blocks[i - 1].End) throw new InvalidDataException("Overlapping country ranges.");
            return blocks.ToArray();
        }
        if (v4.Count + v6.Count == 0) throw new InvalidDataException("Empty country database.");
        return new(Prepare(v4), Prepare(v6));
    }
    public string? Find(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        if (!IPAddress.TryParse(address, out var ip))
        {
            if (!IPEndPoint.TryParse(address, out var endpoint)) return null;
            ip = endpoint.Address;
        }
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var bytes = ip.GetAddressBytes();
        bool v4 = bytes.Length == 4;
        if (IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal) return null;
        if (v4)
        {
            if (bytes[0] is 0 or 10 or 127 || bytes[0] >= 224 || bytes[0] == 169 && bytes[1] == 254 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 || bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 100 && bytes[1] is >= 64 and <= 127) return null;
            if (bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2 || bytes[0] == 198 && bytes[1] is 18 or 19 || bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100 || bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) return null;
        }
        else if ((bytes[0] & 0xe0) != 0x20 || bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0d && bytes[3] == 0xb8) return null;
        var blocks = v4 ? _v4 : _v6;
        var value = Number(ip);
        int low = 0, high = blocks.Length - 1;
        while (low <= high)
        {
            int mid = low + (high - low) / 2; var block = blocks[mid];
            if (value < block.Start) high = mid - 1;
            else if (value > block.End) low = mid + 1;
            else return block.Country is "ZZ" or "XX" ? null : block.Country;
        }
        return null;
    }
    private static UInt128 Number(IPAddress ip)
    {
        UInt128 value = 0;
        foreach (var b in ip.GetAddressBytes()) value = (value << 8) | b;
        return value;
    }
}
