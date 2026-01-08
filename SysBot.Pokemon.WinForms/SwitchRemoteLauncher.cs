using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    public static class SwitchRemoteLauncher
    {
        private static Process? _runningInstance;

        public static bool Launch(string? baseDirectory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(baseDirectory) || !Directory.Exists(baseDirectory))
                {
                    MessageBox.Show(
                        "Switch Remote for PC directory is not set or does not exist.",
                        "Switch Remote for PC",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return false;
                }

                string executablePath = Path.Combine(baseDirectory, "SwitchRemoteForPC.exe");

                if (!File.Exists(executablePath))
                {
                    MessageBox.Show(
                        "SwitchRemoteForPC.exe not found in the selected folder.",
                        "Switch Remote for PC",
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
                    FileName = executablePath,
                    WorkingDirectory = baseDirectory,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to launch Switch Remote for PC:\n{ex.Message}",
                    "Switch Remote for PC",
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
