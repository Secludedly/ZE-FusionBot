using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using SysBot.Base;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;


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

        private Button _B_Start;
        private Button _B_Stop;
        private Button _B_RebootStop;
        private Button _updater;
        private Button _B_New;
        private Button _B_Reload;

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
            // Buttons
            _B_Start = new FancyButton { Text = "START", Location = new Point(10, 7), Size = new Size(100, 40) };
            _B_Stop = new FancyButton { Text = "STOP", Location = new Point(120, 7), Size = new Size(100, 40) };
            _B_RebootStop = new FancyButton { Text = "RESTART", Location = new Point(230, 7), Size = new Size(100, 40) };
            _updater = new FancyButton { Text = "UPDATE", Location = new Point(340, 7), Size = new Size(100, 40) };
            _B_New = new FancyButton { Text = "+", Location = new Point(423, 56), Size = new Size(54, 30) };
            _B_New.Font = new Font(_B_New.Font.FontFamily, 10, FontStyle.Bold);
            _B_Reload = new FancyButton { Text = "RELOAD", Location = new Point(625, 40), Size = new Size(100, 40) };
            _B_Reload.Click += (_, _) => RestartApplication();

            // Colors for boxes and controls
            Color darkBG = Color.FromArgb(20, 19, 57);
            Color whiteText = Color.White;

            // Controls
            _TB_IP = new TextBox { Location = new Point(12, 57), Width = 120, BackColor = Color.FromArgb(20, 19, 57), ForeColor = whiteText };
            _NUD_Port = new NumericUpDown { Location = new Point(144, 57), Width = 65, Maximum = 65535, Minimum = 0, Value = 6000, BackColor = Color.FromArgb(20, 19, 57), ForeColor = whiteText };

            _CB_Protocol = new ComboBox { Location = new Point(221, 57), Width = 60, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(20, 19, 57), ForeColor = whiteText };
            var protocols = ((SwitchProtocol[])Enum.GetValues(typeof(SwitchProtocol)))
                .Select(z => new { Text = z.ToString(), Value = (int)z }).ToArray();
            _CB_Protocol.DisplayMember = "Text";
            _CB_Protocol.ValueMember = "Value";
            _CB_Protocol.DataSource = protocols;
            _CB_Protocol.SelectedValue = (int)SwitchProtocol.WiFi;

            _CB_Routine = new ComboBox { Location = new Point(292, 57), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(20, 19, 57), ForeColor = whiteText };
            var routines = ((PokeRoutineType[])Enum.GetValues(typeof(PokeRoutineType)))
                .Select(z => new { Text = z.ToString(), Value = (int)z }).ToArray();
            _CB_Routine.DisplayMember = "Text";
            _CB_Routine.ValueMember = "Value";
            _CB_Routine.DataSource = routines;
            _CB_Routine.SelectedValue = (int)PokeRoutineType.FlexTrade;

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

            _FLP_Bots = new FlowLayoutPanel
            {
                Location = new Point(10, 89),
                Anchor = AnchorStyles.Top | AnchorStyles.Left,
                Size = new Size(ClientSize.Width - 24, ClientSize.Height - 100),
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
                WrapContents = false,
                FlowDirection = FlowDirection.TopDown,
                BackColor = Color.FromArgb(28, 27, 65)
            };

            this.BackColor = Color.FromArgb(28, 27, 65);
            this.Resize += (s, e) =>
            {
                _FLP_Bots.BackColor = Color.FromArgb(23, 22, 60);
                _FLP_Bots.Size = new Size(ClientSize.Width - 20, ClientSize.Height - 100); // Match the initial size minus padding
                ResizeBots();
            };

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
            // Now create the controller for the same config
            var controller = new BotController();
            controller.Initialize(runner, cfg);
            controller.Margin = new Padding(0, 1, 0, 1);
            controller.Remove += (s, e) => RemoveBot(controller);
            controller.Click += (s, e) => LoadBotSettingsToUI(cfg);
            _FLP_Bots.Controls.Add(controller);
            _FLP_Bots.SetFlowBreak(controller, true);
            BotControls.Add(controller);
            _FLP_Bots.PerformLayout();
            _FLP_Bots.Update();
            if (_FLP_Bots.Controls.Count > 0 && _FLP_Bots.Controls[0] is BotController first)
            {
                controller.Width = first.Width;
            }
            else
            {
                // Fallback just in case no bots exist yet
                controller.Width = _FLP_Bots.ClientSize.Width - 5;
            }
        }

        private void RemoveBot(BotController controller)
        {
            _FLP_Bots.Controls.Remove(controller);
            BotControls.Remove(controller);
        }

        private void ResizeBots()
        {
            int safeWidth = _FLP_Bots.ClientSize.Width - 5;

            foreach (var ctrl in BotControls)
        {
            ctrl.Width = safeWidth;
        }
    }

        public void ReadAllBotStates()
        {
            foreach (var bot in BotControls)
                bot.ReadState();
        }

        private void LoadBotSettingsToUI(PokeBotState cfg)
        {
            var details = cfg.Connection;
            _TB_IP.Text = details.IP;
            _NUD_Port.Value = details.Port;
            _CB_Protocol.SelectedValue = (int)details.Protocol;
            _CB_Routine.SelectedValue = (int)cfg.InitialRoutine;
        } 
    }
}
