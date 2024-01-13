namespace HatModLoader.Source.Assets
{
    public interface FileSourceProxy
    {
        public void Precache();
        public bool IsValid();
        public HashSet<string> GetFileList();
        public bool FileChanged(string filePath);
        public Dictionary<string, Stream> OpenFilesAndMarkUnchanged(HashSet<string> filePaths);
    }
}
