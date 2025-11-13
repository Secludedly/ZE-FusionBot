using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    public class UpdateForm : Form
    {
        private Button buttonDownload;
        private Label labelUpdateInfo;
        private Label labelChangelogTitle;
        private TextBox textBoxChangelog;
        private readonly bool isUpdateRequired;
        private readonly bool isUpdateAvailable;
        private readonly string newVersion;

        public UpdateForm(bool updateRequired, string newVersion, bool updateAvailable)
        {
            isUpdateRequired = updateRequired;
            this.newVersion = newVersion;
            isUpdateAvailable = updateAvailable;

            InitializeComponent();
            ConfigureDynamicUpdateInfo();
            labelChangelogTitle.Text = $"Changelog ({newVersion}):";
            Load += async (sender, e) => await FetchAndDisplayChangelog();
            UpdateFormText();
        }

        public void PerformUpdate()
        {
            Application.Restart();
        }

        private void ConfigureDynamicUpdateInfo()
        {
            if (isUpdateRequired)
            {
                labelUpdateInfo.Text = "A required update is available. You must update to continue using this application.";
                ControlBox = false;
            }
            else if (isUpdateAvailable)
            {
                labelUpdateInfo.Text = "A new version is available. Please download the latest version.";
            }
            else
            {
                labelUpdateInfo.Text = "You are on the latest version. You can re-download if needed.";
                buttonDownload.Text = "Re-Download Latest Version";
            }

            if (string.IsNullOrWhiteSpace(buttonDownload.Text))
                buttonDownload.Text = "Download Update";
        }

        private void InitializeComponent()
        {
            labelUpdateInfo = new Label();
            buttonDownload = new Button();
            textBoxChangelog = new TextBox();
            SuspendLayout();
            // 
            // labelUpdateInfo
            // 
            labelUpdateInfo.AutoSize = true;
            labelUpdateInfo.ForeColor = Color.White;
            labelUpdateInfo.Location = new Point(12, 8);
            labelUpdateInfo.Name = "labelUpdateInfo";
            labelUpdateInfo.Size = new Size(158, 20);
            labelUpdateInfo.TabIndex = 0;
            labelUpdateInfo.Text = "Checking for updates...";
            // 
            // labelChangelogTitle
            //
            labelChangelogTitle = new Label();
            labelChangelogTitle.AutoSize = true;
            labelChangelogTitle.ForeColor = Color.White;
            labelChangelogTitle.Font = new Font("Segoe UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            labelChangelogTitle.Location = new Point(10, 48); // Moved higher
            labelChangelogTitle.Name = "labelChangelogTitle";
            labelChangelogTitle.Size = new Size(85, 20);
            labelChangelogTitle.TabIndex = 3;
            labelChangelogTitle.Text = $"Changelog:";
            // 
            // buttonDownload
            // 
            buttonDownload.BackColor = Color.FromArgb(20, 19, 57);
            buttonDownload.Dock = DockStyle.Bottom;
            buttonDownload.FlatStyle = FlatStyle.Flat;
            buttonDownload.ForeColor = Color.White;
            buttonDownload.Location = new Point(0, 275);
            buttonDownload.Name = "buttonDownload";
            buttonDownload.Size = new Size(708, 40);
            buttonDownload.TabIndex = 1;
            buttonDownload.Text = "Download Update";
            buttonDownload.UseVisualStyleBackColor = false;
            buttonDownload.Click += ButtonDownload_Click;
            // 
            // textBoxChangelog
            // 
            textBoxChangelog.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textBoxChangelog.BackColor = Color.FromArgb(20, 19, 57);
            textBoxChangelog.BorderStyle = BorderStyle.FixedSingle;
            textBoxChangelog.ForeColor = Color.White;
            textBoxChangelog.Location = new Point(10, 71);
            textBoxChangelog.Multiline = true;
            textBoxChangelog.Name = "textBoxChangelog";
            textBoxChangelog.ReadOnly = true;
            textBoxChangelog.ScrollBars = ScrollBars.Vertical;
            textBoxChangelog.Size = new Size(688, 187);
            textBoxChangelog.TabIndex = 2;
            // 
            // UpdateForm
            // 
            BackColor = Color.FromArgb(31, 30, 68);
            ClientSize = new Size(708, 300);
            Controls.Add(labelUpdateInfo);
            Controls.Add(buttonDownload);
            Controls.Add(textBoxChangelog);
            Controls.Add(labelChangelogTitle);
            ForeColor = Color.White;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "UpdateForm";
            StartPosition = FormStartPosition.CenterScreen;
            ResumeLayout(false);
            PerformLayout();
        }

        private void UpdateFormText()
        {
            Text = isUpdateAvailable ? $"Update Available ({newVersion})" : "Re-Download Latest Version";
        }

        private async Task FetchAndDisplayChangelog()
        {
            _ = new UpdateChecker();
            string changelog = await UpdateChecker.FetchChangelogAsync();
            textBoxChangelog.Text = changelog;
        }

        private async void ButtonDownload_Click(object sender, EventArgs e)
        {
            await PerformUpdateAsync();
        }

        public async Task PerformUpdateAsync()
        {
            buttonDownload.Enabled = false;
            buttonDownload.Text = "Downloading...";
            try
            {
                string? downloadUrl = await UpdateChecker.FetchDownloadUrlAsync();
                if (!string.IsNullOrWhiteSpace(downloadUrl))
                {
                    string downloadedFilePath = await StartDownloadProcessAsync(downloadUrl);
                    if (!string.IsNullOrEmpty(downloadedFilePath))
                        InstallUpdate(downloadedFilePath);
                }
                else
                {
                    MessageBox.Show("Failed to fetch the download URL. Please check your internet connection and try again.",
                        "Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Update failed: {ex.Message}", "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                buttonDownload.Enabled = true;
                buttonDownload.Text = isUpdateAvailable ? "Download Update" : "Re-Download Latest Version";
            }
        }


        private static async Task<string> StartDownloadProcessAsync(string downloadUrl)
        {
            Main.IsUpdating = true;
            string tempPath = Path.Combine(Path.GetTempPath(), $"SysBot.Pokemon.WinForms_{Guid.NewGuid()}.exe");
            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "ZE-FusionBot");
                var response = await client.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();
                var fileBytes = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(tempPath, fileBytes);
            }
            return tempPath;
        }
        private void InstallUpdate(string downloadedFilePath)
        {
            try
            {
                string currentExePath = Application.ExecutablePath;
                string applicationDirectory = Path.GetDirectoryName(currentExePath) ?? "";
                string executableName = Path.GetFileName(currentExePath);
                string backupPath = Path.Combine(applicationDirectory, $"{executableName}.backup");
                // Create batch file for update process
                string batchPath = Path.Combine(Path.GetTempPath(), "UpdateSysBot.bat");
                string batchContent = @$"
                                            @echo off
                                            timeout /t 2 /nobreak >nul
                                            echo Updating SysBot...
                                            rem Backup current version
                                            if exist ""{currentExePath}"" (
                                                if exist ""{backupPath}"" (
                                                    del ""{backupPath}""
                                                )
                                                move ""{currentExePath}"" ""{backupPath}""
                                            )
                                            rem Install new version
                                            move ""{downloadedFilePath}"" ""{currentExePath}""
                                            rem Start new version
                                            start """" ""{currentExePath}""
                                            rem Clean up
                                            del ""%~f0""
                                            ";
                File.WriteAllText(batchPath, batchContent);
                // Start the update batch file
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = batchPath,
                    CreateNoWindow = true,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                Process.Start(startInfo);
                // Exit the current instance
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to install update: {ex.Message}", "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
