namespace HatModLoader.Source.ModDefinition
{
    public enum ModDependencyStatus
    {
        None,
        Valid,
        InvalidVersion,
        InvalidNotFound,
        InvalidRecursive,
        InvalidDependencyTree
    }
}
