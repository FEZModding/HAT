using FEZRepacker.Converter.FileSystem;

namespace HatModLoader.Source.Assets
{
    public interface FileSourceProxy
    {
        public FileBundle ReadFileBundle(string assetName);
        public HashSet<string> GetFilePathsByAssetPath(string assetName);
        public bool FileChanged(string filePath);
        public bool IsValid();
    }
}
