using Common;
using FezEngine;
using FezEngine.Tools;
using FezGame.Services;
using Microsoft.Xna.Framework;

namespace FezGame.Components
{
    public class patch_GameLightingPostProcess : GameLightingPostProcess
    {
        private bool hasTested;

        public new IPlayerManager PlayerManager { private get; set; }

        public new IGameStateManager GameState { private get; set; }

        public patch_GameLightingPostProcess(Game game) : base(game)
        {
        }

        protected override void DoSetup()
        {
            if (!PlayerManager.Hidden && !GameState.InFpsMode)
            {
                if (!CameraManager.Viewpoint.IsOrthographic() && CameraManager.LastViewpoint != Viewpoint.None)
                {
                    PlayerManager.MeshHost.PlayerMesh.Rotation =
                        Quaternion.CreateFromAxisAngle(Vector3.UnitY, CameraManager.LastViewpoint.ToPhi());
                }
                else
                {
                    PlayerManager.MeshHost.PlayerMesh.Rotation = CameraManager.Rotation;
                }

                if (PlayerManager.LookingDirection == HorizontalDirection.Left)
                {
                    PlayerManager.MeshHost.PlayerMesh.Rotation *= FezMath.QuaternionFromPhi((float)Math.PI);
                }
            }

            if (hasTested)
            {
                return;
            }

            try
            {
                GraphicsDevice.SetRenderTarget(lightMapsRth.Target);
                GraphicsDevice.SetRenderTarget(null);
            }
            catch (InvalidOperationException exception)
            {
                // The original game opens a System.Windows.Forms error dialog here.
                // That desktop-framework dependency is unavailable on CoreCLR and is
                // unnecessary because the outer FEZ logger records the same failure.
                Logger.LogError(exception);
                Game.Exit();
                return;
            }

            hasTested = true;
        }
    }
}
