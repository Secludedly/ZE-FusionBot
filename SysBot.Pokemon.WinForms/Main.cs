using ControllerCommand = SysBot.Pokemon.WinForms.BotController.BotControlCommand;
using FontAwesome.Sharp;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.WinForms;
using SysBot.Pokemon.Discord;
using SysBot.Pokemon.Helpers;
using SysBot.Pokemon.WinForms.Controls;
using SysBot.Pokemon.WinForms.Properties;
using SysBot.Pokemon.Z3;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Drawing.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using static SysBot.Pokemon.WinForms.BotController;
using WebApiCommand = SysBot.Pokemon.WinForms.WebApi.BotControlCommand;

namespace SysBot.Pokemon.WinForms
{
    public sealed partial class Main : Form
    {
        // Main form properties
        private Form activeForm = null; // Active child form in the main panel

        // Running environment and configuration
        private IPokeBotRunner RunningEnvironment { get; set; } // Bot runner based on game mode

        // Program configuration
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public static ProgramConfig Config { get; set; }

        // Static properties for update state
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // Do not serialize in the designer
        public static bool IsUpdating { get; set; } = false;

        // Singleton instance of Main form
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public static Main? Instance { get; private set; }

        // Program has update flag
        internal bool hasUpdate = false;

        // Flag to indicate if the form is still loading
        private bool _isFormLoading = true;

        // Left‑side button styling
        private IconButton currentBtn;                             // Currently active button
        private Panel leftBorderBtn;                               // Left border panel for the active button
        private Dictionary<IconButton, Timer> hoverTimers = new(); // Dictionary to hold hover timers for buttons

        // BotsForm & runner to help manage the bots
        private readonly List<PokeBotState> Bots = new(); // List of bots created in the program
        private BotsForm _botsForm;                       // BotsForm instance to manage bot controls
        private IPokeBotRunner _runner;                   // Runner instance to manage bot operations

        // Place GameMode BG Images into panelLeftSide
        public Panel PanelLeftSide => panelLeftSide;

        // LogsForm loading
        private LogsForm _logsForm;

        // HubForm loading
        private HubForm _hubForm;

        private void EnsureFontAwesomeButtonsRender()
        {
            // Force the FontAwesome font to be applied and glyphs to render
            foreach (var btn in new[] { btnClose, btnMaximize, btnMinimize })
            {
                btn.Font = new Font("FontAwesome", btn.Font.Size); // Make sure FontAwesome is applied
                btn.Invalidate();                                  // Force redraw
            }
        }

        // Main Constructor
        public Main()
        {
            // Load custom fonts before initializing
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

            // GLOBAL EXCEPTION HANDLERS — LOG BEFORE BOT DIES
            TaskScheduler.UnobservedTaskException += (sender, e) =>
            {
                LogUtil.LogSafe(e.Exception, "🔥 UnobservedTaskException");
                e.SetObserved(); // Prevents app crash
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    LogUtil.LogSafe(ex, "💥 UnhandledException");
                else
                    LogUtil.LogGeneric($"💀 Non-Exception UnhandledException: {e.ExceptionObject}", "💥 UnhandledException");
            };

            Task.Run(BotMonitor);      // Start the bot monitor
            InitializeComponent();     // Initialize all the form components before program
            Instance = this;
            InitializeLeftSideImage(); // Initialize the left side BG image in panelLeftSide
            InitializeUpperImage();    // Initialize the upper image in panelTitleBar

            // Wait for the form crap to load before initializing
            this.Load += async (s, e) => await InitializeAsync();

            // Set up left‑panel buttons & effects
            ApplyButtonEffects();
            var baseColor = ThemeManager.CurrentColors.PanelBase; // Base color for buttons according to themes
            var hoverColor = ThemeManager.CurrentColors.Hover;    // Hover color for buttons according to themes
            SetupHoverAnimation(btnBots, baseColor, hoverColor);  // Bots button
            SetupHoverAnimation(btnHub, baseColor, hoverColor);   // Hub button
            SetupHoverAnimation(btnLogs, baseColor, hoverColor);  // Logs button
            leftBorderBtn = new Panel { Size = new Size(7, 60) }; // Left border for active button
            panelLeftSide.Controls.Add(leftBorderBtn);            // Add left border to the panel
            panelTitleBar.MouseDown += panelTitleBar_MouseDown;   // Allow dragging the window from the title bar
            HookDrag(panelTitleBar);


            // Title‑bar controls
            this.Text = ""; // Set the form title to empty

            this.ControlBox = false;                                           // Disable the default Minimize/Maximize/Close
            this.FormBorderStyle = FormBorderStyle.None;                       // Remove the default form border
            this.DoubleBuffered = true;                                        // Enable double buffering to reduce flickering
            this.SetStyle(ControlStyles.ResizeRedraw, true);                   // Redraw on resize
            this.MaximizedBounds = Screen.FromHandle(this.Handle).WorkingArea; // Set the maximized bounds to the working area of the screen
            this.AutoScaleMode = AutoScaleMode.Dpi;                            // Set auto scale mode to DPI
            this.AutoScaleDimensions = new SizeF(96F, 96F);                    // Set auto scale dimensions to 96 DPI


            // Handlers for the Close/Maximize/Minimize buttons
            btnClose.Click += BtnClose_Click;       // Close button
            btnMaximize.Click += BtnMaximize_Click; // Maximize button
            btnMinimize.Click += BtnMinimize_Click; // Minimize button

            // Set up hover animations for Close/Maximize/Minimize buttons
            SetupTopButtonHoverAnimation(btnClose, Color.FromArgb(232, 17, 35));    // Color is red
            SetupTopButtonHoverAnimation(btnMaximize, Color.FromArgb(0, 120, 215)); // Color is blue
            SetupTopButtonHoverAnimation(btnMinimize, Color.FromArgb(255, 185, 0)); // Color is yellow
        }

        // Runs once when Main form first loads
        private async Task InitializeAsync()
        {
            if (IsUpdating)
                return;

            PokeTradeBotSWSH.SeedChecker = new Z3SeedSearchHandler<PK8>(); // Initialize the seed checker for SWSH mode

            try
            {
                var (updateAvailable, _, _) = await UpdateChecker.CheckForUpdatesAsync(); // Check for updates
                hasUpdate = updateAvailable; // If there's an update, this flag gets checked
            }
            catch { }
            _botsForm = new BotsForm(); // Initialize the BotsForm instance
            _logsForm = new LogsForm(); // Initialize the LogsForm instance
            LogUtil.Forwarders.Add(new LogTextBoxForwarder(_logsForm.LogsBox)); // Add a log forwarder to the LogsForm's LogsBox
            _logsForm.LogsBox.MaxLength = 32767; // Set the maximum length of the LogsBox to 32767 characters (why this number though?)

            // If it knows a config file exists in root folder then load that shit up
            if (File.Exists(Program.ConfigPath))
            {
                var lines = File.ReadAllText(Program.ConfigPath);
                Config = JsonSerializer.Deserialize<ProgramConfig>(lines) ?? new ProgramConfig();
                LogConfig.MaxArchiveFiles = Config.Hub.MaxArchiveFiles;
                LogConfig.LoggingEnabled = Config.Hub.LoggingEnabled;

                // Clean up bad bot entries
                Config.Bots = Config.Bots
                    .Where(b => b != null && b.IsValid() && !string.IsNullOrWhiteSpace(b.Connection?.IP))
                    .GroupBy(b => $"{b.Connection.IP}:{b.Connection.Port}")
                    .Select(g => g.First())
                    .ToArray();

                RunningEnvironment = GetRunner(Config);

                foreach (var bot in Config.Bots)
                {
                    if (!Bots.Any(b => b.Connection.Equals(bot.Connection)))
                    {
                        if (string.IsNullOrWhiteSpace(bot.Connection?.IP) || bot.Connection.Port <= 0) // Check if the bot has a valid IP and port
                        {
                            Console.WriteLine("Skipping invalid bot with empty IP or port."); // Fuck off, failure.
                            continue;
                        }
                        bot.Initialize();
                        AddBot(bot);
                    }
                }
            }
            else
            {
                // config.json shits
                Config = new ProgramConfig();
                RunningEnvironment = GetRunner(Config); // What mode is this bitch on?
                Config.Hub.Folder.CreateDefaults(Program.WorkingDirectory); // Hubbabubba
            }
            // Load other form shit and/or save valuable shit to config
            LoadControls();
            Text = $"{(string.IsNullOrEmpty(Config.Hub.BotName) ? "ZE FusionBot |" : Config.Hub.BotName)} {TradeBot.Version} | Mode: {Config.Mode}";
            UpdateBackgroundImage(Config.Mode);        // Call the method to update image in leftSidePanel
            UpdateUpperImage(Config.Mode);        // Call the method to update image in panelTitleBar
            LoadThemeOptions();

            CB_Themes.SelectedIndexChanged += CB_Themes_SelectedIndexChanged;
            LoadLogoImage(Config.Hub.BotLogoImage); // Load a URL image to replace logo
            InitUtil.InitializeStubs(Config.Mode);     // Stubby McStubbinson will set environment based on config mode
            _isFormLoading = false;                    // ...but is it loading?
            ActivateButton(btnBots, RGBColors.color9); // We gonna start this party off right with the Bots Control panel and set its button color
            OpenChildForm(_botsForm);                  // I don't usually like to incite violence on children, but this time, open them kids up!
            SaveCurrentConfig();                       // Save me from myself... or save to the config.

            _botsForm.StartButton.Click += B_Start_Click;           // Start button... do any of these really need explaining?
            _botsForm.StopButton.Click += B_Stop_Click;             // Stop button
            _botsForm.RebootStopButton.Click += B_RebootStop_Click; // Reboot and Stop button
            _botsForm.UpdateButton.Click += Updater_Click;          // Update button
            _botsForm.AddBotButton.Click += B_New_Click;            // Add button

            lblTitle.Text = Text; // Set the title label text to the form's text

            this.ActiveControl = null;
            LogUtil.LogInfo("System", "Bot initialization complete");
            // Start web server async to avoid UI blocking
            _ = Task.Run(() =>
            {
                try
                {
                    this.InitWebServer();
                }
                catch (Exception ex)
                {
                    LogUtil.LogError($"Failed to initialize web server: {ex.Message}", "System");
                }
            });
        }

        // Save the current running environment config
        private static IPokeBotRunner GetRunner(ProgramConfig cfg) => cfg.Mode switch
        {
            ProgramMode.SWSH => new PokeBotRunnerImpl<PK8>(cfg.Hub, new BotFactory8SWSH()), // Too lazy, won't explain, very simple, much sexy
            ProgramMode.BDSP => new PokeBotRunnerImpl<PB8>(cfg.Hub, new BotFactory8BS()),
            ProgramMode.LA => new PokeBotRunnerImpl<PA8>(cfg.Hub, new BotFactory8LA()),
            ProgramMode.SV => new PokeBotRunnerImpl<PK9>(cfg.Hub, new BotFactory9SV()),
            ProgramMode.PLZA => new PokeBotRunnerImpl<PA9>(cfg.Hub, new BotFactory9PLZA()),
            ProgramMode.LGPE => new PokeBotRunnerImpl<PB7>(cfg.Hub, new BotFactory7LGPE()),
            _ => throw new IndexOutOfRangeException("Unsupported mode."), // A LIE
        };

        // Read the fucking state of all bots in the BotsForm like a creepy code stalker
        private async Task BotMonitor()
        {
            while (!Disposing)
            {
                try
                {
                    if (_botsForm?.BotPanel?.Controls == null) // If the BotPanel or its controls are null, skip it for being a bitch
                        continue; // Yes, you may.

                    foreach (var bot in _botsForm.BotPanel.Controls.OfType<BotController>()) // Iterate through each BotController in the BotPanel
                        bot.ReloadStatus(); // Read the state of the bot controller to update its UI, but do it sexy
                }
                catch { /* Fail silently, iterator safety */ }

                await Task.Delay(2_000).ConfigureAwait(false); // Wait for 2 seconds before checking the bot states again, which is longer than I lasted with my wife
            }
        }

        // Load the controls for the BotsForm
        private void LoadControls()
        {
            // Establish global minimum size for the BotsForm
            MinimumSize = Size;

            // Routine Selection
            var routines = ((PokeRoutineType[])Enum.GetValues(typeof(PokeRoutineType))).Where(z => RunningEnvironment.SupportsRoutine(z)) // Get all routine types
                .Select(z => new { Text = z.ToString(), Value = (int)z }).ToList(); // Create a list of routine types with their text and value
            _botsForm.RoutineBox.DisplayMember = "Text";                            // Set the display text for the RoutineBox
            _botsForm.RoutineBox.ValueMember = "Value";                             // Set the value number for the RoutineBox (Flextrade, etc.)
            _botsForm.RoutineBox.DataSource = routines;                             // Bind the RoutineBox to the list of routine types (Dropdown list)
            _botsForm.RoutineBox.SelectedValue = (int)PokeRoutineType.FlexTrade;    // Set the default to FlexTrade in RoutineBox

            // Protocol Selection
            var protocols = ((SwitchProtocol[])Enum.GetValues(typeof(SwitchProtocol))) // Get all switch protocols
                .Select(z => new { Text = z.ToString(), Value = (int)z }).ToList();    // Create a list of protocols with their text and value
            _botsForm.ProtocolBox.DisplayMember = "Text";                              // Set the display text for the ProtocolBox
            _botsForm.ProtocolBox.ValueMember = "Value";                               // Set the value number for the ProtocolBox (WiFi/USB)
            _botsForm.ProtocolBox.DataSource = protocols;                              // Bind the ProtocolBox to the list of protocols (Dropdown list)
            _botsForm.ProtocolBox.SelectedValue = (int)SwitchProtocol.WiFi;            // Set the default to WiFi in ProtocolBox
            SaveCurrentConfig();                                                       // Save the current config for BotsForm data
            
            try
            {
                string? exePath = Application.ExecutablePath;
                if (!string.IsNullOrEmpty(exePath))
                {
                    string? dirPath = Path.GetDirectoryName(exePath);
                    if (!string.IsNullOrEmpty(dirPath))
                    {
                        string portInfoPath = Path.Combine(dirPath, $"MergeBot_{Environment.ProcessId}.port");
                        if (File.Exists(portInfoPath))
                            File.Delete(portInfoPath);
                    }
                }
            }
            catch { }
        }

        // Start the bot with the current config
        private void B_Start_Click(object sender, EventArgs e) // Start all bots on Start button click
        {
            SaveCurrentConfig();                               // Save the current config before starting the bot

            LogUtil.LogInfo("Starting all bots...", "Form");   // Log the start action
            RunningEnvironment.InitializeStart();              // Initialize the bot runner
            SendAll(WebApi.BotControlCommand.Start);                  // Send the Start command to all bots present in the controller
            _logsForm.LogsBox.Select();                        // Select the logs box in the LogsForm to write to

            if (Bots.Count == 0)
                WinFormsUtil.Alert("No bots configured, but all supporting services have been started.");
        }

        // Restart the bot and stop all consoles with current config
        private void B_RebootStop_Click(object sender, EventArgs e) // Restart all bots and reboot the game on console
        {
            B_Stop_Click(sender, e); // Stop all bots first

            // Log the reboot and stop action
            Task.Run(async () =>
            {
                await Task.Delay(3_500).ConfigureAwait(false);             // Add 3.5 second delay before rebooting
                SaveCurrentConfig();                                       // Save the current config before rebooting
                LogUtil.LogInfo("Restarting all the consoles...", "Form"); // Log the restart bots action
                RunningEnvironment.InitializeStart();                      // Start up the bot runner again
                SendAll(WebApi.BotControlCommand.RebootAndStop);                  // Send the RebootAndStop command to all bots
                await Task.Delay(5_000).ConfigureAwait(false);             // Add a 5 second delay before restarting the bots
                SendAll(WebApi.BotControlCommand.Start);                          // Start the bot after the delay
                _logsForm.LogsBox.Select();                                // Select the logs box in the LogsForm to write to

                if (Bots.Count == 0)
                    WinFormsUtil.Alert("No bots configured, but all supporting services have been issued the reboot command.");
            });
        }

        // Sends command to all bots when them buttons be pushed
        private void SendAll(WebApiCommand cmd)
        {
            RunningEnvironment.InitializeStart();

            foreach (var c in _botsForm.BotPanel.Controls.OfType<BotController>())
            {
                var translated = TranslateCommand(cmd);
                c.SendCommand(translated);
                c.ReloadStatus();

                if (translated == BotController.BotControlCommand.Stop)
                    c.ResetProgress();
            }
        }
        private BotController.BotControlCommand TranslateCommand(WebApiCommand webCmd)
        {
            return webCmd switch
            {
                WebApiCommand.Start => BotController.BotControlCommand.Start,
                WebApiCommand.Stop => BotController.BotControlCommand.Stop,
                WebApiCommand.Idle => BotController.BotControlCommand.Idle,
                WebApiCommand.Resume => BotController.BotControlCommand.Resume,
                WebApiCommand.Restart => BotController.BotControlCommand.Restart,
                WebApiCommand.RebootAndStop => BotController.BotControlCommand.RebootAndStop,

                WebApiCommand.ScreenOnAll => BotController.BotControlCommand.ScreenOn,
                WebApiCommand.ScreenOffAll => BotController.BotControlCommand.ScreenOff,

                _ => BotController.BotControlCommand.None
            };
        }

        // Stop or Idle/Resume all bots
        private void B_Stop_Click(object sender, EventArgs e)     // Stop all bots on Stop button click
        {
            var env = RunningEnvironment;                         // Get the current running environment
            if (!_botsForm.BotPanel.Controls.OfType<BotController>().Any(c => c.IsRunning()) && (ModifierKeys & Keys.Alt) == 0)
            // If not running and no Alt key pressed
            {
                WinFormsUtil.Alert("Nothing's running, genius."); // Derp
                return;
            }

            var cmd = WebApi.BotControlCommand.Stop; // Default command to stop all bots

            if ((ModifierKeys & Keys.Control) != 0 || (ModifierKeys & Keys.Shift) != 0) // If Control or Shift key is pressed (Honestly didn't know this ever existed. Cool shit)
            {
                if (env.IsRunning)
                {
                    WinFormsUtil.Alert("Commanding all bots to Idle.", "Press Stop (without a modifier key) to hard-stop and unlock control, or press Stop with the modifier key again to resume.");
                    cmd = WebApi.BotControlCommand.Idle;
                }
                else
                {
                    WinFormsUtil.Alert("Commanding all bots to resume their original task.", "Press Stop (without a modifier key) to hard-stop and unlock control.");
                    cmd = WebApi.BotControlCommand.Resume;
                }
            }
            else
            {
                env.StopAll(); // Stop in the name of love. (All bots)
            }
            SendAll(cmd);
        }

        // Add a new bot with the current config
        private void B_New_Click(object sender, EventArgs e) // Add a new bot on Add button click
        {
            var cfg = CreateNewBotConfig(); // Create a new bot config based on current settings in BotsForm

            // If the config is null or invalid, show an alert and return
            if (cfg == null)
                return;
            if (!AddBot(cfg))
            {
                WinFormsUtil.Alert("Unable to add bot; ensure details are valid and not duplicate with an already existing bot.");
                return;
            }
            System.Media.SystemSounds.Asterisk.Play(); // Play a sound to indicate the bot was added successfully
        }

        // Update handling
        private async void Updater_Click(object sender, EventArgs e)
        {
            await UpdateChecker.CheckForUpdatesAsync(forceShow: true); // Will auto-handle the UpdateForm without all the other crap
        }

        // Add a new bot to the environment and UI
        private bool AddBot(PokeBotState? cfg)
        {
            if (!cfg.IsValid())
                return false;

            if (Bots.Any(z => z.Connection.Equals(cfg.Connection)))
                return false;

            PokeRoutineExecutorBase newBot;
            try
            {
                newBot = RunningEnvironment.CreateBotFromConfig(cfg);
            }
            catch
            {
                return false;
            }

            try
            {
                RunningEnvironment.Add(newBot);
            }
            catch (ArgumentException ex)
            {
                WinFormsUtil.Error(ex.Message);
                return false;
            }

            AddBotControl(cfg);
            Bots.Add(cfg);
            Config.Bots = Bots.ToArray();
            SaveCurrentConfig();
            HookBotProgress(cfg, newBot);

            return true;
        }

        private void AddBotControl(PokeBotState cfg)
        {
            var row = new BotController { Width = _botsForm.BotPanel.Width };
            row.Initialize(RunningEnvironment, cfg);
            _botsForm.BotPanel.Controls.Add(row);
            _botsForm.BotPanel.SetFlowBreak(row, true);
            row.ReloadStatus();
            ProgressHelper.Initialize(row);

            row.Click += (s, e) =>
            {
                var details = cfg.Connection;
                _botsForm.IPBox.Text = details.IP;
                _botsForm.PortBox.Value = details.Port;
                _botsForm.ProtocolBox.SelectedIndex = (int)details.Protocol;
                _botsForm.RoutineBox.SelectedValue = (int)cfg.InitialRoutine;
            };

            row.Remove += (s, e) =>
            {
                Bots.RemoveAll(b => b.Connection.Equals(row.State.Connection));
                RunningEnvironment.Remove(row.State, !RunningEnvironment.Config.SkipConsoleBotCreation);
                _botsForm.BotPanel.Controls.Remove(row);
                Config.Bots = Bots.ToArray();
                SaveCurrentConfig();
            };
        }

        private void CB_Themes_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CB_Themes.SelectedItem is not string selected)
                return;

            Config.Theme = selected;
            SaveCurrentConfig();
            ThemeManager.ApplyTheme(this, selected);
        }

        private void LoadThemeOptions()
        {
            CB_Themes.Items.Clear();
            foreach (var key in ThemeManager.ThemePresets.Keys)
                CB_Themes.Items.Add(key);

            CB_Themes.SelectedItem = Config.Theme;
            ThemeManager.ApplyTheme(this, Config.Theme);
        }

        // Creates a new bot config based on current settings in BotsForm class
        private PokeBotState CreateNewBotConfig() // Create a new bot configuration based on the current settings in the BotsForm
        {
            var ip = _botsForm.IPBox.Text.Trim();    // Get the IP address from the IPBox and trim any whitespace
            var port = (int)_botsForm.PortBox.Value; // Get the port number from the PortBox
            if (string.IsNullOrWhiteSpace(ip))       // Check if the IP address is empty or whitespace
            {
                WinFormsUtil.Error("IP address cannot be empty.");
                return null;
            }
            if (!System.Net.IPAddress.TryParse(ip, out _))
            {
                WinFormsUtil.Error($"Invalid IP address: {ip}");
                return null;
            }
            if (_botsForm.ProtocolBox.SelectedValue == null)
            {
                WinFormsUtil.Error("Please select a protocol.");
                return null;
            }
            if (_botsForm.RoutineBox.SelectedValue == null)
            {
                WinFormsUtil.Error("Please select a routine.");
                return null;
            }

            // Create a new SwitchConnectionConfig based on the IP and port
            var cfg = BotConfigUtil.GetConfig<SwitchConnectionConfig>(ip, port); // Get the connection config based on the IP and port
            cfg.Protocol = (SwitchProtocol)_botsForm.ProtocolBox.SelectedValue;  // Set the protocol from the ProtocolBox
            var pk = new PokeBotState { Connection = cfg };                      // Create a new PokeBotState with the connection config
            var type = (PokeRoutineType)_botsForm.RoutineBox.SelectedValue;      // Set the routine type from the RoutineBox
            pk.Initialize(type);                                                 // Initialize the PokeBotState with the selected routine type
            return pk;                                                           // Return the new PokeBotState configuration
        }

        // Initialize the method for the left side image in the panelLeftSide
        private PictureBox leftSideImage;

        // Initialize the meat and potatoes for the left side image in the panelLeftSide
        private void InitializeLeftSideImage()
        {
            leftSideImage = new PictureBox
            {
                Size = new Size(200, 35),             // Put actual image dimensions here, or add custom to resize
                Location = new Point(180, 685),        // Fixed position for the image using XY
                SizeMode = PictureBoxSizeMode.Normal, // Makes sure the image is not stretched or resized
                BackColor = Color.Transparent,        // Makes sure the image has no background
                BorderStyle = BorderStyle.None        // Makes sure there's no vague borders and shit
            };

            panelLeftSide.Controls.Add(leftSideImage);                 // Add the left side image to the panelLeftSide
            panelLeftSide.Resize += (s, e) => PositionLeftSideImage(); // Reposition the image when the panel is resized
            PositionLeftSideImage();                                   // Position the image initially
        }

        // Position the left side image in the panelLeftSide
        private void PositionLeftSideImage()
        {
            if (leftSideImage == null || panelLeftSide == null)
                return;

            // Get the actual usable width inside the panel
            int usableWidth = panelLeftSide.ClientSize.Width
                              - panelLeftSide.Padding.Left
                              - panelLeftSide.Padding.Right;

            // Perfect horizontal centering
            int horizontalCenter = panelLeftSide.Padding.Left
                                   + (usableWidth - leftSideImage.Width) / 2;

            // Vertical: below your theme selector
            int verticalOffsetBelowTheme = CB_Themes.Bottom + 20;

            leftSideImage.Location = new Point(horizontalCenter, verticalOffsetBelowTheme);
            leftSideImage.SizeMode = PictureBoxSizeMode.Zoom;
            leftSideImage.Anchor = AnchorStyles.Top;

        }

        // Initialize the method for the upper panel image in the upperPanelImage
        private PictureBox upperPanelImage;

        private void InitializeUpperImage()
        {
            upperPanelImage = new PictureBox
            {
                Size = new Size(325, 72),
                Location = new Point(560, 0),
                SizeMode = PictureBoxSizeMode.Normal,
                BackColor = Color.Transparent,
                BorderStyle = BorderStyle.None
            };

            panelTitleBar.Controls.Add(upperPanelImage);
            panelTitleBar.Resize += (s, e) => PositionUpperImage();
            PositionUpperImage();
        }

        // Position the upper image in the upperPanelImage
        private void PositionUpperImage()
        {
            if (upperPanelImage == null || panelTitleBar == null)
                return;

            int usableWidth = panelTitleBar.ClientSize.Width
                              - panelTitleBar.Padding.Left
                              - panelTitleBar.Padding.Right;

            int centerX = panelTitleBar.Padding.Left
                          + (usableWidth - upperPanelImage.Width) / 1;

            upperPanelImage.Location = new Point(centerX, 0);
        }

        private void LoadLogoImage(string logoPath)
        {
            if (string.IsNullOrWhiteSpace(logoPath))
                return;

            try
            {
                if (Uri.IsWellFormedUriString(logoPath, UriKind.Absolute))
                {
                    using var client = new WebClient();
                    using var stream = client.OpenRead(logoPath);
                    if (stream != null)
                        pictureLogo.Image = Image.FromStream(stream);
                }
                else
                {
                    string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                    string fullPath = Path.Combine(exeDir, logoPath);
                    if (File.Exists(fullPath))
                        pictureLogo.Image = Image.FromFile(fullPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Logo Load] Failed to load logo: {ex.Message}");
            }
        }

        // Update the background image based on the current game mode
        private void UpdateBackgroundImage(ProgramMode mode)
        {
            if (leftSideImage == null) return;

            switch (mode)
            {
                case ProgramMode.PLZA:
                    leftSideImage.Image = Resources.plza_mode_image; // Set the image for SV mode
                    break;
                case ProgramMode.SV:
                    leftSideImage.Image = Resources.sv_mode_image;   // Set the image for SV mode
                    break;
                case ProgramMode.SWSH:
                    leftSideImage.Image = Resources.swsh_mode_image; // Set the image for SWSH mode
                    break;
                case ProgramMode.BDSP:
                    leftSideImage.Image = Resources.bdsp_mode_image; // Set the image for BDSP mode
                    break;
                case ProgramMode.LA:
                    leftSideImage.Image = Resources.pla_mode_image;  // Set the image for PLA mode
                    break;
                case ProgramMode.LGPE:
                    leftSideImage.Image = Resources.lgpe_mode_image; // Set the image for LGPE mode
                    break;
                default:
                    leftSideImage.Image = null;
                    break;
            }
        }

        // Update the upper image based on the current game mode
        private void UpdateUpperImage(ProgramMode mode)
        {
            if (upperPanelImage == null) return;

            switch (mode)
            {
                case ProgramMode.PLZA:
                    upperPanelImage.Image = Resources.plza_mode_upper;
                    break;

                case ProgramMode.SV:
                    upperPanelImage.Image = Resources.sv_mode_upper;
                    break;

                case ProgramMode.SWSH:
                    upperPanelImage.Image = Resources.swsh_mode_upper;
                    break;

                case ProgramMode.BDSP:
                    upperPanelImage.Image = Resources.bdsp_mode_upper;
                    break;

                case ProgramMode.LA:
                    upperPanelImage.Image = Resources.pla_mode_upper;
                    break;

                case ProgramMode.LGPE:
                    upperPanelImage.Image = Resources.lgpe_mode_upper;
                    break;

                default:
                    upperPanelImage.Image = null;
                    break;
            }
        }

        private void HookBotProgress(PokeBotState cfg, PokeRoutineExecutorBase bot)
        {
            BotController? botControl = _botsForm.BotPanel.Controls
                .OfType<BotController>()
                .FirstOrDefault(c => c.State.Connection.Equals(cfg.Connection));

            if (botControl == null)
                return;

            ProgressHelper.Initialize(botControl); // Only if you're using this style

            void SetProgress(int percent)
            {
                if (_botsForm.InvokeRequired)
                    _botsForm.BeginInvoke((Action)(() => botControl.SetProgressValue(percent)));
                else
                    botControl.SetProgressValue(percent);
            }

            switch (bot)
            {
        //        case PokeTradeBotPLZA zaBot:
        //            zaBot.TradeProgressChanged += SetProgress;
        //            break;
                case PokeTradeBotSV svBot:
                    svBot.TradeProgressChanged += SetProgress;
                    break;
                case PokeTradeBotSWSH swshBot:
                    swshBot.TradeProgressChanged += SetProgress;
                    break;
                case PokeTradeBotBS bsBot:
                    bsBot.TradeProgressChanged += SetProgress;
                    break;
                case PokeTradeBotLA laBot:
                    laBot.TradeProgressChanged += SetProgress;
                    break;
                case PokeTradeBotLGPE lgpeBot:
                    lgpeBot.TradeProgressChanged += SetProgress;
                    break;
            }
        }

        // Resize the BotController controls when the panel is resized, focused on width
        private void FLP_Bots_Resize(object sender, EventArgs e)
        {
            // Resize all BotController controls in the BotPanel to match the width of the panel
            foreach (var c in _botsForm.BotPanel.Controls.OfType<BotController>()) // Iterate through each BotController in the BotPanel
                c.Width = _botsForm.BotPanel.Width;                                // Set the width of the BotController to the width of the BotPanel
        }

        // Protocol and IP selection handling
        private void CB_Protocol_SelectedIndexChanged(object sender, EventArgs e)
        {
            _botsForm.IPBox.Visible = _botsForm.ProtocolBox.SelectedIndex == 0; // Show the IPBox only if the selected protocol is WiFi
        }

        private void InitializeTitleBarDrag()
        {
            // Original panel mouse down
            panelTitleBar.MouseDown += panelTitleBar_MouseDown;

            // Forward mouse events from all children to panel
            foreach (Control ctrl in panelTitleBar.Controls)
            {
                ctrl.MouseDown += panelTitleBar_MouseDown;
            }
        }

        private void HookDrag(Control parent)
        {
            parent.MouseDown += panelTitleBar_MouseDown;
            foreach (Control child in parent.Controls)
                HookDrag(child);
        }

        // Drag the window from the titlebar
        [DllImport("user32.DLL", EntryPoint = "ReleaseCapture")] // Release the mouse capture
        private extern static void ReleaseCapture();             // Release the mouse capture to allow dragging the window
        [DllImport("user32.DLL", EntryPoint = "SendMessage")]    // Send a message to the window
        private extern static void SendMessage(System.IntPtr hWnd, int wMsg, int wParam, int lParam); // Send a message to the window to allow dragging
        private void panelTitleBar_MouseDown(object sender, MouseEventArgs e)                         // Allow dragging the window from the title bar
        {
            if (sender == btnClose || sender == btnMaximize || sender == btnMinimize)
                return;
            ReleaseCapture();                           // Release the mouse capture
            SendMessage(this.Handle, 0x112, 0xf012, 0); // Send a message to the window to allow dragging
        }

        // Method to activate Bots button and change its color, loading BotsForm class
        private void Bots_Click(object sender, EventArgs e)
        {
            ActivateButton(sender, RGBColors.color9); // Change the color of the active Bots button
            OpenChildForm(_botsForm);                 // Load the BotsForm in the main panel
        }

        // Method to activate Hub button and change its color, loading HubForm class
        private void Hub_Click(object sender, EventArgs e)
        {
            ActivateButton(sender, RGBColors.color5); // Change the color of the active Hub button
            currentBtn.Refresh();                     // Refresh the current button to apply the new color
            leftBorderBtn.Refresh();                  // Refresh the left border to match the active button

            // If the HubForm is not initialized, create a new instance
            if (_hubForm == null || _hubForm.IsDisposed)
                _hubForm = new HubForm(Config.Hub);

            OpenChildForm(_hubForm); // Load the HubForm in the main panel
        }

        // Method to activate Logs button and change its color, loading LogsForm class
        private void Logs_Click(object sender, EventArgs e)
        {
            ActivateButton(sender, Color.FromArgb(0, 255, 255)); // Change the color of the active Logs button
            currentBtn.Refresh();                                // Refresh the current button to apply the new color
            leftBorderBtn.Refresh();                             // Refresh the left border to match the active button
            OpenChildForm(_logsForm);                            // Load the LogsForm in the main panel
        }

        // Close button
        private void BtnClose_Click(object sender, EventArgs e)
        {
            Application.Exit(); // Exit program on Close button click
        }

        // Maximize and Restore button
        private void BtnMaximize_Click(object sender, EventArgs e)
        {

            if (WindowState == FormWindowState.Normal)   // If the window is in normal state, then...
                WindowState = FormWindowState.Maximized; // ...Maximize the window
            else
                WindowState = FormWindowState.Normal; // Restore the window to normal state if Maximized
        }

        // Minimize button
        private void BtnMinimize_Click(object sender, EventArgs e)
        {
            WindowState = FormWindowState.Minimized; // Minimize the window on Minimize button click
        }

        // Method and logic to open the child form(Bots, Hub, or Logs) in panelMain
        private async void OpenChildForm(Form childForm)
        {
            // If the active form is already open, hide it
            if (activeForm != null) activeForm.Hide();

            activeForm = childForm;                           // Set the active form to the new child form
            childForm.TopLevel = false;                       // Set the child form to be a non-top-level form
            childForm.FormBorderStyle = FormBorderStyle.None; // Remove the border style of the child form
            panelMain.Controls.Add(childForm);                // Add the child form to the main panel
            SlideFadeInForm(childForm);                       // Activate the SlideFadeInForm method to the child form
            childForm.Size = panelMain.ClientSize;                         // Set the size of the child form to match the panelMain size
            childForm.Location = new Point(panelMain.ClientSize.Width, 0); // Set the initial location of the child form to the right edge of the panelMain
            childForm.Opacity = 0;                                         // Set the initial opacity of the child form to 0 (invisible)
            childForm.Show();                                              // Show the child form

            // SlideFadeInForm utilization in the child form
            while (childForm.Left > 0 || childForm.Opacity < 1.0) // While the child form is not fully visible
            {
                // Slide the child form to the left and increase its opacity
                await Task.Delay(10);                                        // Wait for 10 milliseconds for smoother animation
                childForm.Left = Math.Max(childForm.Left - 40, 0);           // Move the child form left by 40 pixels, but not less than 0
                childForm.Opacity = Math.Min(childForm.Opacity + 0.05, 1.0); // Increase the opacity of the child form by 0.05, but not more than 1.0 (fully visible)
            }
            childForm.Dock = DockStyle.Fill; // Set the child form to fill the entire panelMain
            childForm.BringToFront();        // Bring the child form to the front of the panelMain controls
        }

        // RGB Color dictionary
        private struct RGBColors
        {
            public static Color color1 = Color.FromArgb(172, 126, 241); // Vibrant Purple
            public static Color color2 = Color.FromArgb(249, 118, 176); // Dark Crimson Pink
            public static Color color3 = Color.FromArgb(253, 138, 114); // Light Orange
            public static Color color4 = Color.FromArgb(95, 77, 221);   // Dark Purple
            public static Color color5 = Color.FromArgb(249, 88, 155);  // Light Pink
            public static Color color6 = Color.FromArgb(24, 161, 251);  // Light Blue
            public static Color color7 = Color.FromArgb(100, 149, 237); // Corn Flower Blue
            public static Color color8 = Color.FromArgb(60, 179, 113);  // Medium Sea Green
            public static Color color9 = Color.FromArgb(147, 112, 219); // Medium Purple
            public static Color color10 = Color.FromArgb(176, 196, 222);// Light Steel Blue
            public static Color color11 = Color.FromArgb(240, 255, 240);// Honeydew
            public static Color color12 = Color.FromArgb(230, 230, 250);// Lavender
        }

        // Method to slide and fade in the child forms (Bots, Hub, Logs) when it is opened
        private async void SlideFadeInForm(Form form)
        {
            form.Dock = DockStyle.None;                               // Remove any docking style from the form
            form.Size = panelMain.ClientSize;                         // Set the size of the form to match the panelMain size to make a seamless transition
            form.Location = new Point(panelMain.ClientSize.Width, 0); // Set the initial location of the form to the right edge of the panelMain
            form.Opacity = 0;                                         // Set the initial opacity of the form to 0 (invisible)
            form.Show();                                              // Show the form in all its glory

            // Slide the form to the left and increase its opacity
            while (form.Left > 0 || form.Opacity < 1.0) // While the form is not fully visible
            {
                await Task.Delay(10);                              // Wait for 10 milliseconds for smoother animation
                form.Left = Math.Max(form.Left - 40, 0);           // Move the form left by 40 pixels, but not less than 0
                form.Opacity = Math.Min(form.Opacity + 0.05, 1.0); // Increase the opacity of the form by 0.05, but not more than 1.0 (fully visible)
            }
            form.Dock = DockStyle.Fill; // Set the form to fill the entire panelMain like it should
            form.BringToFront();        // Bring the form to the front of panelMain
        }

        // Animation method for the Bots, Hub, and Logs buttons
        private void ApplyButtonEffects()
        {
            foreach (Control control in panelLeftSide.Controls)     // Go through each control in the left side panel
            {
                if (control is FontAwesome.Sharp.IconButton button) // Check if the control is an IconButton 
                {
                    button.MouseEnter += (s, e) => AnimateButtonHover(button, true);  // Start hover animation
                    button.MouseLeave += (s, e) => AnimateButtonHover(button, false); // Stop hover animation
                }
            }
        }

        // Animation method for Bots, Hub, and Logs button hover effect
        private async void AnimateButtonHover(FontAwesome.Sharp.IconButton button, bool hover)
        {
            float targetScale = hover ? 0.1f : 0.4f;                       // Target scale for hover effect
            float step = 0.01f * (hover ? 1 : -1);                         // Step size for scaling
            float currentScale = button.Tag is float scale ? scale : 1.0f; // Get current scale from Tag or default to 1.0f

            while ((hover && currentScale < targetScale) || (!hover && currentScale > targetScale)) // While scaling towards target
            {
                currentScale = Math.Clamp(currentScale + step, 1.0f, 1.1f);                         // Clamp the scale between 1.0 and 1.1
                button.Tag = currentScale;                                                          // Store the current scale in the button's Tag property
                button.Font = new Font(button.Font.FontFamily, 12F * currentScale, FontStyle.Bold); // Adjust font size based on scale
                await Task.Delay(10);                                                               // Delay for 10 milliseconds for smoother animation
            }
        }
        public void SetupThemeAwareButtons()
        {
            // Use the current theme colors
            var baseColor = ThemeManager.CurrentColors.PanelBase;
            var hoverColor = ThemeManager.CurrentColors.Hover;

            SetupHoverAnimation(btnBots, baseColor, hoverColor);
            SetupHoverAnimation(btnHub, baseColor, hoverColor);
            SetupHoverAnimation(btnLogs, baseColor, hoverColor);
        }

        // Method to set up hover animation for Bots, Hub, and Logs button
        private void SetupHoverAnimation(IconButton button, Color baseColor, Color hoverColor)
        {
            Timer fadeTimer = new Timer();                  // Create a new timer for the hover animation
            fadeTimer.Interval = 25;                        // Lower value = smoother effect
            float t = 1f;                                   // Current interpolation value (0 to 1)
            float speed = 0.05f;                            // Lower value = slower fade
            bool hovering = false;                          // Whether the mouse is hovering over the button

            // Start the fade timer when the button is hovered over or not
            fadeTimer.Tick += (s, e) =>
            {
                if (hovering && t < 1f)       // If hovering, increase the interpolation value
                    t += speed;
                else if (!hovering && t > 0f) // If not hovering, decrease the interpolation value
                    t -= speed;

                t = Math.Clamp(t, 0f, 1f); // Ensure t(interpolation) is between 0 and 1

                // Interpolate the button's background color based on the interpolation value
                int r = (int)(baseColor.R + (hoverColor.R - baseColor.R) * t);
                int g = (int)(baseColor.G + (hoverColor.G - baseColor.G) * t);
                int b = (int)(baseColor.B + (hoverColor.B - baseColor.B) * t);
                button.BackColor = Color.FromArgb(r, g, b);

                if ((hovering && t >= 1f) || (!hovering && t <= 0f)) // Wait for animation timer to complete
                    fadeTimer.Stop();                                // Stop the timer when complete
            };

            // Assign the hover state to the button
            button.MouseEnter += (s, e) => StartColorFade(button, ThemeManager.CurrentColors.PanelBase); // Hover color
            button.MouseLeave += (s, e) => StartColorFade(button, ThemeManager.CurrentColors.Hover);     // Default color
            button.UseVisualStyleBackColor = false;      // Set UseVisualStyleBackColor to false to allow custom colors
            button.BackColor = baseColor;                // Set the initial background color of the button
        }

        // Method to set up hover animation for the top buttons (Close, Minimize, Maximize)
        private void SetupTopButtonHoverAnimation(IconPictureBox button, Color hoverColor)
        {
            Color baseColor = button.BackColor;            // Default color for the buttons before hover
            float fadeSpeed = 0.01f;                       // Speed of the fade animation, lower value = slower fade
            Timer fadeTimer = new Timer { Interval = 10 }; // Timer for the fade animation, lower value = smoother effect
            float step = 1f;                               // Current step in the fade animation, higher values = slower fade
            bool hovering = false;                         // Whether the mouse is hovering over the button

            // Start the fade timer when the button is hovered over or not
            fadeTimer.Tick += (s, e) =>
            {
                if (hovering && step < 1f) // If hovering, increase the step value of 1 (fade in) according to timer
                    step += fadeSpeed;
                else if (!hovering && step > 0f) // If not hovering, decrease the step value of 0 (fade out) according to timer
                    step -= fadeSpeed;

                // Clamp the step value between 0 and 1
                step = Math.Clamp(step, 0f, 1f);                                      // Ensure step is between 0 and 1
                button.BackColor = LerpColor(baseColor, hoverColor, EaseInOut(step)); // Interpolate color based on step value

                if ((hovering && step >= 1f) || (!hovering && step <= 0f)) // Wait for animation timer to complete
                    fadeTimer.Stop();                                      // Stop the timer when complete
            };

            button.MouseEnter += (s, e) => { hovering = true; fadeTimer.Start(); };  // Start the fade timer when the mouse enters the button
            button.MouseLeave += (s, e) => { hovering = false; fadeTimer.Start(); }; // Stop the fade timer when the mouse leaves the button
        }

        // Method to start the color fade animation on the Bots, Hub, and Logs buttons
        private void StartColorFade(IconButton btn, Color endColor)
        {
            if (hoverTimers.ContainsKey(btn)) // If the button already has a hover timer, stop and dispose of it
            {
                hoverTimers[btn].Stop();    // Stop the existing timer
                hoverTimers[btn].Dispose(); // Dispose of the existing timer
            }

            Timer t = new Timer();            // Create a new timer for the hover animation
            Color startColor = ThemeManager.CurrentColors.PanelBase; // Current color of the button
            float fadeSpeed = 0.04f;          // 0.02 = 750ms fade, higher values = slower fade
            float step = 0.1f;                // Current step in the fade animation, higher values = slower fade
            t.Interval = 10;                  // Around 60fps, lower value = smoother effect

            // Set up the timer tick event for the hover animation
            t.Tick += (s, e) =>
            {
                step += fadeSpeed;
                if (step >= 1.0f) // Fade speed step reached 100%
                {
                    btn.BackColor = ThemeManager.CurrentColors.Hover; // Set the button's background color to the end color
                    t.Stop();                 // Stop the timer when the fade is complete
                    t.Dispose();              // Dispose of the timer to free resources
                    hoverTimers.Remove(btn);  // Remove the button from the hover timers dictionary
                    return;
                }

                btn.BackColor = LerpColor(startColor, endColor, EaseInOut(step)); // Interpolate color
            };

            hoverTimers[btn] = t; // Store the timer in the hover timers dictionary
            t.Start();            // Start the timer to begin the hover animation
        }

        // Linear interpolation for colors on Bots, Hub, and Logs button hover effect
        private float EaseInOut(float t) => t < 0.5 ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;

        // Lerp color method for the Bots, Hub, and Logs button hover effect
        private Color LerpColor(Color start, Color end, float t) // Linearly interpolate between two colors
        {
            t = Math.Clamp(t, 0f, 1f); // ensure 0 ≤ t ≤ 1 for good interpolation

            // Calculate the interpolated color components
            int r = (int)Math.Clamp(start.R + (end.R - start.R) * t, 0, 255);
            int g = (int)Math.Clamp(start.G + (end.G - start.G) * t, 0, 255);
            int b = (int)Math.Clamp(start.B + (end.B - start.B) * t, 0, 255);
            return Color.FromArgb(r, g, b);
        }

        // Method to activate the buttons and set the left border
        private void ActivateButton(object senderBtn, Color color)
        {
            if (senderBtn != null) // Check if the sender is not null
            {
                DisableButton();
                // Cast the sender to Bots, Hub, and Logs button
                currentBtn = (IconButton)senderBtn;
                currentBtn.BackColor = Color.FromArgb(37, 36, 81);                // Darker shade for active button
                currentBtn.ForeColor = color;                                     // Set the text color of the active button
                currentBtn.TextAlign = ContentAlignment.MiddleCenter;             // Center the text in the active button
                currentBtn.IconColor = color;                                     // Set the icon color of the active button
                currentBtn.TextImageRelation = TextImageRelation.TextBeforeImage; // Set the text and image relation of the active button
                currentBtn.ImageAlign = ContentAlignment.MiddleRight;             // Align the image to the right of the text in the active button

                // Set the left border button properties
                leftBorderBtn.BackColor = color;                              // Set the left border color
                leftBorderBtn.Location = new Point(0, currentBtn.Location.Y); // Set the left border position to the active button's position
                leftBorderBtn.Visible = true;                                 // Make the left border visible
                leftBorderBtn.BringToFront();                                 // Bring the left border to the front
                childFormIcon.IconChar = currentBtn.IconChar;                 // Set the icon of the current child form to the active button's icon
                childFormIcon.IconColor = color;                              // Set the icon color of the current child form to the active button's color
                lblTitleChildForm.Text = ((IconButton)senderBtn).Text;        // Set the title of the child form to the active button's text
            }
        }

        // Method to disable the current button and reset its style to default
        private void DisableButton()
        {
            if (currentBtn != null)
            {
                currentBtn.BackColor = ThemeManager.CurrentColors.PanelBase;      // Default background color
                currentBtn.ForeColor = Color.Gainsboro;                           // Default text color
                currentBtn.TextAlign = ContentAlignment.MiddleLeft;               // Center the text in the button
                currentBtn.IconColor = Color.Gainsboro;                           // Default icon color
                currentBtn.TextImageRelation = TextImageRelation.ImageBeforeText; // Set the text and image relation to default
                currentBtn.ImageAlign = ContentAlignment.MiddleLeft;              // Align the image to the left of the text in the button
            }
        }

        // Config save method
        public void SaveCurrentConfig()
        {
            try
            {
                string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions // Serialize the current config to json
                {
                    WriteIndented = true                     // Format the json with indentation for readability
                });
                File.WriteAllText(Program.ConfigPath, json); // Save the serialized json to the config file
            }
            catch (Exception ex)
            {
                WinFormsUtil.Error($"Failed to save configuration:\n{ex.Message}");
            }
        }


    }
}
