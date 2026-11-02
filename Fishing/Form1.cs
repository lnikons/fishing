using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Reflection;
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
        private bool spaceDown;       // пробіл зараз утримується
        private float playerPos;      // центр зеленої зони на смузі, 0–100 знизу вгору
        private float fightProgress;  // прогрес вилову, 0–100
        private int fightElapsed;     // мс від початку міні-гри
        private float fishPos;        // позиція риби на смузі, 0–100
        private float fishTarget;     // куди зараз плаває риба
        private int fishRetargetMs;   // скільки ще риба плаває до нової цілі
        private const float ZoneHalfSize = 12.5f;
        private const int FightTimeLimit = 12000;

        // Масштаб екрана: 1.0 при 100%, 1.25 при 125% і т. д.
        // Гра малюється в логічних пікселях (як при 100%), а Graphics масштабує їх під екран.
        private float DpiScale => DeviceDpi / 96f;
        private float LogicalWidth => pnlGame.ClientSize.Width / DpiScale;
        private float LogicalHeight => pnlGame.ClientSize.Height / DpiScale;

        // Ресурси для малювання: створюються один раз, а не в кожному кадрі.
        // Шрифти в пікселях, щоб масштаб не застосувався до них двічі.
        private readonly Pen rodPen = new Pen(Color.SaddleBrown, 3);
        private readonly Font alertFont = new Font("Segoe UI", 27F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font statusFont = new Font("Segoe UI", 19F, FontStyle.Bold, GraphicsUnit.Pixel);

        private readonly System.Windows.Forms.Timer tmrCast = new System.Windows.Forms.Timer { Interval = 16 };
        private readonly System.Windows.Forms.Timer tmrBite = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer tmrReaction = new System.Windows.Forms.Timer { Interval = 1500 };
        private readonly System.Windows.Forms.Timer tmrFight = new System.Windows.Forms.Timer { Interval = 16 };

        public Form1()
        {
            InitializeComponent();

            // Фіксований розмір вікна: не можна тягнути краї і розгортати
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            // Панелі займають всю область під написом монет
            pnlMenu.Size = new Size(ClientSize.Width, ClientSize.Height - pnlMenu.Top);
            pnlGame.Size = new Size(ClientSize.Width, ClientSize.Height - pnlGame.Top);

            // Клавіатура: пробіл дублює ЛКМ
            KeyPreview = true;
            KeyDown += Form1_KeyDown;
            KeyUp += Form1_KeyUp;
            Deactivate += (s, e) => ReleaseAllInput();

            btnPlay.Click += (s, e) => ShowPanel(pnlGame);
            btnExit.Click += (s, e) => Close();

            floatPos = rodTip;
            floatTarget = rodTip;

            tmrCast.Tick += TmrCast_Tick;
            tmrBite.Tick += TmrBite_Tick;
            tmrReaction.Tick += TmrReaction_Tick;
            tmrFight.Tick += TmrFight_Tick;

            // Подвійне буферизування прибирає мерехтіння при малюванні
            EnableDoubleBuffering(pnlGame);

            // Малювання підключено в коді, щоб дизайнер його не загубив
            pnlGame.Paint += pnlGame_Paint;
            pnlGame.MouseDown += PnlGame_MouseDown;
            pnlGame.MouseUp += PnlGame_MouseUp;
        }

        private static void EnableDoubleBuffering(Control control)
        {
            typeof(Control)
                .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(control, true, null);
        }

        private void ShowPanel(Panel panel)
        {
            pnlMenu.Visible = false;
            pnlGame.Visible = false;
            panel.Visible = true;
            ActiveControl = null;
        }

        public void UpdateScore(int points)
        {
            lblScore.Text = $"Монети: {points}";
        }

        private void PnlGame_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                HandlePress();
            }
        }

        private void PnlGame_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                HandleRelease();
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;

                // Автоповтор утриманої клавіші не повинен знову запускати дію
                if (!spaceDown && pnlGame.Visible)
                {
                    spaceDown = true;
                    HandlePress();
                }
            }
            else if (e.KeyCode == Keys.Menu || e.KeyCode == Keys.F10)
            {
                // Alt і F10 вмикають режим меню вікна, який з'їдає наступне введення
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void Form1_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                e.Handled = true;
                if (spaceDown)
                {
                    spaceDown = false;
                    HandleRelease();
                }
            }
            else if (e.KeyCode == Keys.Menu || e.KeyCode == Keys.F10)
            {
                e.Handled = true;
            }
        }

        private void ReleaseAllInput()
        {
            // Вікно втратило фокус: відпускання могло не дійти, тому скидаємо утримання
            spaceDown = false;
            isHolding = false;
            if (isCharging)
            {
                isCharging = false;
                tmrCast.Stop();
                pnlGame.Invalidate();
            }
        }

        private void HandlePress()
        {
            // Під час міні-гри утримання тримає зону вгорі
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

        private void HandleRelease()
        {
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
            floatTarget = new PointF(rodTip.X + distance, LogicalHeight - 100);
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
            playerPos = 50f;
            fightProgress = 30f;
            fightElapsed = 0;
            fishPos = 50f;
            PickNewFishTarget();
            statusText = "Риба на гачку!";
            tmrFight.Start();
        }

        private void PickNewFishTarget()
        {
            // Риба обирає нову точку на смузі і плаває до неї
            fishTarget = rng.Next(5, 96);
            fishRetargetMs = rng.Next(800, 2001);
        }

        private void EndFight(bool caught)
        {
            tmrFight.Stop();
            isFighting = false;
            isHolding = false;
            statusText = caught ? "Зловлено!" : "Риба втекла";
            pnlGame.Invalidate();
        }

        private bool IsFishInZone()
        {
            return fishPos >= playerPos - ZoneHalfSize && fishPos <= playerPos + ZoneHalfSize;
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
            // Зелена зона піднімається під час утримання і опускається, коли кнопку відпущено
            playerPos += isHolding ? 1.2f : -0.9f;
            playerPos = Math.Max(ZoneHalfSize, Math.Min(100f - ZoneHalfSize, playerPos));

            // Риба плаває випадково: доходить до цілі і обирає нову
            if (fishRetargetMs <= 0 || Math.Abs(fishTarget - fishPos) < 1f)
            {
                PickNewFishTarget();
            }
            fishRetargetMs -= tmrFight.Interval;
            fishPos += Math.Sign(fishTarget - fishPos) * 0.8f;
            fishPos = Math.Max(0f, Math.Min(100f, fishPos));

            // Прогрес росте, поки риба в зеленій зоні, і падає поза нею
            fightProgress += IsFishInZone() ? 0.5f : -0.3f;
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
            // Масштаб під DPI і згладжування ліній та тексту
            e.Graphics.ScaleTransform(DpiScale, DpiScale);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Вудка і волосінь
            e.Graphics.DrawLine(rodPen, rodTip, floatPos);

            // Поплавець
            e.Graphics.FillEllipse(Brushes.Red, floatPos.X - 6, floatPos.Y - 6, 12, 12);
            e.Graphics.FillEllipse(Brushes.White, floatPos.X - 3, floatPos.Y - 3, 6, 6);

            // Знак поклювання над поплавцем
            if (isBiting)
            {
                e.Graphics.DrawString("!", alertFont, Brushes.Red, floatPos.X - 8, floatPos.Y - 55);
            }

            // Шкала сили закидання
            if (isCharging)
            {
                e.Graphics.DrawRectangle(Pens.Black, 20, LogicalHeight - 40, 200, 16);
                e.Graphics.FillRectangle(Brushes.OrangeRed, 21, LogicalHeight - 39, castPower * 2 - 1 < 0 ? 0 : castPower * 2 - 1, 14);
            }

            // Міні-гра: смуга з зеленою зоною, рибою і шкала прогресу поруч
            if (isFighting)
            {
                DrawFightBar(e.Graphics);
            }

            // Повідомлення про результат
            if (!string.IsNullOrEmpty(statusText))
            {
                e.Graphics.DrawString(statusText, statusFont, Brushes.Black, 20, 20);
            }
        }

        private void DrawFightBar(Graphics g)
        {
            const int barWidth = 30;
            const int barHeight = 300;
            const int barTop = 60;
            float barLeft = LogicalWidth - 80;

            // Фон смуги
            g.FillRectangle(Brushes.White, barLeft, barTop, barWidth, barHeight);

            // Зелена зона навколо центру playerPos: значення 100 — верх смуги, 0 — низ
            float zoneHighValue = Math.Min(100f, playerPos + ZoneHalfSize);
            float zoneLowValue = Math.Max(0f, playerPos - ZoneHalfSize);
            float zoneTop = barTop + (100f - zoneHighValue) / 100f * barHeight;
            float zoneBottom = barTop + (100f - zoneLowValue) / 100f * barHeight;
            g.FillRectangle(Brushes.LightGreen, barLeft, zoneTop, barWidth, zoneBottom - zoneTop);

            // Рамка смуги
            g.DrawRectangle(Pens.Black, barLeft, barTop, barWidth, barHeight);

            // Риба: темно-червона точка, що плаває по смузі
            float fishY = barTop + (100f - fishPos) / 100f * barHeight;
            g.FillEllipse(Brushes.DarkRed, barLeft + barWidth / 2f - 6, fishY - 5, 12, 10);

            // Шкала прогресу зліва від смуги, заповнюється знизу вгору
            float progressLeft = barLeft - 30;
            g.DrawRectangle(Pens.Black, progressLeft, barTop, 16, barHeight);
            float fillHeight = fightProgress / 100f * barHeight;
            g.FillRectangle(Brushes.Gold, progressLeft + 1, barTop + barHeight - fillHeight, 14, fillHeight);
        }
    }
}
