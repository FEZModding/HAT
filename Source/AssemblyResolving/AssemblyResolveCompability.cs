using System.Reflection;

namespace HatModLoader.Source.AssemblyResolving
{
    internal static class AssemblyResolveCompability
    {
        public static bool MatchesRequest(this AssemblyName assemblyName, ResolveEventArgs args, bool allowRollForward)
        {
            var requestedName = new AssemblyName(args.Name);

            return assemblyName.Name == requestedName.Name &&
                   assemblyName.CultureName == requestedName.CultureName &&
                   ComparePublicKeyTokens(assemblyName.GetPublicKeyToken(), requestedName.GetPublicKeyToken()) &&
                   CompareVersions(assemblyName.Version, requestedName.Version, allowRollForward);
        }
        
        private static bool ComparePublicKeyTokens(byte[] tokenA, byte[] tokenB)
        {
            // Avoiding usage of stuff like SequenceEqual to prevent accidental dependency request at this stage.
            
            if (tokenA == null && tokenB == null)
            {
                return true;
            }

            if (tokenA == null || tokenB == null || tokenA.Length != tokenB.Length)
            {
                return false;
            }

            for (int i = 0; i < tokenA.Length; i++)
            {
                if (tokenA[i] != tokenB[i])
                {
                    return false;
                }
            }

            return true;
        }
        
        private static bool CompareVersions(Version checkedVersion, Version requiredVersion, bool allowRollForward)
        {
            if (allowRollForward)
            {
                return checkedVersion >= requiredVersion;
            }
            
            return checkedVersion.Major == requiredVersion.Major &&
                   checkedVersion.Minor == requiredVersion.Minor &&
                   checkedVersion.Build == requiredVersion.Build &&
                   checkedVersion.Revision >= requiredVersion.Revision;
        }
    }
}

