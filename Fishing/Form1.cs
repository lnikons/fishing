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
        // Точка кінчика вудки і поточна позиція поплавця
        private readonly PointF rodTip = new PointF(80, 60);
        private PointF floatPos;
        private PointF floatTarget;

        // Стан закидання
        private bool isCharging;
        private bool isFlying;
        private int castPower;
        private int castDirection = 1;

        // Стан поклювання
        private bool isBiting;
        private int score;
        private string statusText = "";
        private readonly Random rng = new Random();

        private readonly System.Windows.Forms.Timer tmrCast = new System.Windows.Forms.Timer { Interval = 16 };
        private readonly System.Windows.Forms.Timer tmrBite = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer tmrReaction = new System.Windows.Forms.Timer { Interval = 1500 };

        public Form1()
        {
            InitializeComponent();

            btnPlay.Click += (s, e) => ShowPanel(pnlGame);
            btnExit.Click += (s, e) => Close();

            floatPos = rodTip;
            floatTarget = rodTip;

            tmrCast.Tick += TmrCast_Tick;
            tmrBite.Tick += TmrBite_Tick;
            tmrReaction.Tick += TmrReaction_Tick;

            pnlGame.MouseDown += PnlGame_MouseDown;
            pnlGame.MouseUp += PnlGame_MouseUp;
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

        private void PnlGame_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            // Клік у вікні реакції: підсічення
            if (isBiting)
            {
                tmrReaction.Stop();
                isBiting = false;
                score += 1;
                UpdateScore(score);
                statusText = "Підсічено!";
                pnlGame.Invalidate();
                return;
            }

            // Поки поплавець летить або чекає поклювання, закидати не можна
            if (isFlying || tmrBite.Enabled)
            {
                return;
            }

            isCharging = true;
            castPower = 0;
            castDirection = 1;
            statusText = "";
            tmrCast.Start();
        }

        private void PnlGame_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !isCharging)
            {
                return;
            }

            isCharging = false;
            StartCast(castPower);
        }

        private void StartCast(int power)
        {
            // Чим сильніше заряд, тим далі летить поплавець
            float distance = 150 + power * 4;
            floatPos = rodTip;
            floatTarget = new PointF(rodTip.X + distance, pnlGame.Height - 100);
            isFlying = true;
            tmrCast.Start();
        }

        private void StartBiteWait()
        {
            // Випадковий час очікування від 2 до 5 секунд
            tmrBite.Interval = rng.Next(2000, 5001);
            tmrBite.Start();
        }

        private void TmrBite_Tick(object sender, EventArgs e)
        {
            tmrBite.Stop();
            isBiting = true;
            tmrReaction.Start();
            pnlGame.Invalidate();
        }

        private void TmrReaction_Tick(object sender, EventArgs e)
        {
            // Вікно реакції минуло без кліку
            tmrReaction.Stop();
            isBiting = false;
            statusText = "Риба втекла";
            pnlGame.Invalidate();
        }

        private void TmrCast_Tick(object sender, EventArgs e)
        {
            if (isCharging)
            {
                // Заряд ходить туди-сюди від 0 до 100
                castPower += castDirection * 3;
                if (castPower >= 100)
                {
                    castPower = 100;
                    castDirection = -1;
                }
                else if (castPower <= 0)
                {
                    castPower = 0;
                    castDirection = 1;
                }
            }
            else if (isFlying)
            {
                // Поплавець летить до цілі
                float dx = floatTarget.X - floatPos.X;
                float dy = floatTarget.Y - floatPos.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);

                if (length <= 8)
                {
                    floatPos = floatTarget;
                    isFlying = false;
                    tmrCast.Stop();
                    StartBiteWait();
                }
                else
                {
                    floatPos = new PointF(floatPos.X + dx / length * 8, floatPos.Y + dy / length * 8);
                }
            }
            else
            {
                tmrCast.Stop();
            }

            pnlGame.Invalidate();
        }

        private void pnlGame_Paint(object sender, PaintEventArgs e)
        {
            // Вудка і волосінь
            using (var rodPen = new Pen(Color.SaddleBrown, 3))
            {
                e.Graphics.DrawLine(rodPen, rodTip, floatPos);
            }

            // Поплавець
            e.Graphics.FillEllipse(Brushes.Red, floatPos.X - 6, floatPos.Y - 6, 12, 12);
            e.Graphics.FillEllipse(Brushes.White, floatPos.X - 3, floatPos.Y - 3, 6, 6);

            // Знак поклювання над поплавцем
            if (isBiting)
            {
                using (var alertFont = new Font("Segoe UI", 20F, FontStyle.Bold))
                {
                    e.Graphics.DrawString("!", alertFont, Brushes.Red, floatPos.X - 8, floatPos.Y - 55);
                }
            }

            // Шкала сили закидання
            if (isCharging)
            {
                e.Graphics.DrawRectangle(Pens.Black, 20, pnlGame.Height - 40, 200, 16);
                e.Graphics.FillRectangle(Brushes.OrangeRed, 21, pnlGame.Height - 39, castPower * 2 - 1 < 0 ? 0 : castPower * 2 - 1, 14);
            }

            // Повідомлення про результат
            if (!string.IsNullOrEmpty(statusText))
            {
                using (var statusFont = new Font("Segoe UI", 14F, FontStyle.Bold))
                {
                    e.Graphics.DrawString(statusText, statusFont, Brushes.Black, 20, 20);
                }
            }
        }
    }
}
