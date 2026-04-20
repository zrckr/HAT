using System.Reflection;

namespace HatModLoader.Source.FileProxies
{
    public class ZipFileProxy : IFileProxy
    {
        private ZipReader _archive;
        private readonly string _zipPath;
        private DateTime _zipLastModified;
        private readonly Dictionary<IntPtr, string> _tempFiles = new();

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
            _archive = new ZipReader(_zipPath);
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
                .Where(e => !e.IsDirectory && e.Name.StartsWith(localPath))
                .Select(e => e.Name);
        }

        public bool FileExists(string localPath)
        {
            return _archive.Entries.Any(e => !e.IsDirectory && e.Name == localPath);
        }

        public Stream OpenFile(string localPath)
        {
            var entry = GetEntry(localPath);
            var ms = new MemoryStream();
            using var s = _archive.OpenEntry(entry);
            s.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        public DateTime GetLastModified(string localPath)
        {
            return GetEntry(localPath).LastModified.ToUniversalTime();
        }

        private ZipReader.Entry GetEntry(string localPath)
        {
            return _archive.Entries.FirstOrDefault(e => !e.IsDirectory && e.Name == localPath);
        }

        public IntPtr LoadLibrary(string localPath)
        {
            var tempFile = Path.GetTempFileName();
            using (var fs = File.Create(tempFile))
            using (var s = _archive.OpenEntry(GetEntry(localPath)))
                s.CopyTo(fs);

            var handle = NativeLibraryInterop.Load(tempFile);
            if (handle != IntPtr.Zero)
            {
                _tempFiles.Add(handle, tempFile);
            }

            return handle;
        }

        public void UnloadLibrary(IntPtr handle)
        {
            if (_tempFiles.TryGetValue(handle, out var tempFile))
            {
                NativeLibraryInterop.Free(handle);
                File.Delete(tempFile);
                _tempFiles.Remove(handle);
            }
        }

        public bool IsDotNetAssembly(string localPath)
        {
            var tempFile = Path.GetTempFileName();
            var result = true;

            try
            {
                using (var fs = File.Create(tempFile))
                using (var s = _archive.OpenEntry(GetEntry(localPath)))
                    s.CopyTo(fs);
                AssemblyName.GetAssemblyName(tempFile);
            }
            catch (BadImageFormatException)
            {
                result = false;     // Native library file
            }

            File.Delete(tempFile);
            return result;
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
