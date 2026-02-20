namespace HatModLoader.Source.ModDefinition
{
    public enum ModDependencyStatus
    {
        Valid,
        InvalidVersion,
        InvalidNotFound,
        InvalidRecursive,
        InvalidDependencyTree
    }
}