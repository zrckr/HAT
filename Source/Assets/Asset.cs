namespace HatModLoader.Source.Assets
{
    public class Asset
    {
        public string AssetPath { get; }
        public string SourcePath { get; }
        public string Extension { get; }
        public byte[] Data { get; }
        public bool IsMusicFile { get; }
        public DateTime LastModified { get; }
        public bool IsRemoved { get; }

        public Asset(string path, string extension, Stream data, DateTime? lastModified = null)
        {
            SourcePath = path;
            Extension = extension;
            LastModified = lastModified ?? DateTime.MinValue;
            IsRemoved = false;

            if (extension == ".ogg" && path.StartsWith("music\\"))
            {
                IsMusicFile = true;
                AssetPath = path.Substring("music\\".Length);
            }
            else
            {
                AssetPath = path;
            }

            Data = new byte[data.Length];
            // ReSharper disable once MustUseReturnValue
            data.Read(Data, 0, Data.Length);
        }

        private Asset(Asset source, bool removed)
        {
            AssetPath = source.AssetPath;
            SourcePath = source.SourcePath;
            Extension = source.Extension;
            Data = source.Data;
            IsMusicFile = source.IsMusicFile;
            LastModified = source.LastModified;
            IsRemoved = removed;
        }

        internal Asset AsRemoved() => new(this, true);
    }
}