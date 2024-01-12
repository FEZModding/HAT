using FEZRepacker.Converter.FileSystem;
using FEZRepacker.Converter.XNB;
using System.IO.Compression;

namespace HatModLoader.Source
{
    public class Asset
    {
        public string AssetPath { get; private set; }
        public string Extension { get; private set; }
        public byte[] Data { get; private set; }
        public bool IsMusicFile { get; private set; }

        public Asset(string path, string extension)
        {
            AssetPath = path;
            Extension = extension;

            CheckMusicAsset();
        }

        public Asset(string path, string extension, byte[] data)
            : this(path, extension)
        {
            Data = data;
        }

        public Asset(string path, string extension, Stream data)
            : this(path, extension)
        {
            Data = new byte[data.Length];
            data.Read(Data, 0, Data.Length);
        }

        private void CheckMusicAsset()
        {
            if(Extension == ".ogg" && AssetPath.StartsWith("music\\"))
            {
                IsMusicFile = true;
                AssetPath = AssetPath.Substring("music\\".Length);
            }
        }
    }
}
