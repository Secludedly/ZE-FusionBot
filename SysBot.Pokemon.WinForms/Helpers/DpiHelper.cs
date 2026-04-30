using System;
using System.Drawing;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms.Helpers
{
    /// <summary>
    /// Helper class for DPI-aware scaling operations
    /// </summary>
    public static class DpiHelper
    {
        private static float _systemDpi = 96f;
        private static bool _initialized = false;

        static DpiHelper()
        {
            Initialize();
        }

        private static void Initialize()
        {
            if (_initialized) return;

            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    _systemDpi = g.DpiX;
                }
            }
            catch
            {
                _systemDpi = 96f;
            }

            _initialized = true;
        }

        /// <summary>
        /// Gets the current system DPI scale factor
        /// </summary>
        public static float DpiScale => _systemDpi / 96f;

        /// <summary>
        /// Scales a value based on current DPI
        /// </summary>
        public static int Scale(int value)
        {
            return (int)(value * DpiScale);
        }

        /// <summary>
        /// Scales a size based on current DPI
        /// </summary>
        public static Size Scale(Size size)
        {
            return new Size(Scale(size.Width), Scale(size.Height));
        }

        /// <summary>
        /// Scales a point based on current DPI
        /// </summary>
        public static Point Scale(Point point)
        {
            return new Point(Scale(point.X), Scale(point.Y));
        }

        /// <summary>
        /// Scales a rectangle based on current DPI
        /// </summary>
        public static Rectangle Scale(Rectangle rect)
        {
            return new Rectangle(
                Scale(rect.X),
                Scale(rect.Y),
                Scale(rect.Width),
                Scale(rect.Height)
            );
        }

        /// <summary>
        /// Scales a font based on current DPI
        /// </summary>
        public static Font ScaleFont(Font font)
        {
            if (Math.Abs(DpiScale - 1.0f) < 0.01f)
                return font;

            return new Font(font.FontFamily, font.Size * DpiScale, font.Style, font.Unit);
        }

        /// <summary>
        /// Gets the appropriate font size for current DPI
        /// </summary>
        public static float GetScaledFontSize(float baseSize)
        {
            return baseSize * DpiScale;
        }

        /// <summary>
        /// Updates DPI for a specific control
        /// </summary>
        public static void UpdateDpiForControl(Control control)
        {
            if (control == null) return;

            // Update font
            if (control.Font != null)
            {
                var scaledFont = ScaleFont(control.Font);
                if (scaledFont != control.Font)
                {
                    control.Font = scaledFont;
                }
            }

            // Recursively update child controls
            foreach (Control child in control.Controls)
            {
                UpdateDpiForControl(child);
            }
        }
    }
}