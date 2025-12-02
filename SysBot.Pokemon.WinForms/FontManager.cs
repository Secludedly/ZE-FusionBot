using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
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
                string? resourcePath = Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
                if (resourcePath == null)
                    throw new Exception($"Embedded font not found for filename: {fileName}");

                using Stream fontStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourcePath)
                    ?? throw new Exception($"Embedded font '{resourcePath}' not found.");

                byte[] fontData = new byte[fontStream.Length];
                fontStream.ReadExactly(fontData);

                IntPtr fontPtr = Marshal.AllocCoTaskMem(fontData.Length);
                Marshal.Copy(fontData, 0, fontPtr, fontData.Length);

                _fontCollection.AddMemoryFont(fontPtr, fontData.Length);

                Marshal.FreeCoTaskMem(fontPtr);

                // Get the real family name from the font
                FontFamily family = _fontCollection.Families[^1];
                string actualName = family.Name;

                // Avoid duplicate keys (like loading Regular + Bold)
                if (!_fonts.ContainsKey(actualName))
                    _fonts.Add(actualName, family);
            }
        }

        public static Font Get(string fontFamilyName, float size, FontStyle style = FontStyle.Regular)
        {
            if (!_fonts.TryGetValue(fontFamilyName, out var family))
            {
                throw new Exception(
                    $"Font '{fontFamilyName}' has not been loaded. " +
                    $"Loaded fonts: {string.Join(", ", _fonts.Keys)}");
            }

            return new Font(family, size, style);
        }

        public static IEnumerable<string> LoadedFamilies => _fonts.Keys;
    }

    public static class StreamExtensions
    {
        public static void ReadExactly(this Stream stream, byte[] buffer)
        {
            int offset = 0;
            int remaining = buffer.Length;

            while (remaining > 0)
            {
                int read = stream.Read(buffer, offset, remaining);
                if (read <= 0)
                    throw new EndOfStreamException("Unexpected end of stream while reading font.");

                offset += read;
                remaining -= read;
            }
        }
    }
}
