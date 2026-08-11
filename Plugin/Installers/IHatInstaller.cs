using HatModLoader.Source;

namespace HatModLoader.Installers
{
    internal interface IHatInstaller
    {
        void Install(Hat hat);
        void Uninstall();
    }
}
