namespace HatModLoader.Source.FileProxies
{
    public interface IFileProxy : IDisposable
    {
        public string RootPath { get; }
        public string ContainerName { get; }
        public IEnumerable<string> EnumerateFiles(string localPath);
        public bool FileExists(string localPath);
        public Stream OpenFile(string localPath);
        public IntPtr LoadLibrary(string localPath);
        public void UnloadLibrary(IntPtr handle);
        public bool IsDotNetAssembly(string localPath);
    }
}
