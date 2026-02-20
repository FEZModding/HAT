namespace HatModLoader.Source.ModDefinition
{
    public class ModDependencyGraph
    {
        private readonly Dictionary<string, Node> _nodes = new(StringComparer.OrdinalIgnoreCase);

        public void AddNode(ModContainer mod)
        {
            var name = mod.Metadata.Name;
            if (_nodes.TryGetValue(name, out var existing))
            {
                if (mod.Metadata.Version > existing.Mod.Metadata.Version)
                {
                    _nodes[name] = new Node(mod);
                }
            }
            else
            {
                _nodes[name] = new Node(mod);
            }
        }

        public bool TryGetNode(string name, out Node node)
        {
            return _nodes.TryGetValue(name, out node);
        }

        public IEnumerable<Node> Nodes => _nodes.Values;

        public class Node
        {
            public ModContainer Mod { get; }
            
            public List<Node> Dependencies { get; } = [];
            
            public ModDependencyStatus Status { get; private set; } = ModDependencyStatus.Valid;

            public string Details { get; private set; }

            public Node(ModContainer mod)
            {
                Mod = mod;
            }

            public void MarkInvalid(ModDependencyStatus status, string details)
            {
                if (Status == ModDependencyStatus.Valid && status != ModDependencyStatus.Valid)
                {
                    Status = status;
                    Details = details;
                }
            }

            public override string ToString()
            {
                return Mod.Metadata.Name;
            }
        }
    }
}
