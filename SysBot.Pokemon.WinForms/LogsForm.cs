using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    public partial class LogsForm : Form
    {
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public RichTextBox LogsBox { get; private set; }

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

            Controls.Add(LogsBox);
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
