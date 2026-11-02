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

        // Міні-гра на утримання
        private bool isFighting;
        private bool isHolding;
        private float playerPos;      // позиція маркера на смузі, 0–100 знизу вгору
        private float fightProgress;  // прогрес вилову, 0–100
        private int fightElapsed;     // мс від початку міні-гри
        private const float ZoneLow = 40f;
        private const float ZoneHigh = 65f;
        private const int FightTimeLimit = 12000;

        private readonly System.Windows.Forms.Timer tmrCast = new System.Windows.Forms.Timer { Interval = 16 };
        private readonly System.Windows.Forms.Timer tmrBite = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer tmrReaction = new System.Windows.Forms.Timer { Interval = 1500 };
        private readonly System.Windows.Forms.Timer tmrFight = new System.Windows.Forms.Timer { Interval = 16 };

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
            tmrFight.Tick += TmrFight_Tick;

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

            // Під час міні-гри утримання тримає маркер вгорі
            if (isFighting)
            {
                isHolding = true;
                return;
            }

            // Клік у вікні реакції: підсічення і старт міні-гри
            if (isBiting)
            {
                tmrReaction.Stop();
                isBiting = false;
                score += 1;
                UpdateScore(score);
                StartFight();
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
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (isFighting)
            {
                isHolding = false;
                return;
            }

            if (!isCharging)
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

        private void StartFight()
        {
            isFighting = true;
            isHolding = false;
            playerPos = 30f;
            fightProgress = 30f;
            fightElapsed = 0;
            statusText = "Риба на гачку!";
            tmrFight.Start();
        }

        private void EndFight(bool caught)
        {
            tmrFight.Stop();
            isFighting = false;
            isHolding = false;
            statusText = caught ? "Зловлено!" : "Риба втекла";
            pnlGame.Invalidate();
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

        private void TmrFight_Tick(object sender, EventArgs e)
        {
            // Маркер піднімається під час утримання і падає, коли кнопку відпущено
            playerPos += isHolding ? 1.2f : -0.9f;
            playerPos = Math.Max(0f, Math.Min(100f, playerPos));

            // Прогрес росте, поки маркер у зеленій зоні, і падає поза нею
            bool inZone = playerPos >= ZoneLow && playerPos <= ZoneHigh;
            fightProgress += inZone ? 0.4f : -0.25f;
            fightProgress = Math.Max(0f, Math.Min(100f, fightProgress));

            fightElapsed += tmrFight.Interval;

            if (fightProgress >= 100f)
            {
                EndFight(true);
            }
            else if (fightProgress <= 0f || fightElapsed >= FightTimeLimit)
            {
                EndFight(false);
            }
            else
            {
                pnlGame.Invalidate();
            }
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

            // Міні-гра: смуга з зеленою зоною і маркером, та шкала прогресу поруч
            if (isFighting)
            {
                DrawFightBar(e.Graphics);
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

        private void DrawFightBar(Graphics g)
        {
            const int barWidth = 30;
            const int barHeight = 300;
            const int barTop = 60;
            int barLeft = pnlGame.Width - 80;

            // Фон смуги
            g.FillRectangle(Brushes.White, barLeft, barTop, barWidth, barHeight);

            // Зелена зона: значення 100 — верх смуги, 0 — низ
            float zoneTop = barTop + (100f - ZoneHigh) / 100f * barHeight;
            float zoneBottom = barTop + (100f - ZoneLow) / 100f * barHeight;
            g.FillRectangle(Brushes.LightGreen, barLeft, zoneTop, barWidth, zoneBottom - zoneTop);

            // Рамка смуги
            g.DrawRectangle(Pens.Black, barLeft, barTop, barWidth, barHeight);

            // Маркер гравця
            float markerY = barTop + (100f - playerPos) / 100f * barHeight;
            g.FillRectangle(Brushes.Black, barLeft - 4, markerY - 3, barWidth + 8, 6);

            // Шкала прогресу зліва від смуги, заповнюється знизу вгору
            int progressLeft = barLeft - 30;
            g.DrawRectangle(Pens.Black, progressLeft, barTop, 16, barHeight);
            float fillHeight = fightProgress / 100f * barHeight;
            g.FillRectangle(Brushes.Gold, progressLeft + 1, barTop + barHeight - fillHeight, 14, fillHeight);
        }
    }
}
