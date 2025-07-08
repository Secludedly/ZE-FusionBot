using System.Windows.Forms;
using System;
using System.Drawing;
using System.Windows.Forms;


namespace SysBot.Pokemon.WinForms
{
    partial class BotController
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

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            RTB_Description = new RichTextBox();
            RTB_Left = new RichTextBox();
            PB_Lamp = new PictureBox();
            RCMenu = new ContextMenuStrip(components);
            ((System.ComponentModel.ISupportInitialize)PB_Lamp).BeginInit();
            SuspendLayout();
            // 
            // RTB_Description
            // 
            RTB_Description.BackColor = Color.FromArgb(20, 19, 57);
            RTB_Description.BorderStyle = BorderStyle.None;
            RTB_Description.Font = new Font("Gadugi", 9F);
            RTB_Description.ForeColor = Color.White;
            RTB_Description.Location = new Point(41, 46);
            RTB_Description.Name = "RTB_Description";
            RTB_Description.ReadOnly = true;
            RTB_Description.ScrollBars = RichTextBoxScrollBars.None;
            RTB_Description.Size = new Size(660, 39);
            RTB_Description.TabIndex = 6;
            RTB_Description.TabStop = false;
            RTB_Description.Text = "";
            // 
            // RTB_Left
            // 
            RTB_Left.BackColor = Color.FromArgb(20, 19, 57);
            RTB_Left.BorderStyle = BorderStyle.None;
            RTB_Left.Font = new Font("Gadugi", 9F);
            RTB_Left.ForeColor = Color.White;
            RTB_Left.Location = new Point(41, 6);
            RTB_Left.Name = "RTB_Left";
            RTB_Left.ReadOnly = true;
            RTB_Left.ScrollBars = RichTextBoxScrollBars.None;
            RTB_Left.Size = new Size(660, 41);
            RTB_Left.TabIndex = 5;
            RTB_Left.TabStop = false;
            RTB_Left.Text = "";
            // 
            // PB_Lamp
            // 
            PB_Lamp.Location = new Point(7, 4);
            PB_Lamp.Margin = new Padding(4, 5, 4, 5);
            PB_Lamp.Name = "PB_Lamp";
            PB_Lamp.Size = new Size(29, 32);
            PB_Lamp.TabIndex = 4;
            PB_Lamp.TabStop = false;
            // 
            // RCMenu
            // 
            RCMenu.ImageScalingSize = new Size(20, 20);
            RCMenu.Name = "RCMenu";
            RCMenu.ShowImageMargin = false;
            RCMenu.ShowItemToolTips = false;
            RCMenu.Size = new Size(36, 4);
            // 
            // BotController
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(20, 19, 57);
            ContextMenuStrip = RCMenu;
            Controls.Add(PB_Lamp);
            Controls.Add(RTB_Left);
            Controls.Add(RTB_Description);
            Margin = new Padding(0);
            Name = "BotController";
            Size = new Size(704, 91);
            MouseEnter += BotController_MouseEnter;
            MouseLeave += BotController_MouseLeave;
            ((System.ComponentModel.ISupportInitialize)PB_Lamp).EndInit();
            ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.RichTextBox RTB_Description;
        private System.Windows.Forms.RichTextBox RTB_Left;
        private System.Windows.Forms.PictureBox PB_Lamp;
        private System.Windows.Forms.ContextMenuStrip RCMenu;
    }
}
