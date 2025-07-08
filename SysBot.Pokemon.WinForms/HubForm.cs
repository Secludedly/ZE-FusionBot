using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SysBot.Pokemon.WinForms
{
    public partial class HubForm : Form
    {
        public HubForm(object selectedObject)
        {
            InitializeComponent();

            PG_Hub.SelectedObject = selectedObject;
        }
    }
}
