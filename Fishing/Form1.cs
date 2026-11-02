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
using Fishing.Models;

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

        // Екран результату вилову
        private bool isShowingResult;
        private Fish lastFish;
        private double lastWeight;
        private int lastPrice;
        private bool lastIsNewSpecies;
        private readonly HashSet<int> discoveredFishIds = new HashSet<int>();

        // ТИМЧАСОВО до карток 20–21 (каталог і вибір риби на бекенді):
        // кілька риб на моделі Fish, щоб екран результату мав що показати.
        private static readonly List<Fish> TempFishCatalog = new List<Fish>
        {
            new Fish { Id = 1, Name = "Карась",       Rarity = Rarity.Common,    BasePrice = 5,   MinWeight = 0.2,  MaxWeight = 0.8 },
            new Fish { Id = 2, Name = "Окунь",        Rarity = Rarity.Common,    BasePrice = 7,   MinWeight = 0.1,  MaxWeight = 0.6 },
            new Fish { Id = 3, Name = "Щука",         Rarity = Rarity.Uncommon,  BasePrice = 15,  MinWeight = 1.0,  MaxWeight = 4.0 },
            new Fish { Id = 4, Name = "Короп",        Rarity = Rarity.Rare,      BasePrice = 25,  MinWeight = 1.5,  MaxWeight = 6.0 },
            new Fish { Id = 5, Name = "Сом",          Rarity = Rarity.Epic,      BasePrice = 50,  MinWeight = 5.0,  MaxWeight = 20.0 },
            new Fish { Id = 6, Name = "Золота рибка", Rarity = Rarity.Legendary, BasePrice = 100, MinWeight = 0.05, MaxWeight = 0.2 },
        };

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
        private readonly Font resultTitleFont = new Font("Segoe UI", 26F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font resultTextFont = new Font("Segoe UI", 18F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font resultHintFont = new Font("Segoe UI", 14F, FontStyle.Italic, GraphicsUnit.Pixel);
        private readonly SolidBrush overlayBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
        private readonly StringFormat centerFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

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
            // Екран результату закривається будь-яким натисканням
            if (isShowingResult)
            {
                isShowingResult = false;
                statusText = "";
                pnlGame.Invalidate();
                return;
            }

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
            if (caught)
            {
                ShowCatchResult(PickCaughtFish());
            }
            else
            {
                statusText = "Риба втекла";
            }
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

            // Екран результату поверх гри
            if (isShowingResult && lastFish != null)
            {
                DrawResultCard(e.Graphics);
            }
        }

        private Fish PickCaughtFish()
        {
            // ТИМЧАСОВО: випадкова риба з тимчасового списку, рідкісні випадають рідше.
            // Замінити на вибір з бекенду (картка 21).
            int[] rarityWeights = { 50, 25, 15, 8, 2 }; // Common .. Legendary
            int total = TempFishCatalog.Sum(f => rarityWeights[(int)f.Rarity]);
            int roll = rng.Next(total);
            foreach (Fish fish in TempFishCatalog)
            {
                roll -= rarityWeights[(int)fish.Rarity];
                if (roll < 0)
                {
                    return fish;
                }
            }
            return TempFishCatalog[0];
        }

        private void ShowCatchResult(Fish fish)
        {
            lastFish = fish;
            lastWeight = fish.MinWeight + rng.NextDouble() * (fish.MaxWeight - fish.MinWeight);
            lastPrice = fish.BasePrice;
            lastIsNewSpecies = discoveredFishIds.Add(fish.Id); // true, якщо цей вид ще не ловили
            statusText = "";
            isShowingResult = true;
        }

        private static string RarityName(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Common: return "Звичайна";
                case Rarity.Uncommon: return "Незвичайна";
                case Rarity.Rare: return "Рідкісна";
                case Rarity.Epic: return "Епічна";
                default: return "Легендарна";
            }
        }

        private static Color RarityColor(Rarity rarity)
        {
            switch (rarity)
            {
                case Rarity.Common: return Color.DimGray;
                case Rarity.Uncommon: return Color.ForestGreen;
                case Rarity.Rare: return Color.RoyalBlue;
                case Rarity.Epic: return Color.MediumOrchid;
                default: return Color.DarkOrange;
            }
        }

        private static string CoinsWord(int amount)
        {
            // 1 монета, 2–4 монети, 5+ монет (з урахуванням 11–14)
            int lastTwo = amount % 100;
            int last = amount % 10;
            if (lastTwo >= 11 && lastTwo <= 14) return "монет";
            if (last == 1) return "монета";
            if (last >= 2 && last <= 4) return "монети";
            return "монет";
        }

        private void DrawResultCard(Graphics g)
        {
            float width = LogicalWidth;
            float height = LogicalHeight;

            // Затемнення ігрового поля
            g.FillRectangle(overlayBrush, 0, 0, width, height);

            // Картка по центру
            const float cardWidth = 380;
            const float cardHeight = 250;
            float cardLeft = (width - cardWidth) / 2;
            float cardTop = (height - cardHeight) / 2;
            g.FillRectangle(Brushes.White, cardLeft, cardTop, cardWidth, cardHeight);
            g.DrawRectangle(Pens.Black, cardLeft, cardTop, cardWidth, cardHeight);

            Color rarityColor = RarityColor(lastFish.Rarity);

            // Заголовок
            g.DrawString("Зловлено!", resultTitleFont, Brushes.Black,
                new RectangleF(cardLeft, cardTop + 10, cardWidth, 36), centerFormat);

            // Проста риба-заглушка в кольорі рідкості (справжні спрайти — картка 16)
            float fishX = cardLeft + cardWidth / 2;
            float fishY = cardTop + 75;
            using (var fishBrush = new SolidBrush(rarityColor))
            {
                g.FillEllipse(fishBrush, fishX - 35, fishY - 15, 60, 30);
                g.FillPolygon(fishBrush, new[]
                {
                    new PointF(fishX + 20, fishY),
                    new PointF(fishX + 42, fishY - 14),
                    new PointF(fishX + 42, fishY + 14),
                });
            }
            g.FillEllipse(Brushes.White, fishX - 25, fishY - 6, 7, 7);

            // Назва, рідкість, вага, ціна
            using (var rarityBrush = new SolidBrush(rarityColor))
            {
                g.DrawString(lastFish.Name, resultTitleFont, rarityBrush,
                    new RectangleF(cardLeft, cardTop + 98, cardWidth, 34), centerFormat);
                g.DrawString(RarityName(lastFish.Rarity), resultTextFont, rarityBrush,
                    new RectangleF(cardLeft, cardTop + 130, cardWidth, 24), centerFormat);
            }
            g.DrawString($"Вага: {lastWeight:0.00} кг", resultTextFont, Brushes.Black,
                new RectangleF(cardLeft, cardTop + 156, cardWidth, 24), centerFormat);
            g.DrawString($"Ціна: {lastPrice} {CoinsWord(lastPrice)}", resultTextFont, Brushes.Black,
                new RectangleF(cardLeft, cardTop + 182, cardWidth, 24), centerFormat);

            // Позначка нового виду в правому верхньому куті картки
            if (lastIsNewSpecies)
            {
                var badge = new RectangleF(cardLeft + cardWidth - 128, cardTop - 14, 140, 30);
                g.FillRectangle(Brushes.Gold, badge);
                g.DrawRectangle(Pens.DarkGoldenrod, badge.X, badge.Y, badge.Width, badge.Height);
                g.DrawString("Новий вид!", statusFont, Brushes.Black, badge, centerFormat);
            }

            // Підказка
            g.DrawString("ЛКМ або пробіл — продовжити", resultHintFont, Brushes.DimGray,
                new RectangleF(cardLeft, cardTop + cardHeight - 30, cardWidth, 22), centerFormat);
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
