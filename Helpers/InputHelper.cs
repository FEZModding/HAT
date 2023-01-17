using FezEngine.Services;
using FezEngine.Tools;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HatModLoader.Helpers
{
    internal static class InputHelper
    {
        private static Dictionary<Keys, double> KeyboardRepeatHeldTimers = new Dictionary<Keys, double>();
        private static List<Keys> KeyboardRepeatedPresses = new List<Keys>();

        public static KeyboardState CurrentKeyboardState { get; private set; }
        public static KeyboardState PreviousKeyboardState { get; private set; }

        public static double KeyboardRepeatDelay { get; set; } = 0.4;
        public static double KeyboardRepeatSpeed { get; set; } = 0.03;

        public static void Update(GameTime gameTime)
        {
            PreviousKeyboardState = CurrentKeyboardState;
            CurrentKeyboardState = Keyboard.GetState();


            KeyboardRepeatedPresses.Clear();
            foreach (Keys key in CurrentKeyboardState.GetPressedKeys())
            {
                if (IsKeyPressed(key) || !KeyboardRepeatHeldTimers.ContainsKey(key))
                {
                    KeyboardRepeatHeldTimers[key] = 0.0f;
                }

                KeyboardRepeatHeldTimers[key] += gameTime.ElapsedGameTime.TotalSeconds;
                if (KeyboardRepeatHeldTimers[key] > KeyboardRepeatDelay + KeyboardRepeatSpeed)
                {
                    KeyboardRepeatHeldTimers[key] = KeyboardRepeatDelay;
                    KeyboardRepeatedPresses.Add(key);
                }
            }
        }

        public static bool IsKeyPressed(Keys key)
        {
            return PreviousKeyboardState.IsKeyUp(key) && CurrentKeyboardState.IsKeyDown(key);
        }

        public static bool IsKeyHeld(Keys key)
        {
            return CurrentKeyboardState.IsKeyDown(key);
        }

        public static bool IsKeyReleased(Keys key)
        {
            return PreviousKeyboardState.IsKeyDown(key) && CurrentKeyboardState.IsKeyUp(key);
        }
        
        public static bool IsKeyTyped(Keys key)
        {
            return IsKeyPressed(key) || KeyboardRepeatedPresses.Contains(key);
        }
    }
}
