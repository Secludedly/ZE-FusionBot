using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace SysBot.Pokemon.WinForms
{
    public static class FontManager
    {
        private static readonly Dictionary<string, FontFamily> _fonts = new();
        private static readonly PrivateFontCollection _fontCollection = new();

        public static void LoadFonts(params string[] fontFileNames)
        {
            foreach (var fileName in fontFileNames)
            {
                string resourcePath = $"SysBot.Pokemon.WinForms.Fonts.{fileName}";

                using Stream fontStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath)
                    ?? throw new Exception($"Embedded font '{resourcePath}' not found.");

                byte[] fontData = new byte[fontStream.Length];
                int bytesRead = 0;
                int totalRead = 0;

                while (totalRead < fontData.Length &&
                       (bytesRead = fontStream.Read(fontData, totalRead, fontData.Length - totalRead)) > 0)
                {
                    totalRead += bytesRead;
                }

                if (totalRead != fontData.Length)
                    throw new EndOfStreamException($"Could not fully read embedded font '{fileName}'. Expected {fontData.Length} bytes, got {totalRead}.");


                IntPtr fontPtr = Marshal.AllocCoTaskMem(fontData.Length);
                Marshal.Copy(fontData, 0, fontPtr, fontData.Length);
                _fontCollection.AddMemoryFont(fontPtr, fontData.Length);
                Marshal.FreeCoTaskMem(fontPtr);

                FontFamily family = _fontCollection.Families[^1]; // grab last added
                _fonts[Path.GetFileNameWithoutExtension(fileName)] = family;
            }
        }

        public static Font Get(string fontName, float size, FontStyle style = FontStyle.Regular)
        {
            if (!_fonts.TryGetValue(fontName, out var family))
                throw new Exception($"Font '{fontName}' has not been loaded.");

            return new Font(family, size, style);
        }

        public static bool IsLoaded(string fontName) => _fonts.ContainsKey(fontName);
    }
}
