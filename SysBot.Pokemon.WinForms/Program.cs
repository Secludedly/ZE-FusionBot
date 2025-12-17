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

            // Show splash screen on a separate thread
            var splash = new SplashScreen();
            splash.StartPosition = FormStartPosition.CenterScreen;
            splash.TopMost = true;

            // When splash loads, preload assets and transition to main form
            splash.Shown += OnSplashShown;

            async void OnSplashShown(object? sender, EventArgs e)
            {
                Main? mainForm = null;
                try
                {
                    // Start loading assets
                    var preloadTask = PreloadAssetsAsync();

                    // Require splash to last at least 3 seconds for visibility
                    await Task.WhenAll(
                        preloadTask,
                        Task.Delay(3000)
                    ).ConfigureAwait(true);

                    // Create main form on UI thread (may throw if FontAwesome.Sharp has issues)
                    mainForm = new Main();
                    mainForm.StartPosition = FormStartPosition.CenterScreen;
                    mainForm.FormClosed += (s, args) => Application.Exit(); // Ensure app exits when main form closes
                    mainForm.Show();

                    // Properly close splash screen (not just hide)
                    splash.Close();
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Font Awesome") || ex.Message.Contains("font"))
                {
                    // Font Awesome library error - try to recover or show helpful message
                    splash.Close();

                    var result = MessageBox.Show(
                        "FontAwesome.Sharp library failed to initialize.\n\n" +
                        "This is usually caused by:\n" +
                        "• Missing or corrupted FontAwesome.Sharp.dll\n" +
                        "• Incompatible .NET runtime version\n" +
                        "• Anti-virus blocking embedded resources\n\n" +
                        "Try running as administrator or reinstalling the application.\n\n" +
                        "Technical details:\n" + ex.Message + "\n\n" +
                        "Continue anyway? (UI may look broken)",
                        "Font Library Error",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning
                    );

                    if (result == DialogResult.Yes && mainForm != null)
                    {
                        // User wants to continue despite the error
                        mainForm.Show();
                    }
                    else
                    {
                        Application.Exit();
                    }
                }
                catch (Exception ex)
                {
                    // If loading fails, show error and close gracefully
                    splash.Close();
                    MessageBox.Show(
                        $"Failed to initialize application:\n\n{ex.Message}\n\n{ex.StackTrace}",
                        "Startup Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    Application.Exit();
                }
            }


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
            }).ConfigureAwait(false);
        }
    }
}

