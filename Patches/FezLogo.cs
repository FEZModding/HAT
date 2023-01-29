using FezEngine.Effects;
using FezEngine.Structure;
using FezEngine.Structure.Geometry;
using FezEngine.Tools;
using HatModLoader.Helpers;
using HatModLoader.Source;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FezGame.Components
{
    public class patch_FezLogo : FezLogo
    {
        public patch_FezLogo(Game game) : base(game){}

        public extern void orig_Initialize();
        public override void Initialize()
        {
            orig_Initialize();

            // custom very cool procedural logo creation code

            var LogoMesh = new Mesh
            {
                AlwaysOnTop = true,
                DepthWrites = false,
                Blending = BlendingMode.Alphablending
            };
            var WireMesh = new Mesh
            {
                DepthWrites = false,
                AlwaysOnTop = true
            };

            var LogoMap = new string[]
            {
                "# # ### ###",
                "### ###  # ",
                "### ###  # ",
                "# # # #  # "
            };

            var logoWidth = LogoMap[0].Length;
            var logoHeight = LogoMap.Length;

            Func<int, int, bool> IsFilled = delegate (int x, int y)
            {
                y = logoHeight - (y + 1);
                if (x < 0 || x >= logoWidth || y < 0 || y >= logoHeight) return false;
                return LogoMap[y][x] == '#';
            };


            var WireMeshVertices = new List<Vector3>();
            var WireMeshIndices = new List<int>();

            Action<Vector3> AddPoint = delegate (Vector3 pos)
            {
                int index = WireMeshVertices.IndexOf(pos);
                if (index < 0)
                {
                    index = WireMeshVertices.Count;
                    WireMeshVertices.Add(pos);
                }
                WireMeshIndices.Add(index);
            };

            Action<float, float, float, float> Line = delegate (float x1, float y1, float x2, float y2)
            {
                if (x1 == x2 && y1 == y2)
                {
                    AddPoint(new Vector3(x1, y1, 0.0f));
                    AddPoint(new Vector3(x1, y1, 1.0f));
                }
                else for(float i = 0.0f; i <= 1.0f; i++)
                {
                    AddPoint(new Vector3(x1, y1, i));
                    AddPoint(new Vector3(x2, y2, i));
                }
            };

            for (int x = 0; x < logoWidth; x++)
            {
                for (int y = 0; y < logoHeight; y++)
                {
                    // colored box for LogoMesh
                    if (!IsFilled(x, y)) continue;
                    LogoMesh.AddColoredBox(Vector3.One, new Vector3(x, y, 0f), Color.Black, centeredOnOrigin: false);

                    // wireframe for WireMesh
                    bool top = IsFilled(x, y + 1);
                    bool bottom = IsFilled(x, y - 1);
                    bool left = IsFilled(x - 1, y);
                    bool right = IsFilled(x + 1, y);
                    bool topleft = IsFilled(x - 1, y + 1);
                    bool topright = IsFilled(x + 1, y + 1);
                    bool bottomleft = IsFilled(x - 1, y - 1);
                    bool bottomright = IsFilled(x + 1, y - 1);

                    if (!top) Line(x, y + 1, x + 1, y + 1);
                    if (!bottom) Line(x, y, x + 1, y);
                    if (!right) Line(x + 1, y, x + 1, y + 1);
                    if (!left) Line(x, y, x, y + 1);
                    if ((!top && !left) || (top && left && !topleft)) Line(x, y + 1, x, y + 1);
                    if ((!top && !right) || (top && right && !topright)) Line(x + 1, y + 1, x + 1, y + 1);
                    if ((!bottom && !left) || (bottom && left && !bottomleft)) Line(x, y, x, y);
                    if ((!bottom && !right) || (bottom && right && !bottomright)) Line(x + 1, y, x + 1, y);
                }
            }


            IndexedUserPrimitives<FezVertexPositionColor> indexedUserPrimitives = (IndexedUserPrimitives<FezVertexPositionColor>)(WireMesh.AddGroup().Geometry = new IndexedUserPrimitives<FezVertexPositionColor>(PrimitiveType.LineList));

            indexedUserPrimitives.Vertices = WireMeshVertices.Select(pos => new FezVertexPositionColor(pos, Color.White)).ToArray();
            indexedUserPrimitives.Indices = WireMeshIndices.ToArray();

            WireMesh.Position = LogoMesh.Position = new Vector3(-logoWidth * 0.5f, -logoHeight * 0.5f, -0.5f);
            WireMesh.BakeTransform<FezVertexPositionColor>();
            LogoMesh.BakeTransform<FezVertexPositionColor>();
            LogoMesh.Material.Opacity = 0f;

            var FezEffectField = typeof(FezLogo).GetField("FezEffect", BindingFlags.NonPublic | BindingFlags.Instance);
            var LogoMeshField = typeof(FezLogo).GetField("LogoMesh", BindingFlags.NonPublic | BindingFlags.Instance);
            var WireMeshField = typeof(FezLogo).GetField("WireMesh", BindingFlags.NonPublic | BindingFlags.Instance);

            DrawActionScheduler.Schedule(delegate
            {
                WireMesh.Effect = LogoMesh.Effect = (BaseEffect)FezEffectField.GetValue(this);
            });

            LogoMeshField.SetValue(this, LogoMesh);
            WireMeshField.SetValue(this, WireMesh);
        }

        public extern void orig_Draw(GameTime gameTime);
        public override void Draw(GameTime gameTime)
        {
            orig_Draw(gameTime);

            if (Hat.Instance == null) return;

            float alpha = Math.Max(0, Math.Min(Starfield.Opacity, 1.0f - SinceStarted));
            if (alpha == 0.0f) return;

            Viewport viewport = DrawingTools.GetViewport();
            int modCount = Hat.Instance.Mods.Count;
            string hatText = $"HAT Mod Loader, version {Hat.Version}, {modCount} mod{(modCount != 1 ? "s" : "")} installed";
            int enabledModCount = Hat.Instance.EnabledMods.Count;
            if (enabledModCount != modCount)
                hatText += $", {enabledModCount} mod{(enabledModCount != 1 ? "s" : "")} enabled";
            if (enabledModCount == 69) hatText += "... nice";
            Color textColor = Color.Lerp(Color.White, Color.Black, alpha);

            DrawingTools.BeginBatch();
            DrawingTools.DrawText(hatText, new Vector2(30, viewport.Height - 80), textColor);
            DrawingTools.EndBatch();
        }
    }
}
