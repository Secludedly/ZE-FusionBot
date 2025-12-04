using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

            // Run splash as the main form temporarily
            var splash = new SplashScreen();
            splash.StartPosition = FormStartPosition.CenterScreen;
            splash.TopMost = true;

            // When splash loads, preload assets async
            splash.Shown += async (s, e) =>
            {
                // Start loading assets
                var preloadTask = PreloadAssetsAsync();

                // Require splash to last at least 3 seconds
                await Task.WhenAll(
                    preloadTask,
                    Task.Delay(3000)
                );

                // After loading + delay, show main form
                var mainForm = new Main();
                mainForm.StartPosition = FormStartPosition.CenterScreen;
                mainForm.Show();

                // Hide splash
                splash.Hide();
            };


            ///////////////////////////////////////////
            /// Prevent crashes from missing fonts ////
            ///////////////////////////////////////////
            Application.ThreadException += (sender, e) =>
            {
                if (e.Exception is ArgumentException && e.Exception.Message.Contains("Font"))
                {
                    // Global fallback to avoid crashes from missing fonts
                    Application.UseWaitCursor = false;

                    // Switch default font for all controls
                    var fallback = SystemFonts.DefaultFont;

                    Application.OpenForms
                        .Cast<Form>()
                        .ToList()
                        .ForEach(f => ApplyFallbackFont(f, fallback));

                    return;
                }

                // If it's not a font issue, rethrow
                throw e.Exception;
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
            /// Start UI form on the main thread //////
            ///////////////////////////////////////////
            Application.Run(splash);
        }

        ////////////////////////////////////////////
        // Preload assets like fonts, images, etc.//
        ////////////////////////////////////////////
        private static async Task PreloadAssetsAsync()
        {
            await Task.Run(() =>
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
            });
        }
    }
}

