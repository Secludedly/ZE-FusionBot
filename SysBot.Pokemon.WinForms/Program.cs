using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    static class Program
    {
        //////////////////////////////////////////////////
        // Set working directory to executable location //
        //////////////////////////////////////////////////

        public static readonly string WorkingDirectory = Environment.CurrentDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;

        /////////////////////////////
        // Configuration file path //
        /////////////////////////////
        public static string ConfigPath { get; private set; } = Path.Combine(WorkingDirectory, "config.json");

        //////////////////////////////////////////////////
        // Get the main entry point for the program //////
        //////////////////////////////////////////////////
        [STAThread]
        private static void Main()
        {
            // Enable high DPI support for .NET Core apps
#if NETCOREAPP
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
#endif
            // Standard WinForms setup to enable visual styles
            Application.EnableVisualStyles();

            // Set text rendering to be compatible
            Application.SetCompatibleTextRenderingDefault(false);

            ///////////////////////////////////////////
            /// Load fonts before showing main form ///
            ///////////////////////////////////////////
            LoadFonts();

            ///////////////////////////////////////////
            /// Prevent crashes from missing fonts ////
            ///////////////////////////////////////////
            Application.ThreadException += (sender, e) =>
            {
                if (e.Exception is ArgumentException && e.Exception.Message.Contains("Font"))
                {
                    // Log the font error but don't crash
                    Console.WriteLine($"[Font Warning] {e.Exception.Message}");

                    // Global fallback to avoid crashes from missing fonts
                    Application.UseWaitCursor = false;

                    // Switch default font for all controls
                    var fallback = SystemFonts.DefaultFont;

                    Application.OpenForms
                        .Cast<Form>()
                        .ToList()
                        .ForEach(f => ApplyFallbackFont(f, fallback));

                    return; // Handle gracefully, don't crash
                }

                if (e.Exception is InvalidOperationException && e.Exception.Message.Contains("Font"))
                {
                    // Log and handle Font Awesome loading errors
                    Console.WriteLine($"[Font Warning] {e.Exception.Message}");
                    return; // Handle gracefully, don't crash
                }

                // For other exceptions, log them but don't crash the entire application
                Console.WriteLine($"[Unhandled Exception] {e.Exception.GetType().Name}: {e.Exception.Message}");
                Console.WriteLine(e.Exception.StackTrace);
            };

            /// Recursively apply fallback font to control and its children
            static void ApplyFallbackFont(Control control, Font fallback)
            {
                try
                {
                    control.Font = fallback;
                }
                catch { }

                // Apply to all child controls recursively
                foreach (Control child in control.Controls)
                    ApplyFallbackFont(child, fallback);
            }


            ///////////////////////////////////////////
            /// Start main form ////////////////////////
            ///////////////////////////////////////////
            Application.Run(new Main());
        }

        ////////////////////////////////////////////
        // Load fonts synchronously on startup ////
        ////////////////////////////////////////////
        private static void LoadFonts()
        {
            try
            {
                FontManager.LoadFonts(
                    "bahnschrift.ttf",
                    "Bobbleboddy_light.ttf",
                    "EnterTheGrid.ttf",
                    "gadugi.ttf",
                    "gadugib.ttf",
                    "GNUOLANERG.ttf",
                    "Montserrat-Bold.ttf",
                    "Montserrat-Regular.ttf",
                    "segoeui.ttf",
                    "segoeuib.ttf",
                    "segoeuii.ttf",
                    "segoeuil.ttf",
                    "segoeuisl.ttf",
                    "segoeuiz.ttf",
                    "seguibl.ttf",
                    "seguibli.ttf",
                    "seguili.ttf",
                    "seguisb.ttf",
                    "seguisbi.ttf",
                    "seguisli.ttf",
                    "SegUIVar.ttf",
                    "UbuntuMono-R.ttf",
                    "UbuntuMono-B.ttf",
                    "UbuntuMono-BI.ttf",
                    "UbuntuMono-RI.ttf"
                );
            }
            catch (Exception ex)
            {
                // Log font loading errors but don't fail startup
                Console.WriteLine($"[Font Loading Warning] Some fonts failed to load: {ex.Message}");
                Console.WriteLine("[Font Loading Warning] Application will use fallback fonts.");
            }
        }
    }
}

