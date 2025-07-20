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

        private List<int> matchIndices = new();
        private int currentMatchIndex = -1;

        public LogsForm()
        {
            InitializeComponent();

            LogsBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font("Ubuntu Mono", 10),
                BackColor = Color.FromArgb(10, 10, 40),
                ForeColor = Color.FromArgb(51, 255, 255),
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                ContextMenuStrip = CreateContextMenu()
            };

            var topPanel = CreateSearchPanel();

            Controls.Add(LogsBox);
            Controls.Add(topPanel);
        }

        private Panel CreateSearchPanel()
        {
            var panel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = Color.FromArgb(20, 20, 50)
            };

            searchBox = new TextBox
            {
                Width = 200,
                Location = new Point(5, 3),
                Font = new Font("Montserrat-Regular", 8, FontStyle.Italic),
                ForeColor = Color.Gray,
                Text = "Search logs..."
            };
            searchBox.Enter += SearchBox_Enter;
            searchBox.Leave += SearchBox_Leave;
            searchBox.KeyDown += SearchBox_KeyDown;

            enterButton = new FancyButton
            {
                Text = "Enter",
                Location = new Point(210, 2),
                Height = 25,
                Font = new Font("Montserrat-Regular", 8)
            };
            enterButton.Click += (s, e) => PerformSearch(SearchDirection.Current);

            nextButton = new FancyButton
            {
                Text = "Next",
                Location = new Point(290, 2), // was 210
                Height = 25,
                Font = new Font("Montserrat-Regular", 8)
            };
            nextButton.Click += (s, e) => PerformSearch(SearchDirection.Forward);

            prevButton = new FancyButton
            {
                Text = "Prev",
                Location = new Point(370, 2), // was 290
                Height = 25,
                Font = new Font("Montserrat-Regular", 8)
            };
            prevButton.Click += (s, e) => PerformSearch(SearchDirection.Backward);

            var clearButton = new FancyButton
            {
                Text = "Clear",
                Location = new Point(450, 2),
                Height = 25,
                Font = new Font("Montserrat-Regular", 8)
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
                Location = new Point(550, 6),
                ForeColor = Color.White,
                Font = new Font("Montserrat-Regular", 8)
            };

            panel.Controls.Add(searchBox);
            panel.Controls.Add(enterButton);
            panel.Controls.Add(nextButton);
            panel.Controls.Add(prevButton);
            panel.Controls.Add(resultLabel);
            panel.Controls.Add(clearButton);

            return panel;
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                DoSearch(searchBox.Text);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
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
                searchBox.Font = new Font("Montserrat-Regular", 8, FontStyle.Regular);
            }
        }

        private void SearchBox_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(searchBox.Text))
            {
                searchBox.Text = "Search logs...";
                searchBox.ForeColor = Color.Gray;
                searchBox.Font = new Font("Montserrat-Regular", 8, FontStyle.Italic);
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
            return contextMenu;
        }
    }
}
