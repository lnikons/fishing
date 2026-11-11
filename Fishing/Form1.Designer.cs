namespace Fishing
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.Label lblScore;
        private System.Windows.Forms.Panel pnlMenu;
        private System.Windows.Forms.Panel pnlGame;
        private System.Windows.Forms.Button btnPlay;
        private System.Windows.Forms.Button btnJournal;
        private System.Windows.Forms.Button btnExit;

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
            this.lblScore = new System.Windows.Forms.Label();
            this.pnlMenu = new System.Windows.Forms.Panel();
            this.btnPlay = new System.Windows.Forms.Button();
            this.btnJournal = new System.Windows.Forms.Button();
            this.btnExit = new System.Windows.Forms.Button();
            this.pnlGame = new System.Windows.Forms.Panel();
            this.pnlMenu.SuspendLayout();
            this.SuspendLayout();
            //
            // lblScore
            //
            this.lblScore.AutoSize = true;
            this.lblScore.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            this.lblScore.Location = new System.Drawing.Point(12, 9);
            this.lblScore.Name = "lblScore";
            this.lblScore.Size = new System.Drawing.Size(120, 25);
            this.lblScore.TabIndex = 0;
            this.lblScore.Text = "Монети: 0";
            //
            // pnlMenu
            //
            this.pnlMenu.Controls.Add(this.btnExit);
            this.pnlMenu.Controls.Add(this.btnJournal);
            this.pnlMenu.Controls.Add(this.btnPlay);
            this.pnlMenu.Location = new System.Drawing.Point(0, 50);
            this.pnlMenu.Name = "pnlMenu";
            this.pnlMenu.Size = new System.Drawing.Size(800, 400);
            this.pnlMenu.TabIndex = 1;
            //
            // btnPlay
            //
            this.btnPlay.Location = new System.Drawing.Point(300, 100);
            this.btnPlay.Name = "btnPlay";
            this.btnPlay.Size = new System.Drawing.Size(200, 50);
            this.btnPlay.TabIndex = 0;
            this.btnPlay.Text = "Грати";
            this.btnPlay.UseVisualStyleBackColor = true;
            //
            // btnJournal
            //
            this.btnJournal.Location = new System.Drawing.Point(300, 170);
            this.btnJournal.Name = "btnJournal";
            this.btnJournal.Size = new System.Drawing.Size(200, 50);
            this.btnJournal.TabIndex = 1;
            this.btnJournal.Text = "Журнал";
            this.btnJournal.UseVisualStyleBackColor = true;
            //
            // btnExit
            //
            this.btnExit.Location = new System.Drawing.Point(300, 240);
            this.btnExit.Name = "btnExit";
            this.btnExit.Size = new System.Drawing.Size(200, 50);
            this.btnExit.TabIndex = 2;
            this.btnExit.Text = "Вихід";
            this.btnExit.UseVisualStyleBackColor = true;
            //
            // pnlGame
            //
            this.pnlGame.BackColor = System.Drawing.Color.LightBlue;
            this.pnlGame.Location = new System.Drawing.Point(0, 50);
            this.pnlGame.Name = "pnlGame";
            this.pnlGame.Size = new System.Drawing.Size(800, 400);
            this.pnlGame.TabIndex = 2;
            this.pnlGame.Visible = false;
            //
            // Form1
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.pnlGame);
            this.Controls.Add(this.pnlMenu);
            this.Controls.Add(this.lblScore);
            this.Name = "Form1";
            this.Text = "Fishing";
            this.pnlMenu.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
    }
}
