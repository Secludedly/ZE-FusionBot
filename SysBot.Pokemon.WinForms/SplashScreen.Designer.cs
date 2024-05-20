namespace SysBot.Pokemon.WinForms
{
    partial class SplashScreen
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.PictureBox pictureBox;

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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(SplashScreen));
            pictureBox2 = new System.Windows.Forms.PictureBox();
            ((System.ComponentModel.ISupportInitialize)pictureBox2).BeginInit();
            SuspendLayout();
            // 
            // pictureBox2
            // 
            pictureBox2.Image = (System.Drawing.Image)resources.GetObject("pictureBox2.Image");
            pictureBox2.Location = new System.Drawing.Point(-2, -1);
            pictureBox2.Name = "pictureBox2";
            pictureBox2.Size = new System.Drawing.Size(683, 232);
            pictureBox2.TabIndex = 1;
            pictureBox2.TabStop = false;
            // 
            // SplashScreen
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(8F, 20F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(677, 229);
            Controls.Add(pictureBox2);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            Name = "SplashScreen";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "Form1";
            TopMost = true;
            ((System.ComponentModel.ISupportInitialize)pictureBox2).EndInit();
            ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.PictureBox pictureBox2;
    }
}
