
using FezEngine.Components;
using FezEngine.Tools;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace HatModLoader.Helpers
{
    internal static class DrawingTools
    {
        public static IFontManager FontManager { get; private set; }
        public static GraphicsDevice GraphicsDevice { get; private set; }
        public static SpriteBatch Batch { get; private set; }

        private static Texture2D fillTexture;

        public static SpriteFont DefaultFont { get; set; }
        public static float DefaultFontSize { get; set; }

        public static void Init()
        {
            FontManager = ServiceHelper.Get<IFontManager>();
            GraphicsDevice = ServiceHelper.Get<IGraphicsDeviceService>().GraphicsDevice;
            Batch = new SpriteBatch(GraphicsDevice);
            DefaultFont = FontManager.Big;
            DefaultFontSize = 2.0f;

            fillTexture = new Texture2D(GraphicsDevice, 1, 1, false, SurfaceFormat.Color);
            fillTexture.SetData(new[] { new Color(255, 255, 255) });
        }

        public static Viewport GetViewport()
        {
            return GraphicsDevice.Viewport;
        }

        public static void BeginBatch()
        {
            Batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        }

        public static void EndBatch()
        {
            Batch.End();
        }

        public static void DrawRect(Rectangle rect, Color color)
        {
            Batch.Draw(fillTexture, rect, color);
        }

        public static void DrawText(string text, Vector2 position)
        {
            DrawText(text, position, Color.White);
        }

        public static void DrawText(string text, Vector2 position, Color color)
        {
            DrawText(text, position, 0.0f, DefaultFontSize, Vector2.Zero, color);
        }

        public static void DrawText(string text, Vector2 position, float rotation, float scale, Color color)
        {
            DrawText(text, position, rotation, scale, Vector2.Zero, color);
        }

        public static void DrawText(string text, Vector2 position, float rotation, float scale, Vector2 origin, Color color)
        {
            scale *= FontManager.BigFactor / 2f;
            Batch.DrawString(DefaultFont, text, position, color,
                rotation, origin, scale, SpriteEffects.None, 0f
            );
        }

    }
}
