using System.IO.Compression;
using System.Text;

namespace HatModLoader.Source.FileProxies
{
    // Minimal read-only ZIP parser. Uses only DeflateStream from System.IO.Compression
    // (not ZipArchive), so it never touches Mono's SharpCompress backend or CP437 registry.
    internal class ZipReader : IDisposable
    {
        internal class Entry
        {
            public string Name;
            public bool IsDirectory;
            public uint CompressedSize;
            public uint UncompressedSize;
            public ushort Compression;   // 0=stored, 8=deflate
            public uint LocalHeaderOffset;
            public DateTime LastModified;
        }

        private readonly Stream _stream;
        private readonly List<Entry> _entries = new();

        public IReadOnlyList<Entry> Entries => _entries;

        private const uint SignatureCentral = 0x02014b50;

        public ZipReader(string path)
        {
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            ReadCentralDirectory();
        }

        private void ReadCentralDirectory()
        {
            // Search for end-of-central-directory record from the end of file.
            // EOCD is at least 22 bytes; comment can be up to 65535 bytes.
            var fileLen = _stream.Length;
            var searchLen = (int)Math.Min(fileLen, 65557);
            var buf = new byte[searchLen];
            _stream.Seek(fileLen - searchLen, SeekOrigin.Begin);
            _stream.Read(buf, 0, buf.Length);

            var eocdOffset = -1;
            for (var i = buf.Length - 22; i >= 0; i--)
            {
                if (buf[i] == 0x50 && buf[i+1] == 0x4b && buf[i+2] == 0x05 && buf[i+3] == 0x06)
                {
                    eocdOffset = i;
                    break;
                }
            }

            if (eocdOffset < 0)
            {
                throw new InvalidDataException("Not a valid ZIP file.");
            }

            var eocd = new BinaryReader(new MemoryStream(buf, eocdOffset, buf.Length - eocdOffset));
            eocd.ReadUInt32(); // signature
            eocd.ReadUInt16(); // disk number
            eocd.ReadUInt16(); // disk with CD
            eocd.ReadUInt16(); // entries on disk
            var entryCount = eocd.ReadUInt16();
            eocd.ReadUInt32(); // CD size
            var cdOffset = eocd.ReadUInt32();

            // Read central directory
            _stream.Seek(cdOffset, SeekOrigin.Begin);
            var cdReader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);

            for (var i = 0; i < entryCount; i++)
            {
                var sig = cdReader.ReadUInt32();
                if (sig != SignatureCentral) break;

                cdReader.ReadUInt16(); // version made by
                cdReader.ReadUInt16(); // version needed
                var flags = cdReader.ReadUInt16();
                var compression = cdReader.ReadUInt16();
                var modTime = cdReader.ReadUInt16();
                var modDate = cdReader.ReadUInt16();
                cdReader.ReadUInt32(); // crc32
                var compSize = cdReader.ReadUInt32();
                var uncompSize = cdReader.ReadUInt32();
                var nameLen = cdReader.ReadUInt16();
                var extraLen = cdReader.ReadUInt16();
                var commentLen = cdReader.ReadUInt16();
                cdReader.ReadUInt16(); // disk start
                cdReader.ReadUInt16(); // internal attrs
                cdReader.ReadUInt32(); // external attrs
                var localOffset = cdReader.ReadUInt32();

                var utf8 = (flags & 0x800) != 0;
                var nameBytes = cdReader.ReadBytes(nameLen);
                var name = (utf8 ? Encoding.UTF8 : Encoding.ASCII).GetString(nameBytes);
                cdReader.ReadBytes(extraLen + commentLen);

                _entries.Add(new Entry
                {
                    Name = name,
                    IsDirectory = name.EndsWith("/"),
                    Compression = compression,
                    CompressedSize = compSize,
                    UncompressedSize = uncompSize,
                    LocalHeaderOffset = localOffset,
                    LastModified = DosDateTimeToDateTime(modDate, modTime),
                });
            }
        }

        public Stream OpenEntry(Entry entry)
        {
            // Skip local file header to get to data
            _stream.Seek(entry.LocalHeaderOffset + 26, SeekOrigin.Begin);
            var localReader = new BinaryReader(_stream, Encoding.UTF8, leaveOpen: true);
            var nameLen = localReader.ReadUInt16();
            var extraLen = localReader.ReadUInt16();
            _stream.Seek(nameLen + extraLen, SeekOrigin.Current);

            var data = new byte[entry.CompressedSize];
            _stream.Read(data, 0, data.Length);

            if (entry.Compression == 0)
            {
                return new MemoryStream(data);
            }

            // Deflate: raw deflate stream (no zlib header)
            return new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
        }

        private static DateTime DosDateTimeToDateTime(ushort date, ushort time)
        {
            try
            {
                var year = ((date >> 9) & 0x7f) + 1980;
                var month = (date >> 5) & 0x0f;
                var day = date & 0x1f;
                var hour = (time >> 11) & 0x1f;
                var minute = (time >> 5) & 0x3f;
                var second = (time & 0x1f) * 2;
                return new DateTime(year, Math.Max(1, month), Math.Max(1, day), hour, minute, second);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        public void Dispose() => _stream.Dispose();
    }
}
