using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    public static class SysDVRLauncher
    {
        private static Process? _runningInstance;

        public static bool Launch()
        {
            try
            {
                // SysDVR.bat should be in the current working directory (same as Discord command)
                string sysDvrBatPath = "SysDVR.bat";

                if (!File.Exists(sysDvrBatPath))
                {
                    MessageBox.Show(
                        "SysDVR.bat cannot be found in the application directory.\n\n" +
                        "Please ensure SysDVR.bat is in the same folder as the FusionBot executable.",
                        "SysDVR",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
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
                    FileName = sysDvrBatPath,
                    UseShellExecute = true
                });

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to launch SysDVR:\n{ex.Message}",
                    "SysDVR",
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
