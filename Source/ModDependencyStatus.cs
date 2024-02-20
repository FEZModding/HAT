namespace HatModLoader.Source
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
