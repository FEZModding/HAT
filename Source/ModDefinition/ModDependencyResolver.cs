namespace HatModLoader.Source.ModDefinition
{
    public static class ModDependencyResolver
    {
        private const string HatDependencyName = "HAT";

        public static ResolverResult Resolve(IList<CodeMod> mods)
        {
            var graph = new ModDependencyGraph();
            var rejected = new List<ModDependencyGraph.Node>();

            // Validate HAT dependency and build graph nodes
            foreach (var mod in mods)
            {
                var status = ValidateHatDependency(mod, out var details);
                if (status != ModDependencyStatus.Valid)
                {
                    var node = new ModDependencyGraph.Node(mod);
                    node.MarkInvalid(status, details);
                    rejected.Add(node);
                }
                else
                {
                    graph.AddNode(mod);
                }
            }

            // Build edges and validate dependencies
            foreach (var node in graph.Nodes)
            {
                BuildEdges(graph, node);
            }

            // Topological sort with cycle detection
            var loadOrder = TopologicalSort(graph);

            // Collect invalid nodes
            var invalid = rejected
                .Concat(graph.Nodes.Where(n => n.Status != ModDependencyStatus.Valid))
                .ToList();

            return new ResolverResult(loadOrder, invalid);
        }

        private static ModDependencyStatus ValidateHatDependency(CodeMod mod, out string details)
        {
            var deps = mod.Metadata.Dependencies;
            if (deps == null)
            {
                details = "No dependencies declared, HAT dependency required";
                return ModDependencyStatus.InvalidNotFound;
            }

            var hatDependency = deps.FirstOrDefault(d => IsHatDependency(d.Name));
            if (string.IsNullOrEmpty(hatDependency.Name))
            {
                details = "HAT dependency not declared";
                return ModDependencyStatus.InvalidNotFound;
            }

            if (hatDependency.MinimumVersion != null && Hat.Version < hatDependency.MinimumVersion)
            {
                details = $"Requires HAT >={hatDependency.MinimumVersion}, found {Hat.Version}";
                return ModDependencyStatus.InvalidVersion;
            }

            details = string.Empty;
            return ModDependencyStatus.Valid;
        }

        private static void BuildEdges(ModDependencyGraph graph, ModDependencyGraph.Node node)
        {
            var deps = node.Mod.Metadata.Dependencies;
            if (deps == null)
            {
                return;
            }

            foreach (var dep in deps.Where(d => !IsHatDependency(d.Name)))
            {
                if (!graph.TryGetNode(dep.Name, out var depNode))
                {
                    node.MarkInvalid(ModDependencyStatus.InvalidNotFound, $"Missing dependency '{dep.Name}'");
                    return;
                }

                if (dep.MinimumVersion != null && depNode.Mod.Metadata.Version < dep.MinimumVersion)
                {
                    node.MarkInvalid(ModDependencyStatus.InvalidVersion,
                        $"'{dep.Name}' requires >={dep.MinimumVersion}, found {depNode.Mod.Metadata.Version}");
                    return;
                }

                node.Dependencies.Add(depNode);
            }
        }

        private static List<CodeMod> TopologicalSort(ModDependencyGraph graph)
        {
            var result = new List<CodeMod>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var path = new List<ModDependencyGraph.Node>();
            var pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<Progress>();

            foreach (var startNode in graph.Nodes)
            {
                if (startNode.Status != ModDependencyStatus.Valid || visited.Contains(startNode.Mod.Metadata.Name))
                {
                    continue;
                }

                stack.Push(new Progress(startNode, false));
                while (stack.Count > 0)
                {
                    var (node, processed) = stack.Pop();
                    var name = node.Mod.Metadata.Name;

                    if (processed)
                    {
                        path.RemoveAt(path.Count - 1);
                        pathSet.Remove(name);

                        foreach (var dep in node.Dependencies)
                        {
                            if (dep.Status != ModDependencyStatus.Valid && node.Status == ModDependencyStatus.Valid)
                            {
                                node.MarkInvalid(ModDependencyStatus.InvalidDependencyTree,
                                    $"Depends on '{dep.Mod.Metadata.Name}' which is invalid");
                                break;
                            }
                        }

                        if (node.Status == ModDependencyStatus.Valid)
                        {
                            visited.Add(name);
                            result.Add(node.Mod);
                        }
                        continue;
                    }

                    if (visited.Contains(name))
                    {
                        continue;
                    }

                    if (node.Status != ModDependencyStatus.Valid)
                    {
                        PropagateInvalid(node, path);
                        continue;
                    }

                    if (pathSet.Contains(name))
                    {
                        MarkCycle(node, path);
                        continue;
                    }

                    path.Add(node);
                    pathSet.Add(name);
                    stack.Push(new Progress(node, true));

                    foreach (var dep in node.Dependencies)
                    {
                        if (!visited.Contains(dep.Mod.Metadata.Name))
                        {
                            stack.Push(new Progress(dep, false));
                        }
                    }
                }
            }

            return result;
        }

        private static void MarkCycle(ModDependencyGraph.Node cycleNode, List<ModDependencyGraph.Node> path)
        {
            var cycleName = cycleNode.Mod.Metadata.Name;
            var cycleStart = path.FindIndex(n =>
                string.Equals(n.Mod.Metadata.Name, cycleName, StringComparison.OrdinalIgnoreCase));

            if (cycleStart < 0)
            {
                cycleStart = 0;
            }

            var cyclePath = path.Skip(cycleStart).ToList();
            var cycleChain = string.Join(" -> ", cyclePath.Select(n => n.Mod.Metadata.Name)) + " -> " + cycleName;

            // Mark all nodes in the cycle
            foreach (var node in cyclePath)
            {
                node.MarkInvalid(ModDependencyStatus.InvalidRecursive, cycleChain);
            }

            // Mark nodes before the cycle as having invalid dependency tree
            for (var i = 0; i < cycleStart; i++)
            {
                var node = path[i];
                node.MarkInvalid(ModDependencyStatus.InvalidDependencyTree,
                    $"Depends on '{path[i + 1].Mod.Metadata.Name}' which has a circular dependency");
            }
        }

        private static void PropagateInvalid(ModDependencyGraph.Node invalidNode, List<ModDependencyGraph.Node> path)
        {
            var invalidName = invalidNode.Mod.Metadata.Name;
            foreach (var node in path)
            {
                node.MarkInvalid(ModDependencyStatus.InvalidDependencyTree,
                    $"Depends on '{invalidName}' which is invalid");
            }
        }

        private static bool IsHatDependency(string name)
        {
            return string.Equals(name, HatDependencyName, StringComparison.OrdinalIgnoreCase);
        }

        private record struct Progress(ModDependencyGraph.Node Node, bool Processed);
    }

    public class ResolverResult
    {
        public List<CodeMod> LoadOrder { get; }
        
        public List<ModDependencyGraph.Node> Invalid { get; }

        public ResolverResult(List<CodeMod> loadOrder, List<ModDependencyGraph.Node> invalid)
        {
            LoadOrder = loadOrder;
            Invalid = invalid;
        }
    }
}
