using ControllerCommand = SysBot.Pokemon.WinForms.BotController.BotControlCommand;
using FontAwesome.Sharp;
using PKHeX.Core;
using SysBot.Base;
using SysBot.Pokemon.WinForms;
using SysBot.Pokemon.Discord;
using SysBot.Pokemon.Discord.Helpers;
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
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Drawing.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using static SysBot.Pokemon.WinForms.BotController;
using WebApiCommand = SysBot.Pokemon.WinForms.WebApi.BotControlCommand;
using System.Net.Http;

namespace SysBot.Pokemon.WinForms
{
    public sealed partial class Main : Form
    {
        // Currently active child form
        private Form? activeForm = null;

        // Current running environment
        private IPokeBotRunner RunningEnvironment { get; set; } = null!;

        // Program configuration
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // Do not serialize in the designer
        public static ProgramConfig Config { get; set; } = new();

        // Static properties for update state
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)] // Do not serialize in the designer
        public static bool IsUpdating { get; set; } = false;

        // Singleton instance of Main form
        [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public static Main? Instance { get; private set; }

        // Update available flag
        internal bool hasUpdate = false;

        // Currently active button in the left panel, for setting Bots as default
        private IconButton currentBtn = null!;

        private Panel leftBorderBtn = null!;
        private Dictionary<IconButton, Timer> hoverTimers = new();

#pragma warning disable CS0414 // Field is assigned but never used
        private bool _isFormLoading = true;               // Flag to indicate if the form is still loading (reserved for future use)
#pragma warning restore CS0414                            // Flag to indicate if the form is still loading
        private readonly List<PokeBotState> Bots = new(); // List of bots created in the program
        private BotsForm _botsForm = null!;                       // BotsForm instance to manage bot controls
        private LogsForm _logsForm = null!;                       // LogsForm instance to display logs
        private HubForm _hubForm = null!;                       // HubForm instance to manage hub settings

        public Panel PanelLeftSide => panelLeftSide;      // Expose panelLeftSide for other forms

        // UI EFFECTS VARIABLES
        private Dictionary<IconButton, Timer> pulseTimers = new();
        private readonly Dictionary<Control, Timer> shakeTimers = new();
        private readonly Dictionary<Control, int> shakeFrames = new();
        private readonly Dictionary<Control, Point> originalLocations = new();
        private readonly Random rng = new();
        private readonly List<Sparkle> sparkles = new();
        private readonly Random glitterRng = new Random();
        private Timer glitterTimer = null!;

        ////////////////////////////////////////////////////////////
        // Initialize custom fonts for UI controls with fallbacks //
        ////////////////////////////////////////////////////////////
        private void InitializeFonts()
        {
            try
            {
                // Apply fonts to navigation buttons
                foreach (var btn in new[] { btnBots, btnHub, btnLogs })
                {
                    try
                    {
                        btn.Font = FontManager.Get("Enter The Grid", 13.8F);
                    }
                    catch
                    {
                        btn.Font = new Font(FontFamily.GenericSansSerif, 13.8F);
                    }
                }

                // Apply font to title
                try
                {
                    lblTitle.Font = FontManager.Get("Bahnschrift", 7.2F);
                }
                catch
                {
                    lblTitle.Font = new Font(FontFamily.GenericSansSerif, 7.2F);
                }

                // Apply font to child form title
                try
                {
                    lblTitleChildForm.Font = FontManager.Get("Gnuolane Rg", 25.8F);
                }
                catch
                {
                    lblTitleChildForm.Font = new Font(FontFamily.GenericSansSerif, 25.8F);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Font Initialization] Warning: {ex.Message}");
            }
        }

        ////////////////////////////////////////////////////////////
        /////// MAIN FORM CONSTRUCTOR INITIALIZING FOR /////////////
        /////// UI EFFECTS, EXCEPTION HANDLERS, BOT HANDLING ///////
        /////// FORMS, WEBSERVER, FONTS, THEMES, UPDATES ///////////
        ////////////////////////////////////////////////////////////

        public Main()
        {
            // GLOBAL EXCEPTION HANDLERS — LOG BEFORE BOT DIES
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                var agg = e.Exception; // always AggregateException
                var ex = agg.InnerException ?? agg; // unwrap if possible

                // Ignore normal cancellations
                if (ex is OperationCanceledException)
                {
                    e.SetObserved();
                    return;
                }

                // Unwrap AGAIN if it's multiple nested levels (task > agg > inner)
                if (ex is AggregateException agg2 && agg2.InnerException != null)
                    ex = agg2.InnerException;

                // Ignore OperationAborted socket errors
                if (ex is SocketException se && se.SocketErrorCode == SocketError.OperationAborted)
                {
                    e.SetObserved();
                    return;
                }

                // LOG every real problem
                LogUtil.LogSafe(ex, "Unobserved Task Exception");
                e.SetObserved();
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    LogUtil.LogSafe(ex, "Unhandled Exception");
                else
                    LogUtil.LogGeneric($"Non-Exception - Unhandled Exception: {e.ExceptionObject}", "Unhandled Exception");
            };


            Task.Run(BotMonitor);      // Start the bot monitor

            try
            {
                InitializeComponent();     // Initialize all the form components before program
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Font Awesome") || ex.Message.Contains("font"))
            {
                // FontAwesome.Sharp library failed to load its embedded fonts
                // This is a critical error, but we'll log it and let Program.cs handle it
                Console.WriteLine($"[CRITICAL] FontAwesome.Sharp initialization failed: {ex.Message}");
                Console.WriteLine($"[CRITICAL] Stack trace: {ex.StackTrace}");
                throw; // Re-throw to let Program.cs catch and display user-friendly message
            }

            InitializeFonts();         // Apply custom fonts after component initialization
            SetupTitleBarButtonHoverEffects();
            panelTitleBar.Paint += panelTitleBar_Paint;
            InitGlitter();

            Instance = this;
            InitializeLeftSideImage(); // Initialize the left side BG image in panelLeftSide
            InitializeUpperImage();    // Initialize the upper image in panelTitleBar

            // Force load FontAwesome font
            btnBots.IconChar = IconChar.Robot;
            btnHub.IconChar = IconChar.TableList;
            btnLogs.IconChar = IconChar.ListDots;
            btnMinimize.IconChar = IconChar.WindowMinimize;
            btnClose.IconChar = IconChar.Close;
            btnMaximize.IconChar = IconChar.WindowMaximize;

            // Wait for the form crap to load before initializing
            this.Load += async (s, e) => await InitializeAsync();

            // Set up left‑panel buttons & effects
            var baseColor = ThemeManager.CurrentColors.PanelBase; // Base color for buttons according to themes
            var hoverColor = ThemeManager.CurrentColors.Hover;    // Hover color for buttons according to themes
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
        }

        // Runs once when Main form first loads
        private async Task InitializeAsync()
        {
            if (IsUpdating)
                return;

            PokeTradeBotSWSH.SeedChecker = new Z3SeedSearchHandler<PK8>(); // Initialize the seed checker for SWSH mode

            _botsForm = new BotsForm(); // Initialize the BotsForm instance

            try
            {
                var (updateAvailable, _, newVersion) = await UpdateChecker.CheckForUpdatesAsync(); // Check for updates
                hasUpdate = updateAvailable; // If there's an update, this flag gets checked
                _botsForm.SetUpdateNotification(updateAvailable, newVersion); // Show update notification in BotsForm
            }
            catch { }
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

                // Set the current game mode for BatchCommandNormalizer
                BatchCommandNormalizer.CurrentGameMode = Config.Mode;

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

                // Set the current game mode for BatchCommandNormalizer
                BatchCommandNormalizer.CurrentGameMode = Config.Mode;

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
            OpenChildForm(_botsForm);
            SetupThemeAwareButtons();

            // Activate the Bots button after everything is initialized
            ActivateButton(btnBots);

            SaveCurrentConfig();

            _botsForm.StartButton.Click += B_Start_Click;           // Start button... do any of these really need explaining?
            _botsForm.StopButton.Click += B_Stop_Click;             // Stop button
            _botsForm.RebootStopButton.Click += B_RebootStop_Click; // Reboot and Stop button
            _botsForm.UpdateButton.Click += Updater_Click;          // Update button
            _botsForm.AddBotButton.Click += B_New_Click;            // Add button
            _botsForm.PKHeXButton.Click += B_PKHeX_Click;           // PKHeX button
            _botsForm.SwitchRemoteButton.Click += B_SwitchRemote_Click; // Switch Remote button
            _botsForm.SysDVRButton.Click += B_SysDVR_Click;         // SysDVR button

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


        ///////////////////////////////////////////////////
        ///////// SET CURRENT RUNNING ENVIRONMENT /////////
        ///////////////////////////////////////////////////

        private static IPokeBotRunner GetRunner(ProgramConfig cfg) => cfg.Mode switch
        {
            ProgramMode.SWSH => new PokeBotRunnerImpl<PK8>(cfg.Hub, new BotFactory8SWSH()),
            ProgramMode.BDSP => new PokeBotRunnerImpl<PB8>(cfg.Hub, new BotFactory8BS()),
            ProgramMode.LA => new PokeBotRunnerImpl<PA8>(cfg.Hub, new BotFactory8LA()),
            ProgramMode.SV => new PokeBotRunnerImpl<PK9>(cfg.Hub, new BotFactory9SV()),
            ProgramMode.PLZA => new PokeBotRunnerImpl<PA9>(cfg.Hub, new BotFactory9PLZA()),
            ProgramMode.LGPE => new PokeBotRunnerImpl<PB7>(cfg.Hub, new BotFactory7LGPE()),
            _ => throw new IndexOutOfRangeException("Unsupported mode."), // A LIE
        };


        //////////////////////////////////////////////////////////////////
        /////// BOT CONTROL AND COMMAND LOGIC FOR UI AND WEBSERVER ///////
        //////////////////////////////////////////////////////////////////

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
                        string portInfoPath = Path.Combine(dirPath, $"ZE_FusionBot_{Environment.ProcessId}.port");
                        if (File.Exists(portInfoPath))
                            File.Delete(portInfoPath);
                    }
                }
            }
            catch { }
        }

        // Start the bot with the current config
        private void B_Start_Click(object? sender, EventArgs e) // Start all bots on Start button click
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
        private void B_RebootStop_Click(object? sender, EventArgs e) // Restart all bots and reboot the game on console
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
        private void B_Stop_Click(object? sender, EventArgs e)     // Stop all bots on Stop button click
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
        private void B_New_Click(object? sender, EventArgs e) // Add a new bot on Add button click
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

        // Launch PKHeX when the PKHeX button is clicked
        private void B_PKHeX_Click(object? sender, EventArgs e)
        {
            PKHeXLauncher.Launch(Config.Hub.Folder.PKHeXDirectory);
        }

        // Launch Switch Remote for PC when the button is clicked
        private void B_SwitchRemote_Click(object? sender, EventArgs e)
        {
            SwitchRemoteLauncher.Launch(Config.Hub.Folder.SwitchRemoteForPC);
        }

        // Launch SysDVR when the button is clicked
        private void B_SysDVR_Click(object? sender, EventArgs e)
        {
            SysDVRLauncher.Launch();
        }

        // Update handling
        private async void Updater_Click(object? sender, EventArgs e)
        {
            await UpdateChecker.CheckForUpdatesAsync(forceShow: true); // Will auto-handle the UpdateForm without all the other crap
        }

        // Add a new bot to the environment and UI
        private bool AddBot(PokeBotState? cfg)
        {
            if (cfg == null || !cfg.IsValid()) // Ensure cfg is not null before calling IsValid()
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


        ///////////////////////////////////////////////////
        ////// THEME MANAGEMENT FOR MAIN UI ELEMENTS //////
        ///////////////////////////////////////////////////


        // Update the method signature to explicitly allow nullability for the 'sender' parameter.
        private void CB_Themes_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (sender is not ComboBox comboBox)
                return;

            if (comboBox.SelectedItem is not string selected)
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


        ///////////////////////////////////////////////////////////////////
        /////// BOT HANDLING FOR INITIATING A NEW BOT IN THE FORMS ////////
        /////// ALSO HOLDS RANDOM CALL TO STOP THE WEBSERVER AFTER ////////
        ///////////////////////////////////////////////////////////////////
        private PokeBotState? CreateNewBotConfig() // Create a new bot configuration based on the current settings in the BotsForm
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
            this.StopWebServer(); // Stop the web server to free up resources before adding a new bot

            // Create a new SwitchConnectionConfig based on the IP and port
            var cfg = BotConfigUtil.GetConfig<SwitchConnectionConfig>(ip, port); // Get the connection config based on the IP and port
            cfg.Protocol = (SwitchProtocol)_botsForm.ProtocolBox.SelectedValue;  // Set the protocol from the ProtocolBox
            var pk = new PokeBotState { Connection = cfg };                      // Create a new PokeBotState with the connection config
            var type = (PokeRoutineType)_botsForm.RoutineBox.SelectedValue;      // Set the routine type from the RoutineBox
            pk.Initialize(type);                                                 // Initialize the PokeBotState with the selected routine type
            return pk;                                                           // Return the new PokeBotState configuration
        }


        ///////////////////////////////////////////////////
        ////////// IMAGES FOR THE MAIN UI FORMS ///////////
        ///////////////////////////////////////////////////

        // Initialize the method for the left side image in the panelLeftSide
        private PictureBox leftSideImage = null!;

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
        private PictureBox upperPanelImage = null!;

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
                    using var httpClient = new HttpClient();
                    using var stream = httpClient.GetStreamAsync(logoPath).Result;
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


        ///////////////////////////////////////////////////
        //////////// PROGRESS BAR UPDATE LOGIC ////////////
        ///////////////////////////////////////////////////
       
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
                case PokeTradeBotPLZA zaBot:
                    zaBot.TradeProgressChanged += SetProgress;
                    break;
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


        ///////////////////////////////////////////////////
        //// FLP BOT AND PROTOCOL ADDITIONAL HANDLING /////
        ///////////////////////////////////////////////////

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


        ///////////////////////////////////////////////////
        /////// MOVE UI VIA MOUSE ON PANELTITLEBAR ////////
        ///////////////////////////////////////////////////

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

        [DllImport("user32.DLL", EntryPoint = "ReleaseCapture")] // Release the mouse capture
        private extern static void ReleaseCapture();             // Release the mouse capture to allow dragging the window
        [DllImport("user32.DLL", EntryPoint = "SendMessage")]    // Send a message to the window
        private extern static void SendMessage(System.IntPtr hWnd, int wMsg, int wParam, int lParam); // Send a message to the window to allow dragging
        // Update the method signature to explicitly allow nullability for the 'sender' parameter.
        private void panelTitleBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (sender == btnClose || sender == btnMaximize || sender == btnMinimize)
                return;
            ReleaseCapture();                           // Release the mouse capture
            SendMessage(this.Handle, 0x112, 0xf012, 0); // Send a message to the window to allow dragging
        }


        ///////////////////////////////////////////////////
        ////////// BOTS/HUB/LOGS BUTTON HANDLING //////////
        ////////// CLOSE/MAX/MIN BUTTON HANDLING //////////
        ///////////////////////////////////////////////////

        // Method to activate Bots button and load BotsForm
        private void Bots_Click(object sender, EventArgs e)
        {
            if (sender is IconButton btn)
            {
                ActivateButton(btn);
                OpenChildForm(_botsForm);
            }
        }

        // Method to activate Hub button and load HubForm
        private void Hub_Click(object sender, EventArgs e)
        {
            if (sender is IconButton btn)
            {
                ActivateButton(btn);
                // Ensure HubForm exists
                if (_hubForm == null || _hubForm.IsDisposed)
                    _hubForm = new HubForm(Config.Hub);

                OpenChildForm(_hubForm);
            }
        }

        // Method to activate Logs button and load LogsForm
        private void Logs_Click(object sender, EventArgs e)
        {
            if (sender is IconButton btn)
            {
                ActivateButton(btn);
                OpenChildForm(_logsForm);
            }
        }

        // Close button
        private void BtnClose_Click(object? sender, EventArgs e)
        {
            Application.Exit(); // Exit program on Close button click
        }

        // Maximize and Restore button
        private void BtnMaximize_Click(object? sender, EventArgs e)
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
            activeForm?.Hide(); // Hide the currently active form, if any
            activeForm = childForm; // Set the new active form

            // If the form is not already in the panel, add it
            if (!panelMain.Controls.Contains(childForm))
            {
                childForm.TopLevel = false; // Set the form to be a non-top-level form
                childForm.FormBorderStyle = FormBorderStyle.None; // Remove the border style
                panelMain.Controls.Add(childForm); // Add the form to the panelMain controls
            }

            childForm.Dock = DockStyle.None; // needed for slide
            childForm.Size = panelMain.ClientSize; // set size to panel size
            childForm.Left = panelMain.ClientSize.Width; // reset slide start
            childForm.Opacity = 0; // reset fade start
            childForm.Show(); // Show the form
            childForm.BringToFront(); // Bring the form to the front of panelMain

            // Slide/fade animation
            while (childForm.Left > 0 || childForm.Opacity < 1)
            {
                await Task.Delay(10);
                childForm.Left = Math.Max(childForm.Left - 40, 0);
                childForm.Opacity = Math.Min(childForm.Opacity + 0.05, 1);
            }

            childForm.Dock = DockStyle.Fill;
        }


        ///////////////////////////////////////////////////
        ///////// GUI THEMING AND BUTTON HANDLING /////////
        ///////////////////////////////////////////////////

        public void SetupThemeAwareButtons()
        {
            // Use the current theme colors
            var colors = ThemeManager.CurrentColors;

            foreach (var btn in new[] { btnBots, btnHub, btnLogs })
            {
                btn.BackColor = colors.PanelBase;
                btn.ForeColor = colors.ForeColor;
                btn.UseVisualStyleBackColor = false;
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderSize = 0;

                // Hover animations
                btn.MouseEnter += (s, e) => StartHoverFade(btn, colors.Shadow, 150);
                btn.MouseLeave += (s, e) => StartHoverFade(btn, colors.PanelBase);

                // Click effects
                btn.Click += (s, e) =>
                {
                    ActivateButton(btn);
                    FlashClick(btn);
                };
            }
        }

        // Method to activate the buttons and set the left border
        private void ActivateButton(IconButton btn)
        {
            if (currentBtn == btn) return; // already active

            // Reset previous
            DisableButton();

            currentBtn = btn;

            // Outline colors
            Color outlineColor = btn switch
            {
                var b when b == btnBots => Color.FromArgb(180, 150, 255),
                var b when b == btnHub => Color.HotPink,
                var b when b == btnLogs => Color.Cyan,
                _ => Color.White
            };

            btn.FlatAppearance.BorderSize = 1; // thinner outline
            btn.FlatAppearance.BorderColor = outlineColor;

            // START the glow pulse on the active button
            StartOutlinePulse(btn, outlineColor);

            // Update top panel
            lblTitleChildForm.Text = btn.Text;
            childFormIcon.IconChar = btn.IconChar;
            childFormIcon.IconColor = outlineColor;
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

        // Smooth hover fade
        private void StartHoverFade(IconButton btn, Color targetColor, int durationMs = 200)
        {
            if (hoverTimers.ContainsKey(btn))
            {
                hoverTimers[btn].Stop();
                hoverTimers[btn].Dispose();
                hoverTimers.Remove(btn);
            }

            Color startColor = btn.BackColor;
            int steps = durationMs / 10; // ~60 FPS
            int currentStep = 0;

            Timer timer = new Timer { Interval = 26 };
            timer.Tick += (s, e) =>
            {
                currentStep++;
                float t = EaseInOut((float)currentStep / steps);
                btn.BackColor = LerpColor(startColor, targetColor, t);

                if (currentStep >= steps)
                {
                    timer.Stop();
                    timer.Dispose();
                    hoverTimers.Remove(btn);
                }
            };

            hoverTimers[btn] = timer;
            timer.Start();
        }

        private void StartOutlinePulse(IconButton btn, Color baseColor)
        {
            // stop any existing pulse first
            if (pulseTimers.TryGetValue(btn, out var oldTimer))
            {
                oldTimer.Stop();
                oldTimer.Dispose();
                pulseTimers.Remove(btn);
            }

            float t = 0f;
            bool forward = true;

            Timer pulseTimer = new Timer { Interval = 16 }; // ~60 FPS
            pulseTimer.Tick += (s, e) =>
            {
                // update t
                t += forward ? 0.03f : -0.03f;
                if (t >= 1f) { t = 1f; forward = false; }
                if (t <= 0f) { t = 0f; forward = true; }

                // calculate new color, clamp to 0-255
                int Clamp(int val) => Math.Max(0, Math.Min(255, val));
                float intensity = 0.6f + 0.4f * t; // base 60% -> 100%
                btn.FlatAppearance.BorderColor = Color.FromArgb(
                    Clamp((int)(baseColor.R * intensity)),
                    Clamp((int)(baseColor.G * intensity)),
                    Clamp((int)(baseColor.B * intensity))
                 );
            };

            pulseTimer.Start();
            pulseTimers[btn] = pulseTimer;
        }

        // Call this when disabling a button
        private void StopOutlinePulse(IconButton btn)
        {
            if (pulseTimers.TryGetValue(btn, out var timer))
            {
                timer.Stop();
                timer.Dispose();
                pulseTimers.Remove(btn);
            }
            // reset outline color
            btn.FlatAppearance.BorderColor = ThemeManager.CurrentColors.PanelBase;
        }

        // Click flash animation
        private async void FlashClick(IconButton btn)
        {
            Color flashColor = ThemeManager.CurrentColors.Shadow;
            Color original = btn.BackColor;

            btn.BackColor = flashColor;
            await Task.Delay(100); // quick flash
            btn.BackColor = original;
        }

        // Smooth color interpolation
        private Color LerpColor(Color start, Color end, float t)
        {
            int r = (int)(start.R + (end.R - start.R) * t);
            int g = (int)(start.G + (end.G - start.G) * t);
            int b = (int)(start.B + (end.B - start.B) * t);
            return Color.FromArgb(r, g, b);
        }

        // Easing function for smooth transitions
        private float EaseInOut(float t) => t < 0.5f ? 4 * t * t * t : 1 - MathF.Pow(-2 * t + 2, 3) / 2;

        private void SetupTitleBarButtonHoverEffects()
        {
            // Colors
            Color normalClose = Color.Red;
            Color hoverClose = Color.OrangeRed;

            Color normalMaximize = Color.White;
            Color hoverMaximize = Color.DarkGray;

            Color normalMinimize = Color.White;
            Color hoverMinimize = Color.DarkGray;

            // Close button
            btnClose.MouseEnter += (s, e) => btnClose.IconColor = hoverClose;
            btnClose.MouseLeave += (s, e) => btnClose.IconColor = normalClose;

            // Maximize button
            btnMaximize.MouseEnter += (s, e) => btnMaximize.IconColor = hoverMaximize;
            btnMaximize.MouseLeave += (s, e) => btnMaximize.IconColor = normalMaximize;

            // Minimize button
            btnMinimize.MouseEnter += (s, e) => btnMinimize.IconColor = hoverMinimize;
            btnMinimize.MouseLeave += (s, e) => btnMinimize.IconColor = normalMinimize;
        }

        private void InitGlitter()
        {
            glitterTimer = new Timer { Interval = 33 }; // ~30 FPS
            glitterTimer.Tick += (s, e) =>
            {
                // Randomly spawn new sparkles
                if (glitterRng.NextDouble() < 0.2) // 20% chance each tick
                {
                    PointF pos = new PointF(
                        glitterRng.Next(panelTitleBar.Width),
                        glitterRng.Next(panelTitleBar.Height)
                    );
                    sparkles.Add(new Sparkle(pos));
                }

                // Update existing sparkles
                for (int i = sparkles.Count - 1; i >= 0; i--)
                {
                    if (sparkles[i].Tick())
                        sparkles.RemoveAt(i);
                }

                // Redraw panel
                panelTitleBar.Invalidate();
            };

            glitterTimer.Start();

            // Paint handler
            panelTitleBar.Paint += (s, e) =>
            {
                foreach (var sp in sparkles)
                    sp.Draw(e.Graphics);
            };
        }

        // Method to disable the current button and reset its style to default
        private void DisableButton()
        {
            if (currentBtn != null)
            {
                StopOutlinePulse(currentBtn); // STOP pulse animation
                currentBtn.BackColor = ThemeManager.CurrentColors.PanelBase; // default bg
                currentBtn.TextAlign = ContentAlignment.MiddleLeft;
                currentBtn.TextImageRelation = TextImageRelation.ImageBeforeText;
                currentBtn.ImageAlign = ContentAlignment.MiddleLeft;
                currentBtn.FlatAppearance.BorderSize = 0; // remove outline
            }
        }


        ///////////////////////////////////////////////////
        //////////////// SAVING TO CONFIG /////////////////
        ///////////////////////////////////////////////////
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

        // WINFORMS JUNK THAT'S NEEDED
        private void panel6_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}
