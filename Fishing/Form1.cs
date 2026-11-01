using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Fishing
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();

            btnPlay.Click += (s, e) => ShowPanel(pnlGame);
            btnExit.Click += (s, e) => Close();
        }

        private void ShowPanel(Panel panel)
        {
            pnlMenu.Visible = false;
            pnlGame.Visible = false;
            panel.Visible = true;
        }

        public void UpdateScore(int points)
        {
            lblScore.Text = $"Бали: {points}";
        }

        private void pnlGame_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}
