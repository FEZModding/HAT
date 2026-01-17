using HatModLoader.Source.FileProxies;

namespace HatModLoader.Source.ModDefinition
{
    public interface IMod : IDisposable
    {
        public IFileProxy FileProxy { get; }

        public Metadata Metadata { get; }
    }
}