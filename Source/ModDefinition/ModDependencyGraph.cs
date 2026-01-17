namespace HatModLoader.Source.ModDefinition
{
    public class ModDependencyGraph
    {
        private readonly Dictionary<string, Node> _nodes = new(StringComparer.OrdinalIgnoreCase);

        public void AddNode(CodeMod mod)
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
            public CodeMod Mod { get; }
            
            public List<Node> Dependencies { get; } = [];
            
            public ModDependencyStatus Status { get; private set; } = ModDependencyStatus.Valid;

            private string Details { get; set; }

            public Node(CodeMod mod)
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

            public string GetStatusText()
            {
                var statusText = Status switch
                {
                    ModDependencyStatus.Valid => "Valid",
                    ModDependencyStatus.InvalidVersion => "Version mismatch",
                    ModDependencyStatus.InvalidNotFound => "Not found",
                    ModDependencyStatus.InvalidRecursive => "Circular dependency",
                    ModDependencyStatus.InvalidDependencyTree => "Dependency tree error",
                    _ => "Unknown"
                };
                return $"{statusText} - {Details}";
            }

            public override string ToString()
            {
                return Mod.Metadata.Name;
            }
        }
    }
}
