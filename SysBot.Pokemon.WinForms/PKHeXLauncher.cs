using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    public static class PKHeXLauncher
    {
        private static Process? _runningInstance;

        public static bool Launch(string? baseDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseDirectory) || !Directory.Exists(baseDirectory))
                {
                    MessageBox.Show(
                        "PKHeX directory is not set or does not exist.",
                        "PKHeX",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return false;
                }

                var exePath = Directory
                    .GetFiles(baseDirectory, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f =>
                        Path.GetFileName(f)
                            .Contains("pkhex", StringComparison.OrdinalIgnoreCase));

                if (exePath == null)
                {
                    MessageBox.Show(
                        "No PKHeX executable found in the selected folder.",
                        "PKHeX",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                    return false;
                }

                if (_runningInstance != null && !_runningInstance.HasExited)
                {
                    _runningInstance.Focus();
                    return true;
                }

                _runningInstance = Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = baseDirectory,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to launch PKHeX:\n{ex.Message}",
                    "PKHeX",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return false;
            }
        }

        private static void Focus(this Process process)
        {
            try
            {
                process.Refresh();
                if (!process.HasExited)
                    process.WaitForInputIdle(100);
            }
            catch { }
        }
    }
}
