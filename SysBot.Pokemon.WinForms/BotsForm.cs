using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SysBot.Base;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SysBot.Pokemon.WinForms.Controls;
using System.Diagnostics;


namespace SysBot.Pokemon.WinForms
{
    public partial class BotsForm : Form
    {
        private bool _isInitializing = false;

        public PictureBox ImageOverlay;
        public FlowLayoutPanel BotPanel => _FLP_Bots;

        public Button StartButton => _B_Start;
        public Button StopButton => _B_Stop;
        public Button RebootStopButton => _B_RebootStop;
        public Button UpdateButton => _updater;
        public Button AddBotButton => _B_New;

        public TextBox IPBox => _TB_IP;
        public NumericUpDown PortBox => _NUD_Port;

        public ComboBox ProtocolBox => _CB_Protocol;
        public ComboBox RoutineBox => _CB_Routine;

        private readonly List<BotController> BotControls = new();

        private FancyButton _B_Start;
        private FancyButton _B_Stop;
        private FancyButton _B_RebootStop;
        private FancyButton _updater;
        private FancyButton _B_New;
        private FancyButton _B_Reload;
        private ToolTip _toolTips;


        private TextBox _TB_IP;
        private NumericUpDown _NUD_Port;

        private ComboBox _CB_Protocol;
        private ComboBox _CB_Routine;
        private ComboBox _CB_GameMode;

        private FlowLayoutPanel _FLP_Bots;
        private PictureBox _pictureBox1;

        public BotsForm()
        {
            InitializeControls();
            _isInitializing = true;
            LoadGameModeFromConfig();
            _isInitializing = false;
        }

        private void InitializeControls()
        {
            _toolTips = new ToolTip
            {
                AutoPopDelay = 1000,
                InitialDelay = 2000,
                ReshowDelay = 1000,
                ShowAlways = true
            };

            // Buttons
            _B_Start = new FancyButton { Text = "START", Location = new Point(10, 7), Size = new Size(100, 40) };
            _B_Start.GlowColor = Color.LimeGreen;
            _toolTips.SetToolTip(_B_Start, "Start all bots together that are listed.");
            _toolTips.AutoPopDelay = 2500;      // How long it stays visible
            _toolTips.InitialDelay = 500;       // Delay before it shows up
            _toolTips.ReshowDelay = 1000;        // Delay between tooltips
            _toolTips.ShowAlways = true;        // Show even if the form isn’t active

            _B_Stop = new FancyButton { Text = "STOP", Location = new Point(120, 7), Size = new Size(100, 40) };
            _B_Stop.GlowColor = Color.Red;
            _toolTips.SetToolTip(_B_Stop, "Stop all running bots together that are listed.");
            _toolTips.AutoPopDelay = 2500;      // How long it stays visible
            _toolTips.InitialDelay = 2000;       // Delay before it shows up
            _toolTips.ReshowDelay = 1000;        // Delay between tooltips
            _toolTips.ShowAlways = true;        // Show even if the form isn’t active

            _B_RebootStop = new FancyButton { Text = "REBOOT", Location = new Point(230, 7), Size = new Size(100, 40) };
            _B_RebootStop.GlowColor = Color.Magenta;
            _toolTips.SetToolTip(_B_RebootStop, "Reboot game and stop all bots listed.");
            _toolTips.AutoPopDelay = 2500;      // How long it stays visible
            _toolTips.InitialDelay = 2000;       // Delay before it shows up
            _toolTips.ReshowDelay = 1000;        // Delay between tooltips
            _toolTips.ShowAlways = true;        // Show even if the form isn’t active

            _updater = new FancyButton { Text = "UPDATE", Location = new Point(340, 7), Size = new Size(100, 40) };
            _toolTips.SetToolTip(_updater, "Check for program updates.");
            _toolTips.AutoPopDelay = 2500;      // How long it stays visible
            _toolTips.InitialDelay = 2000;       // Delay before it shows up
            _toolTips.ReshowDelay = 1000;        // Delay between tooltips
            _toolTips.ShowAlways = true;        // Show even if the form isn’t active

            _B_New = new FancyButton { Text = "+", Location = new Point(423, 56), Size = new Size(54, 30) };
            _B_New.GlowColor = Color.White;
            _B_New.Font = new Font(_B_New.Font.FontFamily, 10, FontStyle.Bold);
            _toolTips.SetToolTip(_B_New, "Create a new bot slot.");
            _toolTips.AutoPopDelay = 2500;      // How long it stays visible
            _toolTips.InitialDelay = 2000;       // Delay before it shows up
            _toolTips.ReshowDelay = 1000;        // Delay between tooltips
            _toolTips.ShowAlways = true;        // Show even if the form isn’t active

            _B_Reload = new FancyButton { Text = "RELOAD", Location = new Point(625, 40), Size = new Size(100, 40) };
            _B_Reload.GlowColor = Color.DarkOrange;
            _toolTips.SetToolTip(_B_Reload, "Reload the application cleanly.");
            _toolTips.AutoPopDelay = 2500;      // How long it stays visible
            _toolTips.InitialDelay = 2000;       // Delay before it shows up
            _toolTips.ReshowDelay = 1000;        // Delay between tooltips
            _toolTips.ShowAlways = true;        // Show even if the form isn’t active

            _B_Reload.Click += (_, _) => RestartApplication();

            // Colors for boxes and controls
            Color darkBG = Color.FromArgb(20, 19, 57);
            Color whiteText = Color.White;

            // Controls
            _TB_IP = new TextBox { Location = new Point(12, 57), Width = 120, BackColor = Color.FromArgb(20, 19, 57), ForeColor = whiteText };
            _NUD_Port = new NumericUpDown { Location = new Point(144, 57), Width = 65, Maximum = 65535, Minimum = 0, Value = 6000, BackColor = Color.FromArgb(20, 19, 57), ForeColor = whiteText };

            _CB_Protocol = new ComboBox { Location = new Point(221, 57), Width = 62, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(20, 19, 57), ForeColor = whiteText };
            var protocols = ((SwitchProtocol[])Enum.GetValues(typeof(SwitchProtocol)))
                .Select(z => new { Text = z.ToString(), Value = (int)z }).ToArray();
            _CB_Protocol.DisplayMember = "Text";
            _CB_Protocol.ValueMember = "Value";
            _CB_Protocol.DataSource = protocols;
            _CB_Protocol.SelectedValue = (int)SwitchProtocol.WiFi;
            StyleComboBox(_CB_Protocol);

            _CB_Routine = new ComboBox { Location = new Point(294, 57), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(20, 19, 57), ForeColor = whiteText };
            var routines = ((PokeRoutineType[])Enum.GetValues(typeof(PokeRoutineType)))
                .Select(z => new { Text = z.ToString(), Value = (int)z }).ToArray();
            _CB_Routine.DisplayMember = "Text";
            _CB_Routine.ValueMember = "Value";
            _CB_Routine.DataSource = routines;
            _CB_Routine.SelectedValue = (int)PokeRoutineType.FlexTrade;
            StyleComboBox(_CB_Routine);

            _CB_GameMode = new ComboBox { Location = new Point(625, 5), Size = new Size(100, 40), DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(20, 19, 57), ForeColor = whiteText };
            _CB_GameMode.Items.AddRange(new object[] { "SWSH", "BDSP", "PLA", "SV", "LGPE" });
            _CB_GameMode.SelectedIndex = -1;
            _CB_GameMode.DrawItem += (s, e) =>
            {
                e.DrawBackground();
                var cb = (ComboBox)s;

                string text = (e.Index >= 0) ? cb.Items[e.Index].ToString() : "Game"; // ← Placeholder
                using var brush = new SolidBrush(cb.ForeColor);
                e.Graphics.DrawString(text, cb.Font, brush, e.Bounds);
            };
            _CB_GameMode.SelectedIndexChanged += CB_GameMode_SelectedIndexChanged;
            StyleComboBox(_CB_GameMode);

            _FLP_Bots = new FlowLayoutPanel
            {
                Location = new Point(10, 89),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Size = new Size(ClientSize.Width - 18, ClientSize.Height - 100),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = Color.FromArgb(28, 27, 65),
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            this.BackColor = Color.FromArgb(28, 27, 65); 

                Controls.AddRange(new Control[] {
                _B_Start, _B_Stop, _B_RebootStop, _updater, _B_New,
                _B_Reload, _TB_IP, _NUD_Port, _CB_Protocol, _CB_Routine, _CB_GameMode,
                _FLP_Bots
            });

            Text = "Bots Controller";
            Size = new Size(757, 53);
        }

        private void CB_GameMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isInitializing)
                return; // Don't do anything if we're still initializing

            if (_CB_GameMode.SelectedIndex == -1)
                return;
            var selectedMode = _CB_GameMode.SelectedItem?.ToString();
            int modeValue = selectedMode switch
            {
                "SWSH" => 1,
                "BDSP" => 2,
                "PLA" => 3,
                "SV" => 4,
                "LGPE" => 5,
                _ => 1
            };

            string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
            string exeDir = Path.GetDirectoryName(exePath)!;
            string configPath = Path.Combine(exeDir, "config.json");

            if (!File.Exists(configPath))
            {
                MessageBox.Show($"Config file not found at: {configPath}");
                return;
            }

            try
            {
                var jsonString = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement.Clone();

                using var stream = File.Create(configPath);
                using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

                writer.WriteStartObject();
                foreach (var prop in root.EnumerateObject())
                {
                    if (prop.NameEquals("Mode"))
                        writer.WriteNumber("Mode", modeValue);
                    else
                        prop.WriteTo(writer);
                }
                writer.WriteEndObject();

                MessageBox.Show($"Game environment updated to {selectedMode} (Mode: {modeValue}).\nRestart program or hit the RELOAD button");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to update config: " + ex.Message);
            }
        }

        private void LoadGameModeFromConfig()
        {
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName!;
                string exeDir = Path.GetDirectoryName(exePath)!;
                string configPath = Path.Combine(exeDir, "config.json");

                if (!File.Exists(configPath))
                {
                    MessageBox.Show($"Config file not found at: {configPath}");
                    return;
                }

                string json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                int mode = 1; // default to SWSH

                if (doc.RootElement.TryGetProperty("Mode", out var modeElement))
                    mode = modeElement.GetInt32();

                string modeText = mode switch
                {
                    1 => "SWSH",
                    2 => "BDSP",
                    3 => "PLA",
                    4 => "SV",
                    5 => "LGPE",
                    _ => "SWSH"
                };

                int index = _CB_GameMode.Items.IndexOf(modeText);
                if (index >= 0)
                    _CB_GameMode.SelectedIndex = index;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to load config for game mode: " + ex.Message);
            }
        }

        private void RestartApplication()
        {
            string exePath = Application.ExecutablePath;

            try
            {
                // Start a new instance
                System.Diagnostics.Process.Start(exePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to restart: {ex.Message}");
                return;
            }

            // Kill current one
            Application.Exit();
        }

        public void AddNewBot(IPokeBotRunner runner, PokeBotState cfg)
        {
            if (cfg == null)
                return;

            // Create a new BotController
            var controller = new BotController
            {
                Margin = new Padding(0),
                Padding = new Padding(0),
            };

            // 👇 Grab size from the first existing controller
            if (_FLP_Bots.Controls.Count > 0 && _FLP_Bots.Controls[0] is BotController existing)
            {
                controller.Size = existing.Size;
            }
            else
            {
                // Default size if no others exist
                controller.Size = new Size(700, 110);
            }

            controller.Initialize(runner, cfg);
            controller.Remove += (_, _) => RemoveBot(controller);
            controller.Click += (_, _) => LoadBotSettingsToUI(cfg);

            // Add and finalize
            _FLP_Bots.Controls.Add(controller);
            _FLP_Bots.SetFlowBreak(controller, true);
            BotControls.Add(controller);

            _FLP_Bots.PerformLayout();
            _FLP_Bots.Update();

            var source = runner.GetBot(cfg);
            if (source?.Bot?.Connection != null)
            {
                BotControllerManager.RegisterController(source.Bot.Connection.Label, controller);
            }
            else
            {
                Debug.WriteLine("Warning: could not register controller – missing bot or connection info.");
            }
        }

        private void RemoveBot(BotController controller)
        {
            _FLP_Bots.Controls.Remove(controller);
            BotControls.Remove(controller);
        }

        public void ReadAllBotStates()
        {
            foreach (var bot in BotControls)
                bot.ReloadStatus();
        }

        private void LoadBotSettingsToUI(PokeBotState cfg)
        {
            var details = cfg.Connection;
            _TB_IP.Text = details.IP;
            _NUD_Port.Value = details.Port;
            _CB_Protocol.SelectedValue = (int)details.Protocol;
            _CB_Routine.SelectedValue = (int)cfg.InitialRoutine;
        }

        private void StyleComboBox(ComboBox cb)
        {
            Color darkBG = Color.FromArgb(20, 19, 57);
            Color whiteText = Color.White;

            cb.BackColor = darkBG;
            cb.ForeColor = whiteText;
            cb.DrawMode = DrawMode.OwnerDrawFixed;
            cb.FlatStyle = FlatStyle.Flat;

            cb.DrawItem += (s, e) =>
            {
                if (e.Index < 0) return;

                ComboBox combo = (ComboBox)s;
                e.DrawBackground();

                // darker shade when selected
                Color bgColor = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                    ? Color.FromArgb(40, 39, 87)
                    : darkBG;

                using (SolidBrush bg = new SolidBrush(bgColor))
                    e.Graphics.FillRectangle(bg, e.Bounds);

                string text = combo.GetItemText(combo.Items[e.Index]);
                using (SolidBrush brush = new SolidBrush(whiteText))
                    e.Graphics.DrawString(text, combo.Font, brush, e.Bounds);
            };
        }
    }
}
