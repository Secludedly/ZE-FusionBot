using System;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    public partial class HubForm : Form
    {
        private readonly object _hubConfig;

        public HubForm(object selectedObject)
        {
            InitializeComponent();

            _hubConfig = selectedObject;
            PG_Hub.SelectedObject = _hubConfig;

            // Optional: Auto-save on close
            this.FormClosed += (_, _) =>
            {
                PG_Hub.Refresh(); // Apply changes
                Main.Instance?.SaveCurrentConfig();
            };
        }
    }
}

