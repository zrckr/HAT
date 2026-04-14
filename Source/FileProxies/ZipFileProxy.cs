using System.IO.Compression;

namespace HatModLoader.Source.FileProxies
{
    public class ZipFileProxy : IFileProxy
    {
        private ZipArchive _archive;
        private readonly string _zipPath;
        private DateTime _zipLastModified;

        public string RootPath => _zipPath;
        public string ContainerName => Path.GetFileName(_zipPath);

        public ZipFileProxy(string zipPath)
        {
            _zipPath = zipPath;
            Reopen();
        }

        private void Reopen()
        {
            _archive?.Dispose();
            _zipLastModified = File.GetLastWriteTimeUtc(_zipPath);
            _archive = ZipFile.OpenRead(_zipPath);
        }

        public void Refresh()
        {
            var modified = File.GetLastWriteTimeUtc(_zipPath);
            if (modified > _zipLastModified)
            {
                Reopen();
            }
        }

        public IEnumerable<string> EnumerateFiles(string localPath)
        {
            if (!localPath.EndsWith("/")) localPath += "/";

            return _archive.Entries
                .Where(e => e.FullName.StartsWith(localPath))
                .Select(e => e.FullName);
        }

        public bool FileExists(string localPath)
        {
            return _archive.Entries.Any(e => e.FullName == localPath);
        }

        public Stream OpenFile(string localPath)
        {
            // Copy to MemoryStream so the caller owns the data independently of the archive
            var entry = _archive.Entries.First(e => e.FullName == localPath);
            var ms = new MemoryStream();
            using var s = entry.Open();
            s.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        public DateTime GetLastModified(string localPath)
        {
            return _archive.Entries.First(e => e.FullName == localPath).LastWriteTime.UtcDateTime;
        }

        public void Dispose()
        {
            _archive.Dispose();
        }

        public static IEnumerable<ZipFileProxy> EnumerateInDirectory(string directory)
        {
            return Directory.EnumerateFiles(directory)
                .Where(file => Path.GetExtension(file).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                .Select(file => new ZipFileProxy(file));
        }
    }
}
