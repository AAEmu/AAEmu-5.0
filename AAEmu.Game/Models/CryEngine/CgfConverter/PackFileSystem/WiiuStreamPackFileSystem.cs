using System.Text;
using System.Text.RegularExpressions;
using CgfConverter.Services;
using CgfConverter.Utils;
using Extensions;

namespace CgfConverter.PackFileSystem;

public sealed class WiiuStreamPackFileSystem : IPackFileSystem, IDisposable
{
    private readonly TaggedLogger Log = new("WiiuStreamPackFileSystem");
    public const string PackFileNameSuffix = ".wiiu.stream";

    private readonly Stream _stream;
    private readonly Mutex _streamMutex = new();
    private readonly Dictionary<string, FileEntry> _entries = new();
    private bool _disposed;

    public WiiuStreamPackFileSystem(Stream stream, Dictionary<string, string> options)
    {
        _stream = stream;

        using var reader = new EndiannessChangeableBinaryReader(_stream, Encoding.UTF8, true);
        reader.IsBigEndian = true;

        if (reader.ReadUInt32() != 0x7374726d) // 'strm'
            throw new InvalidDataException();

        options = options.ToDictionary(x => x.Key, x => x.Value);

        var altCostume = false;
        if (options.Remove("alt", out var altCostumeString))
            altCostume = altCostumeString.IsTrueyString(true);

        foreach (var (k, v) in options)
            Log.W("Ignoring unknown parameter \"{0}\" with value \"{1}\".", k, v);

        while (reader.PeekChar() != -1)
        {
            var entry = new FileEntry(reader);
            if ((entry.Variant >= 0 && altCostume) || (entry.Variant <= 0 && !altCostume))
                _entries[entry.InnerPath.ToLowerInvariant()] = entry;

            reader.BaseStream.Seek(
                entry.CompressedSize == 0 ? entry.DecompressedSize : entry.CompressedSize,
                SeekOrigin.Current);
        }
    }

    private void Dispose(bool disposing)
    {
        if (_disposed || !disposing)
            return;
        _stream.Dispose();
        _streamMutex.Dispose();
        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public Stream GetStream(string path) => new MemoryStream(ReadAllBytes(path));

    public bool Exists(string path) => _entries.ContainsKey(path.ToLowerInvariant());

    public string[] Glob(string pattern)
    {
        var regexPattern = new Regex(
            "^" +
            string.Join("[\\s\\S]*", Regex.Split(pattern, "\\*{2,}").Select(x =>
                string.Join("[^/\\\\]*", x.Split("*").Select(y =>
                    string.Join("[^/\\\\]", y.Split('?').Select(
                        Regex.Escape)))))) +
            "$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        return _entries.Values.Where(x => regexPattern.IsMatch(x.InnerPath)).Select(x => x.InnerPath).ToArray();
    }

    public byte[] ReadAllBytes(string path)
    {
        if (!_entries.TryGetValue(path.ToLowerInvariant(), out FileEntry entry))
            throw new FileNotFoundException();

        var buffer = new byte[entry.DecompressedSize];

        switch (_stream)
        {
            case FileStream fs:
                using (var fileStream = new FileStream(fs.Name, FileMode.Open, FileAccess.Read))
                    return ReadAllBytesFromStream(fileStream, entry, buffer);
            case MemoryStream ms:
                return ReadAllBytesFromStream(new MemoryStream(ms.GetBuffer(), 0, ms.Capacity, false), entry, buffer);
            default:
                using (var ms = new MemoryStream())
                {
                    _stream.Seek(0, SeekOrigin.Begin);
                    _stream.CopyTo(ms);
                    ms.Seek(0, SeekOrigin.Begin);
                    return ReadAllBytesFromStream(ms, entry, buffer);
                }
        }
    }

    private byte[] ReadAllBytesFromStream(Stream stream, FileEntry entry, byte[] buffer)
    {
        stream.Seek(entry.Offset, SeekOrigin.Begin);
        if (entry.CompressedSize == 0)
        {
            if (stream.Read(buffer, 0, buffer.Length) != buffer.Length)
                throw new EndOfStreamException();
        }
        else
        {
            using var ms = new MemoryStream(buffer, true);
            while (ms.Position < buffer.Length && stream.Position < entry.Offset + entry.CompressedSize)
            {
                var size = stream.ReadCryIntWithFlag(out var backFlag);

                if (backFlag)
                {
                    var copyLength = stream.ReadCryInt();
                    var copyBaseOffset = (int)ms.Position - copyLength;

                    for (var remaining = size + 3; remaining > 0; remaining -= copyLength)
                        ms.Write(buffer, copyBaseOffset, Math.Min(copyLength, remaining));
                }
                else
                {
                    if (stream.Read(buffer, (int)ms.Position, size) != size)
                        throw new EndOfStreamException();
                    ms.Position += size;
                }
            }

            if (ms.Position != buffer.Length)
                throw new EndOfStreamException();
        }

        return buffer;
    }

    private readonly struct FileEntry : IComparable<FileEntry>
    {
        public readonly int CompressedSize;
        public readonly int DecompressedSize;
        public readonly int Hash;
        public readonly ushort UnknownValue1;
        public readonly short Variant;
        public readonly string InnerPath;
        public readonly long Offset;

        /// <summary>
        /// Create a dummy FileEntry for bisecting purposes.
        /// </summary>
        /// <param name="innerPath">Inner path.</param>
        public FileEntry(string innerPath)
        {
            InnerPath = FileHandlingExtensions.CombineAndNormalizePath(innerPath);
            Offset = CompressedSize = DecompressedSize = Hash = UnknownValue1 = 0;
            Variant = 0;
        }

        public FileEntry(BinaryReader reader)
        {
            reader.ReadInto(out CompressedSize);
            reader.ReadInto(out DecompressedSize);
            reader.ReadInto(out Hash);
            reader.ReadInto(out UnknownValue1);
            reader.ReadInto(out Variant);
            InnerPath = FileHandlingExtensions.CombineAndNormalizePath(reader.ReadCString());
            Offset = reader.BaseStream.Position;
        }

        public int CompareTo(FileEntry other) =>
            string.Compare(InnerPath, other.InnerPath, StringComparison.InvariantCultureIgnoreCase);
    }
}
