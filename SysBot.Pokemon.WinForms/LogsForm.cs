using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;

namespace SysBot.Pokemon.WinForms
{
    public partial class LogsForm : Form
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public RichTextBox LogsBox { get; private set; }

        private TextBox searchBox;
        private Button enterButton;
        private Button nextButton;
        private Button prevButton;
        private Label resultLabel;
        private Label placeholderLabel;

        private List<int> matchIndices = new();
        private int currentMatchIndex = -1;

        public LogsForm()
        {
            InitializeComponent();

            // Use FontManager for custom fonts with fallback
            Font logsFont;
            try
            {
                logsFont = FontManager.Get("Ubuntu Mono", 10);
            }
            catch
            {
                logsFont = new Font(FontFamily.GenericMonospace, 10);
            }

            LogsBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = logsFont,
                BackColor = Color.FromArgb(10, 10, 40),
                ForeColor = Color.FromArgb(51, 255, 255),
                WordWrap = true,
                ScrollBars = RichTextBoxScrollBars.Both,
                ContextMenuStrip = CreateContextMenu()
            };

            // Use the same font for placeholder with fallback
            Font placeholderFont;
            try
            {
                placeholderFont = FontManager.Get("Ubuntu Mono", 10, FontStyle.Italic);
            }
            catch
            {
                placeholderFont = new Font(FontFamily.GenericMonospace, 10, FontStyle.Italic);
            }

            placeholderLabel = new Label
            {
                Text = "Nothing currently logged...",
                ForeColor = Color.Cyan,
                BackColor = Color.Transparent,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Dock = DockStyle.Fill,
                Font = placeholderFont
            };

            var logsPanel = new Panel { Dock = DockStyle.Fill };
            logsPanel.Controls.Add(placeholderLabel);
            logsPanel.Controls.Add(LogsBox);

            var topPanel = CreateSearchPanel();

            // Add the panels in correct order
            Controls.Add(logsPanel);   // ✅ instead of LogsBox directly
            Controls.Add(topPanel);

            LogsBox.TextChanged += LogsBox_TextChanged;
        }

        private void LogsBox_TextChanged(object? sender, EventArgs e)
        {
            placeholderLabel.Visible = string.IsNullOrEmpty(LogsBox.Text);
        }

        private Panel CreateSearchPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = Color.FromArgb(20, 20, 50) // top panel background
            };

            // Use FontManager for Montserrat with fallback
            Font searchFont;
            try
            {
                searchFont = FontManager.Get("Montserrat", 9, FontStyle.Italic);
            }
            catch
            {
                searchFont = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Italic);
            }

            // TextBox inside the border panel
            searchBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(40, 39, 77),
                ForeColor = Color.Gray,
                Font = searchFont,
                Text = "Search...",
                Location = new Point(6, 5),
                Size = new Size(198, 28),
            };

            // Regular font for active search
            Font searchFontRegular;
            try
            {
                searchFontRegular = FontManager.Get("Montserrat", 9, FontStyle.Regular);
            }
            catch
            {
                searchFontRegular = new Font(FontFamily.GenericSansSerif, 9, FontStyle.Regular);
            }

            searchBox.Enter += (s, e) =>
            {
                if (searchBox.Text == "Search...")
                {
                    searchBox.Text = "";
                    searchBox.ForeColor = Color.White;
                    searchBox.Font = searchFontRegular;
                }
            };

            searchBox.Leave += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(searchBox.Text))
                {
                    searchBox.Text = "Search...";
                    searchBox.ForeColor = Color.Gray;
                    searchBox.Font = searchFont;
                }
            };

            searchBox.TextChanged += SearchBox_TextChanged;

            // Button font with fallback
            Font buttonFont;
            try
            {
                buttonFont = FontManager.Get("Montserrat", 8);
            }
            catch
            {
                buttonFont = new Font(FontFamily.GenericSansSerif, 8);
            }

            nextButton = new FancyButton
            {
                Text = "NEXT",
                Location = new Point(210, 2 - 1), // was 210
                Height = 25,
                Font = buttonFont
            };
            nextButton.Click += (s, e) => PerformSearch(SearchDirection.Forward);

            prevButton = new FancyButton
            {
                Text = "PREV",
                Location = new Point(290, 2 - 1), // was 290
                Height = 25,
                Font = buttonFont
            };
            prevButton.Click += (s, e) => PerformSearch(SearchDirection.Backward);

            var clearButton = new FancyButton
            {
                Text = "CLEAR",
                Location = new Point(370, 2 - 1), // was 370
                Height = 25,
                Font = buttonFont
            };
            clearButton.Click += (s, e) =>
            {
                searchBox.Text = string.Empty;
                ClearHighlights();
                matchIndices.Clear();
                currentMatchIndex = -1;
                resultLabel.Text = string.Empty;
            };

            resultLabel = new Label
            {
                AutoSize = true,
                Location = new Point(470, 6 - 2), // was 550
                ForeColor = Color.White,
                Font = buttonFont
            };

            panel.Controls.Add(searchBox);
            panel.Controls.Add(enterButton);
            panel.Controls.Add(nextButton);
            panel.Controls.Add(prevButton);
            panel.Controls.Add(resultLabel);
            panel.Controls.Add(clearButton);

            return panel;
        }

        private void SearchBox_TextChanged(object? sender, EventArgs e)
        {
            // Don’t trigger when it’s the placeholder text  
            if (searchBox.Text == "Search logs...")
                return;

            DoSearch(searchBox.Text);
        }

        private void DoSearch(string term)
        {
            ClearHighlights();
            matchIndices.Clear();
            currentMatchIndex = -1;

            if (string.IsNullOrWhiteSpace(term))
            {
                resultLabel.Text = "No search term.";
                return;
            }

            var text = LogsBox.Text;
            int start = 0;
            while ((start = text.IndexOf(term, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                matchIndices.Add(start);
                start += term.Length;
            }

            if (matchIndices.Count == 0)
            {
                resultLabel.Text = "No matches.";
                return;
            }

            foreach (int index in matchIndices)
            {
                LogsBox.Select(index, term.Length);
                LogsBox.SelectionBackColor = Color.DarkOrange;
            }

            MoveToMatch(0);
        }

        private void MoveToMatch(int direction)
        {
            if (matchIndices.Count == 0)
                return;

            if (direction == 0)
                currentMatchIndex = 0;
            else
                currentMatchIndex = (currentMatchIndex + direction + matchIndices.Count) % matchIndices.Count;

            int matchPos = matchIndices[currentMatchIndex];
            LogsBox.Select(matchPos, searchBox.Text.Length);
            LogsBox.ScrollToCaret();

            if (!searchBox.Focused)
                LogsBox.Focus();


            resultLabel.Text = $"Match {currentMatchIndex + 1} of {matchIndices.Count}";
        }

        private void ClearHighlights()
        {
            var originalColor = Color.FromArgb(10, 10, 40);

            LogsBox.SelectAll();
            LogsBox.SelectionBackColor = originalColor;
            LogsBox.DeselectAll();
        }

        private void SearchBox_Enter(object sender, EventArgs e)
        {
            if (searchBox.Text == "Search logs...")
            {
                searchBox.Text = string.Empty;
                searchBox.ForeColor = Color.White;
                // Use existing font or fallback
                try
                {
                    searchBox.Font = FontManager.Get("Montserrat", 8, FontStyle.Regular);
                }
                catch
                {
                    searchBox.Font = new Font(FontFamily.GenericSansSerif, 8, FontStyle.Regular);
                }
            }
        }

        private void SearchBox_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(searchBox.Text))
            {
                searchBox.Text = "Search logs...";
                searchBox.ForeColor = Color.Gray;
                // Use existing font or fallback
                try
                {
                    searchBox.Font = FontManager.Get("Montserrat", 8, FontStyle.Italic);
                }
                catch
                {
                    searchBox.Font = new Font(FontFamily.GenericSansSerif, 8, FontStyle.Italic);
                }
            }
        }
        private enum SearchDirection
        {
            Current = 0,
            Forward = 1,
            Backward = -1
        }
        private void PerformSearch(SearchDirection direction)
        {
            string term = searchBox.Text;

            if (string.IsNullOrWhiteSpace(term) || term == "Search logs...")
            {
                resultLabel.Text = "Enter a search term.";
                return;
            }

            // If it's the initial "Current" search, perform the scan
            if (direction == SearchDirection.Current)
            {
                DoSearch(term);
                return;
            }

            if (matchIndices.Count == 0)
            {
                resultLabel.Text = "No matches.";
                return;
            }

            // Forward or Backward movement
            int move = (int)direction;
            MoveToMatch(move);
        }

        private ContextMenuStrip CreateContextMenu()
        {
            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add(new ToolStripMenuItem("Copy", null, (sender, e) => LogsBox.Copy()));
            contextMenu.Items.Add(new ToolStripMenuItem("Clear", null, (sender, e) => LogsBox.Clear()));
            contextMenu.Items.Add(new ToolStripMenuItem("Select All", null, (sender, e) =>
            {
                LogsBox.SelectAll();
            }));

            return contextMenu;
        }
    }
}
