using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    static class Program
    {
        public static readonly string WorkingDirectory = Environment.CurrentDirectory = Path.GetDirectoryName(Environment.ProcessPath)!;
        public static string ConfigPath { get; private set; } = Path.Combine(WorkingDirectory, "config.json");

        [STAThread]
        private static void Main()
        {
#if NETCOREAPP
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
#endif

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Run splash as the main form temporarily
            var splash = new SplashScreen();
            splash.StartPosition = FormStartPosition.CenterScreen;
            splash.TopMost = true;

            // When splash loads, preload assets async
            splash.Shown += async (s, e) =>
            {
                await PreloadAssetsAsync();

                // After loading, switch to Main form
                var mainForm = new Main();
                mainForm.StartPosition = FormStartPosition.CenterScreen;
                mainForm.Show();

                splash.Hide();  // or splash.Close() if you want to dispose
            };

            // Start UI loop with splash as the main form
            Application.Run(splash);
        }


        private static async Task PreloadAssetsAsync()
        {
            // Simulate long loading
            await Task.Delay(1650);

            // Load fonts, images, etc.
            await Task.Run(() =>
            {
                FontManager.LoadFonts(
                "bahnschrift.ttf",
                "Bobbleboddy_light.ttf",
                "gadugi.ttf",
                "gadugib.ttf",
                "segoeui.ttf",
                "segoeuib.ttf",
                "segoeuii.ttf",
                "segoeuil.ttf",
                "UbuntuMono-R.ttf",
                "UbuntuMono-B.ttf",
                "UbuntuMono-BI.ttf",
                "UbuntuMono-RI.ttf",
                "segoeuisl.ttf",
                "segoeuiz.ttf",
                "seguibl.ttf",
                "seguibli.ttf",
                "seguili.ttf",
                "seguisb.ttf",
                "seguisbi.ttf",
                "seguisli.ttf",
                "SegUIVar.ttf",
                "Montserrat-Bold.ttf",
                "Montserrat-Regular.ttf",
                "Enter The Grid.ttf",
                "Gnuolane Rg.ttf"
                );


            });
        }
    }
}
