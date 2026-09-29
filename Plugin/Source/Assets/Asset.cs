namespace HatModLoader.Source.Assets
{
    public enum AssetType
    {
        Default,
        MusicFile,
        ModTextResource
    }

    public class Asset
    {
        public string AssetPath { get; }
        public string SourcePath { get; }
        public string Extension { get; }
        public byte[] Data { get; }
        public AssetType AssetType { get; }
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
                AssetType = AssetType.MusicFile;
                AssetPath = path.Substring("music\\".Length);
            }
            else if (string.Equals(path, "resources\\modtext", StringComparison.OrdinalIgnoreCase))
            {
                AssetType = AssetType.ModTextResource;
                AssetPath = path;
            }
            else
            {
                AssetType = AssetType.Default;
                AssetPath = path;
            }

            Data = new byte[data.Length];
            data.ReadExactly(Data, 0, Data.Length);
        }

        private Asset(Asset source, bool removed)
        {
            AssetPath = source.AssetPath;
            SourcePath = source.SourcePath;
            Extension = source.Extension;
            Data = source.Data;
            AssetType = source.AssetType;
            LastModified = source.LastModified;
            IsRemoved = removed;
        }

        internal Asset AsRemoved() => new(this, true);
    }
}