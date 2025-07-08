using System.Drawing;

namespace SysBot.Pokemon.WinForms
{
    partial class HubForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            PG_Hub = new System.Windows.Forms.PropertyGrid();
            SuspendLayout();
            // 
            // PG_Hub
            // 
            PG_Hub.BackColor = Color.FromArgb(30, 30, 60);
            PG_Hub.CategoryForeColor = Color.White;
            PG_Hub.CategorySplitterColor = Color.FromArgb(249, 88, 155);
            PG_Hub.CommandsBackColor = Color.FromArgb(10, 10, 40);
            PG_Hub.CommandsDisabledLinkColor = Color.Silver;
            PG_Hub.DisabledItemForeColor = Color.FromArgb(10, 10, 40);
            PG_Hub.Dock = System.Windows.Forms.DockStyle.Fill;
            PG_Hub.Font = new Font("Trebuchet MS", 9F);
            PG_Hub.HelpBackColor = Color.FromArgb(30, 30, 60);
            PG_Hub.HelpBorderColor = Color.Black;
            PG_Hub.HelpForeColor = Color.White;
            PG_Hub.LineColor = Color.FromArgb(31, 30, 68);
            PG_Hub.Location = new Point(0, 0);
            PG_Hub.Name = "PG_Hub";
            PG_Hub.SelectedItemWithFocusBackColor = Color.Orchid;
            PG_Hub.Size = new Size(739, 305);
            PG_Hub.TabIndex = 0;
            PG_Hub.ViewBackColor = Color.FromArgb(10, 10, 40);
            // 
            // HubForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            BackColor = Color.FromArgb(23, 22, 60);
            ClientSize = new Size(739, 305);
            Controls.Add(PG_Hub);
            Name = "HubForm";
            Text = "Hub Controls";
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.PropertyGrid PG_Hub;
    }
}
