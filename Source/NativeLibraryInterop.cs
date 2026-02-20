using System.Runtime.InteropServices;

namespace HatModLoader.Source
{
    public static class NativeLibraryInterop
    {
        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("libdl", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Dlopen(string fileName, int flags);

        [DllImport("kernel32", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr hModule);

        [DllImport("libdl", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Dlclose(IntPtr handle);
            
        public static IntPtr Load(string fileName)
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? LoadLibrary(fileName)
                : Dlopen(fileName, 1);
        }
            
        public static bool Free(IntPtr libraryHandle)
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? FreeLibrary(libraryHandle)
                : Dlclose(libraryHandle) == 0;
        }
    }
}