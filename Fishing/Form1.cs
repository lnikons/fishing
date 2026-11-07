using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
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
        private readonly PointF rodTip = new PointF(84, 200); // кінчик вудки (арт-піксель 42,100); основа стоїть на помості з фону
        private PointF FloatRest => new PointF(rodTip.X, rodTip.Y + 16); // поплавець висить під кінчиком
        private PointF castFrom;                                      // звідки почався політ (для дуги)
        private PointF floatPos;
        private PointF floatTarget;

        // Стан закидання
        private bool isCharging;
        private bool isFlying;
        private bool isReeling;   // поплавець змотується назад до вудки
        private float castPower;
        private int castDirection = 1;

        // Стан поклювання
        private bool isBiting;
        private string statusText = "";
        private readonly Random rng = new Random();

        // Міні-гра на утримання
        private bool isFighting;
        private bool isHolding;
        private bool spaceDown;       // пробіл зараз утримується
        private bool mouseDown;       // ЛКМ зараз утримується на полі
        private float playerPos;      // центр зеленої зони на смузі, 0–100 знизу вгору
        private float fightProgress;  // прогрес вилову, 0–100
        private float fightElapsed;   // мс від початку міні-гри
        private float fishPos;        // позиція риби на смузі, 0–100
        private float fishTarget;     // куди зараз плаває риба
        private float fishRetargetMs; // скільки ще риба плаває до нової цілі, мс
        private const float BaseZoneHalfSize = 15f; // без прокачки зона займає 30% смуги

        // Половина зеленої зони: росте з покращенням «Краща вудка»
        private float ZoneHalfSize => BaseZoneHalfSize + (float)UpgradeValue(UpgradeType.CatchBar);

        // Риба, що зараз на гачку, і параметри її поведінки в міні-грі
        private Fish hookedFish;
        private float fishSpeed;       // скільки смуги риба проходить за 16 мс
        private float fishJumpScale;   // 1 — спокійна риба, менше — частіше міняє напрям
        private const float PressureGrowthMs = 9000f; // за скільки мс швидкість падіння шкали зростає на 100%

        // Екран результату вилову
        private bool isShowingResult;
        private CatchResult lastCatch;   // результат останньої ловлі (CatchService, картка 22)

        // Журнал риб: скільки разів спіймано кожен вид і найбільша вага
        private readonly Dictionary<int, int> catchCounts = new Dictionary<int, int>();
        private readonly Dictionary<int, double> bestWeights = new Dictionary<int, double>();
        private int journalLocationId = 1; // відкрита вкладка журналу
        private readonly List<(RectangleF Rect, int LocationId)> journalTabs = new List<(RectangleF Rect, int LocationId)>();

        // Екран журналу (створюється в коді, а не в дизайнері)
        private readonly Panel pnlJournal = new Panel();

        // Стан гравця (модель бекенду Player): монети, поточна і відкриті локації, рівні покращень
        private readonly Player player = new Player { Money = 0, CurrentLocationId = 1 };

        // Магазин (створюється в коді, а не в дизайнері)
        private readonly Panel pnlShop = new Panel();
        private readonly Button btnShop = new Button();
        private string shopMessage = "";
        private RectangleF? hoveredShopRect;  // кнопка магазину під курсором
        private Image coinImage;              // монетка для цін у магазині

        // Кнопки, намальовані на панелі магазину: прямокутник, чи активна, що робити при кліку
        private readonly List<(RectangleF Rect, bool Enabled, Action OnClick)> shopButtons =
            new List<(RectangleF Rect, bool Enabled, Action OnClick)>();

        // Масштаб екрана: 1.0 при 100%, 1.25 при 125% і т. д.
        // Гра малюється в логічних пікселях (як при 100%), а Graphics масштабує їх під екран.
        private float DpiScale => DeviceDpi / 96f;
        private float LogicalWidth => pnlGame.ClientSize.Width / DpiScale;
        private float LogicalHeight => pnlGame.ClientSize.Height / DpiScale;

        // Графіка з картки 16: вбудовані в збірку PNG (Fishing/Assets)
        private readonly Dictionary<int, Image> fishSprites = new Dictionary<int, Image>();      // Id риби → спрайт
        private readonly Dictionary<int, Image> fishSilhouettes = new Dictionary<int, Image>();  // Id риби → темний силует
        private readonly Dictionary<int, Image> backgrounds = new Dictionary<int, Image>();      // Id локації → фон
        private readonly List<Image> loadedImages = new List<Image>();                            // усе, що треба звільнити
        private Bitmap backgroundCache;   // фон, уже масштабований під розмір панелі (щоб не масштабувати щокадру)
        private int backgroundCacheLocationId = -1;
        private readonly PictureBox pbCoin = new PictureBox();

        // Ресурси для малювання: створюються один раз, а не в кожному кадрі.
        // Шрифти в пікселях, щоб масштаб не застосувався до них двічі.
        private readonly Pen rodPen = new Pen(Color.SaddleBrown, 3);
        private readonly Pen linePen = new Pen(Color.FromArgb(235, 245, 245, 245), 1.5f); // волосінь
        private readonly Font alertFont = new Font("Segoe UI", 27F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font statusFont = new Font("Segoe UI", 19F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font resultTitleFont = new Font("Segoe UI", 26F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font resultTextFont = new Font("Segoe UI", 18F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font resultHintFont = new Font("Segoe UI", 14F, FontStyle.Italic, GraphicsUnit.Pixel);
        private readonly Font shopNameFont = new Font("Segoe UI", 17F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font shopTextFont = new Font("Segoe UI", 13F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font shopButtonFont = new Font("Segoe UI", 14F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font shopTitleFont = new Font("Segoe UI", 22F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font menuTitleFont = new Font("Segoe UI", 44F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font menuButtonFont = new Font("Segoe UI", 18F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font upgradeNameFont = new Font("Segoe UI", 12F, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font upgradeDescFont = new Font("Segoe UI", 10.5F, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly SolidBrush overlayBrush = new SolidBrush(Color.FromArgb(140, 0, 0, 0));
        private readonly StringFormat centerFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        private readonly StringFormat rightFormat = new StringFormat { Alignment = StringAlignment.Far };

        private readonly System.Windows.Forms.Timer tmrCast = new System.Windows.Forms.Timer { Interval = 16 };
        private readonly System.Windows.Forms.Timer tmrBite = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer tmrReaction = new System.Windows.Forms.Timer { Interval = 1500 };
        private readonly System.Windows.Forms.Timer tmrFight = new System.Windows.Forms.Timer { Interval = 16 };

        // Повідомлення («Риба втекла», «Не вистачає монет» тощо) самі зникають через 3 с
        private readonly System.Windows.Forms.Timer tmrMessage = new System.Windows.Forms.Timer { Interval = 3000 };

        // Реальний час між тіками: таймер WinForms може тікати рідше за 16 мс,
        // тому рух рахується від справжнього часу, а не від кількості тіків
        private readonly Stopwatch castClock = new Stopwatch();
        private readonly Stopwatch fightClock = new Stopwatch();

        // Відлік від моменту, коли минуло вікно реакції: запізнілий клік не повинен одразу закидати знову
        private readonly Stopwatch sinceReactionTimeout = new Stopwatch();
        private const int LatePressIgnoreMs = 300;

        // Кнопка повернення в меню на ігровому екрані (створюється в коді, а не в дизайнері)
        private readonly Button btnMenu = new Button();
        private readonly Button btnNewGame = new Button();
        private bool lastIsRecord;   // улов побив рекорд ваги цього виду

        // Чи утримується зараз хоча б одна кнопка керування (ЛКМ або пробіл)
        private bool AnyInputDown => mouseDown || spaceDown;

        public Form1()
        {
            InitializeComponent();

            LoadGameImages();

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
            btnJournal.Click += (s, e) => ShowPanel(pnlJournal);

            // Кнопка «Магазин» у меню між «Журналом» і «Виходом»
            player.UnlockedLocationIds.Add(1); // Ставок відкритий з початку
            btnShop.Text = "Магазин";
            btnShop.Size = btnJournal.Size;
            btnShop.Location = new Point(btnJournal.Left, btnExit.Top);
            btnShop.UseVisualStyleBackColor = true;

            // Піксельна монета біля лічильника (малюється з коду цілим масштабом — без розмиття)
            int coinScale = Math.Max(2, (int)Math.Round(26 * DpiScale / 12f));
            pbCoin.Image = MakePixelBitmap(CoinSprite, coinScale);
            coinImage = pbCoin.Image;
            loadedImages.Add(pbCoin.Image);
            pbCoin.SizeMode = PictureBoxSizeMode.CenterImage;
            pbCoin.BackColor = Color.Transparent;
            pbCoin.Size = pbCoin.Image.Size;
            pbCoin.Location = new Point(lblScore.Left, lblScore.Top + (lblScore.Height - pbCoin.Height) / 2);
            lblScore.Left += pbCoin.Width + (int)(6 * DpiScale);
            Controls.Add(pbCoin);
            btnShop.Click += (s, e) => { shopMessage = ""; ShowPanel(pnlShop); };
            pnlMenu.Controls.Add(btnShop);

            // Кнопка «Нова гра»: скидає прогрес після підтвердження
            btnNewGame.Text = "Нова гра";
            btnNewGame.Click += (s, e) => StartNewGame();
            pnlMenu.Controls.Add(btnNewGame);

            // Головне меню і верхня панель у піксельному стилі
            MakePixelButton(btnPlay, IconPlay);
            MakePixelButton(btnJournal, IconJournal);
            MakePixelButton(btnShop, IconShop);
            MakePixelButton(btnNewGame, IconNewgame);
            MakePixelButton(btnExit, IconExit);
            EnableDoubleBuffering(pnlMenu);
            pnlMenu.Paint += PnlMenu_Paint;
            pnlMenu.SizeChanged += (s, e) => LayoutMenu();
            LayoutMenu();
            BackColor = Color.FromArgb(62, 39, 49);
            lblScore.ForeColor = Color.FromArgb(255, 236, 190);
            lblScore.BackColor = Color.Transparent;
            Paint += Form1_Paint;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); // іконка вікна = іконка .exe
            }
            catch (ArgumentException)
            {
                // без іконки вікно лишається зі стандартною
            }

            // Панель магазину на місці ігрової
            pnlShop.Location = pnlGame.Location;
            pnlShop.Size = pnlGame.Size;
            pnlShop.BackColor = Color.FromArgb(240, 246, 250);
            pnlShop.Visible = false;
            EnableDoubleBuffering(pnlShop);
            pnlShop.Paint += PnlShop_Paint;
            pnlShop.MouseClick += PnlShop_MouseClick;
            pnlShop.MouseMove += PnlShop_MouseMove;
            pnlShop.MouseLeave += (s, e) => { hoveredShopRect = null; pnlShop.Cursor = Cursors.Default; pnlShop.Invalidate(); };
            Controls.Add(pnlShop);

            // Панель журналу на місці ігрової
            pnlJournal.Location = pnlGame.Location;
            pnlJournal.Size = pnlGame.Size;
            pnlJournal.BackColor = Color.FromArgb(250, 244, 228);
            pnlJournal.Visible = false;
            EnableDoubleBuffering(pnlJournal);
            pnlJournal.Paint += PnlJournal_Paint;
            pnlJournal.MouseClick += PnlJournal_MouseClick;
            Controls.Add(pnlJournal);

            // Кнопка «Меню» вгорі праворуч, видна лише на ігровому екрані
            btnMenu.Text = "Меню (Esc)";
            btnMenu.Size = new Size((int)(150 * DpiScale), (int)(34 * DpiScale));
            btnMenu.Location = new Point(ClientSize.Width - btnMenu.Width - (int)(10 * DpiScale), (int)(8 * DpiScale));
            btnMenu.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnMenu.TabStop = false;
            btnMenu.Visible = false;
            btnMenu.Click += (s, e) => ReturnToMenu();
            MakePixelButton(btnMenu, IconMenu);
            Controls.Add(btnMenu);
            btnMenu.BringToFront(); // кнопка поверх гри і журналу
            btnExit.Click += (s, e) => Close();

            floatPos = FloatRest;
            floatTarget = FloatRest;

            tmrCast.Tick += TmrCast_Tick;
            tmrBite.Tick += TmrBite_Tick;
            tmrReaction.Tick += TmrReaction_Tick;
            tmrFight.Tick += TmrFight_Tick;
            tmrMessage.Tick += TmrMessage_Tick;

            // Подвійне буферизування прибирає мерехтіння при малюванні
            EnableDoubleBuffering(pnlGame);

            // Малювання підключено в коді, щоб дизайнер його не загубив
            // Спершу відписка: якщо дизайнер теж підключить Paint, кадр не малюватиметься двічі
            pnlGame.Paint -= pnlGame_Paint;
            pnlGame.Paint += pnlGame_Paint;
            pnlGame.MouseDown += PnlGame_MouseDown;
            pnlGame.MouseUp += PnlGame_MouseUp;

            // Прогрес з минулої гри; при виході — зберігаємо
            LoadProgress();
            FormClosing += (s, e) => SaveProgress();

            // Звільняємо таймери і ресурси малювання при закритті вікна
            FormClosed += (s, e) => DisposeGameResources();
        }

        private void LoadProgress()
        {
            // Збереження і завантаження — логіка бекенду (картка 25, SaveService).
            // Файлу ще немає — це перша гра, починаємо з нуля.
            if (SaveService.HasSave() && !SaveService.Load(player, catchCounts, bestWeights, out string error))
            {
                MessageBox.Show(error + "\nГра почнеться з нуля.", "Fishing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            UpdateCoinsLabel();
        }

        private void SaveProgress()
        {
            // Автозбереження після улову, покупок, зміни локації і при виході з гри
            if (!SaveService.Save(player, catchCounts, bestWeights, out string error))
            {
                ShowStatus(error);
            }
        }

        // ---------- Піксельні значки кнопок (картка 27): 12 арт-пікселів завширшки, з темною обвідкою
        private static readonly string[] IconPlay = new[] { ".....k......", "....kbk.....", "....kbk.....", "...krrrk....", "..krlrrrk...", "..krlrrrk...", "..krrrrrk...", "..kwwwwwk...", "..kwwwbwk...", "...kwbbk....", "....kbk.....", ".....k......" };
        private static readonly string[] IconJournal = new[] { ".kkkkkkkkkk.", "kntttttttttk", "kntttyyttttk", "knttyyyytytk", "kntykyyyyytk", "knttyyyytytk", "kntttyyttttk", "kntttttttttk", "knwwwwwwwwwk", "knnnnnnnnnnk", ".kkkkkkkkrk.", ".........k.." };
        private static readonly string[] IconShop = new[] { "....kkkk....", "...kppppk...", "....kddk....", "...kppppk...", "..kppppppk..", ".kpppooppqk.", "kpppoyyopqqk", "kpppoyyoqqqk", "kppppooqqqqk", ".kppppqqqqk.", "..kkkkkkkk.." };
        private static readonly string[] IconNewgame = new[] { "..kk....kk..", ".kggk..kggk.", ".kgggkkgggk.", "..kggggggk..", "...kkeekk...", "...kkeekk...", "..kddddddk..", "..kdaaaadk..", "...kddddk...", "....kkkk...." };
        private static readonly string[] IconExit = new[] { ".kkkkkk.....", "kddddddk....", "kdaaaadkk...", "kdaaaadkwk..", "kdaaaakkwwk.", "kdayaawwwwwk", "kdaaaakkwwk.", "kdaaaadkwk..", "kdaaaadkk...", "kddddddk....", ".kkkkkk....." };
        private static readonly string[] IconMenu = new[] { "..kkkkkkkk..", ".kwwwwwwwwk.", "..kkkkkkkk..", ".kwwwwwwwwk.", "..kkkkkkkk..", ".kwwwwwwwwk.", "..kkkkkkkk.." };
        private static readonly string[] CoinSprite = new[] { "...kkkkk....", "..kyyyyyk...", ".kyywwyyyk..", "kyywyyyyyok.", "kyywyyyyyok.", "kyyyyyyyyok.", "kyyyyyyyyok.", "kyyyyyyyook.", ".kyyyyyook..", "..kooooook..", "...kkkkk....", "............" };

        private static readonly Dictionary<char, Color> SpriteColors = new Dictionary<char, Color>
        {
            { 'k', Color.FromArgb(24, 20, 37) }, { 'w', Color.White }, { 'y', Color.FromArgb(254, 231, 97) },
            { 'o', Color.FromArgb(247, 180, 40) }, { 'a', Color.FromArgb(184, 111, 80) },
        };

        private static Bitmap MakePixelBitmap(string[] rows, int scale)
        {
            // Спрайт у бітмап цілим масштабом: кожен піксель — квадрат scale×scale, без згладжування
            var bitmap = new Bitmap(rows[0].Length * scale, rows.Length * scale, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                for (int y = 0; y < rows.Length; y++)
                {
                    for (int x = 0; x < rows[y].Length; x++)
                    {
                        if (SpriteColors.TryGetValue(rows[y][x], out Color color))
                        {
                            using (var brush = new SolidBrush(color))
                            {
                                g.FillRectangle(brush, x * scale, y * scale, scale, scale);
                            }
                        }
                    }
                }
            }
            return bitmap;
        }

        private readonly Dictionary<Button, string[]> pixelButtonIcons = new Dictionary<Button, string[]>();
        private Button hoveredButton;
        private Button pressedButton;

        private void MakePixelButton(Button button, string[] icon)
        {
            // Дерев'яна піксельна кнопка: малюємо її повністю самі, стандартний вигляд прибираємо
            pixelButtonIcons[button] = icon;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = Color.Transparent;
            button.FlatAppearance.MouseDownBackColor = Color.Transparent;
            button.UseVisualStyleBackColor = false;
            button.BackColor = Color.Transparent;
            button.ForeColor = Color.FromArgb(255, 236, 190);
            button.Image = null;
            button.Cursor = Cursors.Hand;
            button.TabStop = false;
            button.Paint += PixelButton_Paint;
            button.MouseEnter += (s, e) => { hoveredButton = button; button.Invalidate(); };
            button.MouseLeave += (s, e) => { if (hoveredButton == button) hoveredButton = null; pressedButton = null; button.Invalidate(); };
            button.MouseDown += (s, e) => { pressedButton = button; button.Invalidate(); };
            button.MouseUp += (s, e) => { pressedButton = null; button.Invalidate(); };
        }

        private void PixelButton_Paint(object sender, PaintEventArgs e)
        {
            var button = (Button)sender;
            Graphics g = e.Graphics;
            g.ScaleTransform(DpiScale, DpiScale);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float aw = (float)Math.Floor(button.ClientSize.Width / DpiScale / Px);
            float ah = (float)Math.Floor(button.ClientSize.Height / DpiScale / Px);
            bool hover = hoveredButton == button;
            float press = pressedButton == button ? 1 : 0;

            // Тінь, а над нею кнопка; натиснута опускається на піксель
            PixelRect(g, Ink, 1, 2, aw - 1, ah - 2);
            DrawPixelPanel(g, ArtRect(0, press, aw - 1, ah - 2), hover ? Wood : PlankLight, Ink, hover ? Gold : Wood, PlankShade);

            string[] icon = pixelButtonIcons[button];
            // Значок у тій самій піксельній сітці, що й решта гри: 1 піксель значка = 1 арт-піксель
            float iconSize = icon[0].Length;
            float iconHeight = icon.Length;
            Font font = button == btnMenu ? shopButtonFont : menuButtonFont;
            SizeF text = g.MeasureString(button.Text, font);
            float textArt = text.Width / Px;
            float content = iconSize + 4 + textArt;
            float left = (float)Math.Round((aw - 1 - content) / 2);
            float iconTop = (float)Math.Round(press + (ah - 2 - iconHeight) / 2);
            PixelSpriteScaled(g, icon, ButtonIconColors, left, iconTop, 1);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawString(button.Text, font, Cream, (left + iconSize + 4) * Px, (press + (ah - 2) / 2) * Px - text.Height / 2);
        }

        private void LayoutMenu()
        {
            // Кнопки меню стовпчиком по центру, під вивіскою; розміри — від розміру панелі
            float scale = pnlMenu.ClientSize.Width / 800f;
            if (scale <= 0)
            {
                return;
            }
            Button[] buttons = { btnPlay, btnJournal, btnShop, btnNewGame, btnExit };
            int width = (int)(232 * scale), height = (int)(44 * scale), gap = (int)(8 * scale);
            int top = (int)(122 * scale);
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Size = new Size(width, height);
                buttons[i].Location = new Point((pnlMenu.ClientSize.Width - width) / 2, top + i * (height + gap));
            }
        }

        private void PnlMenu_Paint(object sender, PaintEventArgs e)
        {
            // Фон — поточна локація, зверху дерев'яна вивіска з назвою гри
            Bitmap background = GetScaledBackground(pnlMenu.ClientSize);
            if (background != null)
            {
                e.Graphics.DrawImageUnscaled(background, 0, 0);
            }
            Graphics g = e.Graphics;
            g.ScaleTransform(DpiScale, DpiScale);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float aw = (float)Math.Floor(pnlMenu.ClientSize.Width / DpiScale / Px);
            float signX = (float)Math.Round(aw / 2 - 70);
            PixelRect(g, Nail, signX + 14, 0, 1, 8);
            PixelRect(g, Nail, signX + 125, 0, 1, 8);
            DrawPixelPanel(g, ArtRect(signX, 8, 140, 44), PlankLight, Ink, Wood, PlankShade);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawString("Fishing", menuTitleFont, Cream, ArtRect(signX, 8, 140, 32), centerFormat);
            g.DrawString("зимова рибалка", shopTextFont, Cream, ArtRect(signX, 36, 140, 12), centerFormat);
        }

        private void Form1_Paint(object sender, PaintEventArgs e)
        {
            // Верхня панель з монетами — дерев'яна, як і решта інтерфейсу
            Graphics g = e.Graphics;
            g.ScaleTransform(DpiScale, DpiScale);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            float width = ClientSize.Width / DpiScale, height = pnlGame.Top / DpiScale;
            DrawPlankWall(g, width, height);
            PixelRect(g, Ink, 0, (float)Math.Floor(height / Px) - 1, (float)Math.Ceiling(width / Px), 1);
        }

        private void StartNewGame()
        {
            DialogResult answer = MessageBox.Show("Почати нову гру? Монети, покупки й журнал буде скинуто.", "Нова гра",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
            {
                return;
            }
            SaveService.DeleteSave();
            player.Money = 0;
            player.Score = 0;
            player.UpgradeLevels.Clear();
            player.DiscoveredFishIds.Clear();
            player.UnlockedLocationIds.Clear();
            foreach (Location location in Catalog.Locations.Where(l => l.Price == 0))
            {
                player.UnlockedLocationIds.Add(location.Id); // безкоштовні локації відкриті завжди
            }
            player.CurrentLocationId = Catalog.Locations.First(l => l.Price == 0).Id;
            catchCounts.Clear();
            bestWeights.Clear();
            ResetGameState();
            UpdateCoinsLabel();
            pnlMenu.Invalidate();
        }

        private void DisposeGameResources()
        {
            tmrCast.Dispose();
            tmrBite.Dispose();
            tmrReaction.Dispose();
            tmrFight.Dispose();
            tmrMessage.Dispose();
            rodPen.Dispose();
            linePen.Dispose();
            alertFont.Dispose();
            statusFont.Dispose();
            resultTitleFont.Dispose();
            resultTextFont.Dispose();
            resultHintFont.Dispose();
            shopNameFont.Dispose();
            shopTextFont.Dispose();
            shopButtonFont.Dispose();
            shopTitleFont.Dispose();
            menuTitleFont.Dispose();
            menuButtonFont.Dispose();
            upgradeNameFont.Dispose();
            upgradeDescFont.Dispose();
            overlayBrush.Dispose();
            centerFormat.Dispose();
            rightFormat.Dispose();
            backgroundCache?.Dispose();
            foreach (Image image in loadedImages)
            {
                image.Dispose();
            }
        }

        private static void EnableDoubleBuffering(Control control)
        {
            typeof(Control)
                .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(control, true, null);
        }

        private static Image LoadEmbeddedImage(string resourcePrefix)
        {
            // Шукаємо вбудований ресурс за початком імені, напр. «Fishing.Assets.Fish.fish_03_»
            Assembly assembly = Assembly.GetExecutingAssembly();
            string name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase));
            if (name == null)
            {
                return null; // графіки немає — гра намалює заглушку
            }
            using (Stream stream = assembly.GetManifestResourceStream(name))
            using (Image image = Image.FromStream(stream))
            {
                // Копія в пам'яті: Image.FromStream вимагає, щоб потік жив увесь час
                return new Bitmap(image);
            }
        }

        private static Image MakeSilhouette(Image source)
        {
            // Той самий спрайт, але суцільно темний: колір замінюємо, прозорість лишаємо
            var silhouette = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            var matrix = new ColorMatrix(new[]
            {
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0, 0 },
                new float[] { 0, 0, 0, 0.85f, 0 },
                new float[] { 0.45f, 0.49f, 0.56f, 0, 1 },
            });
            using (var attributes = new ImageAttributes())
            using (Graphics g = Graphics.FromImage(silhouette))
            {
                attributes.SetColorMatrix(matrix);
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return silhouette;
        }

        private void LoadGameImages()
        {
            foreach (Fish fish in Catalog.AllFish)
            {
                Image sprite = LoadEmbeddedImage($"Fishing.Assets.Fish.fish_{fish.Id:00}_");
                if (sprite != null)
                {
                    fishSprites[fish.Id] = sprite;
                    fishSilhouettes[fish.Id] = MakeSilhouette(sprite);
                    loadedImages.Add(sprite);
                    loadedImages.Add(fishSilhouettes[fish.Id]);
                }
            }

            string[] backgroundNames = { "lake", "river", "sea" }; // Ставок, Річка, Море — у порядку Id локацій
            for (int i = 0; i < backgroundNames.Length; i++)
            {
                Image background = LoadEmbeddedImage($"Fishing.Assets.Backgrounds.bg_{backgroundNames[i]}");
                if (background != null)
                {
                    backgrounds[i + 1] = background;
                    loadedImages.Add(background);
                }
            }
        }

        private Bitmap GetScaledBackground(Size size)
        {
            // Масштабуємо фон під розмір панелі лише один раз для кожної локації
            if (!backgrounds.TryGetValue(player.CurrentLocationId, out Image source) || size.Width <= 0 || size.Height <= 0)
            {
                return null;
            }
            if (backgroundCache != null && backgroundCacheLocationId == player.CurrentLocationId && backgroundCache.Size == size)
            {
                return backgroundCache;
            }
            backgroundCache?.Dispose();
            backgroundCache = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(backgroundCache))
            {
                // Фони — піксель-арт 400x200: збільшуємо без згладжування, щоб пікселі лишались чіткими
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(source, new Rectangle(0, 0, size.Width, size.Height));
            }
            backgroundCacheLocationId = player.CurrentLocationId;
            return backgroundCache;
        }

        private void DrawFish(Graphics g, Fish fish, float centerX, float centerY, float width, bool discovered)
        {
            // Спрайт риби (або силует невідкритої); якщо графіки немає — проста заглушка
            Dictionary<int, Image> set = discovered ? fishSprites : fishSilhouettes;
            if (set.TryGetValue(fish.Id, out Image sprite))
            {
                float height = width * sprite.Height / sprite.Width;
                // Спрайти — піксель-арт: масштабуємо без згладжування, щоб пікселі лишались чіткими
                var previousInterpolation = g.InterpolationMode;
                var previousOffset = g.PixelOffsetMode;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(sprite, new RectangleF(centerX - width / 2, centerY - height / 2, width, height));
                g.InterpolationMode = previousInterpolation;
                g.PixelOffsetMode = previousOffset;
                return;
            }
            DrawFishShape(g, centerX, centerY, width / 120f,
                discovered ? RarityColor(fish.Rarity) : Color.FromArgb(70, 70, 80), discovered);
        }

        private void ShowStatus(string text)
        {
            statusText = text;
            tmrMessage.Stop();
            tmrMessage.Start(); // відлік 3 с починається заново з кожним новим повідомленням
        }

        private void ShowShopMessage(string text)
        {
            shopMessage = text;
            tmrMessage.Stop();
            tmrMessage.Start();
        }

        private void ClearMessages()
        {
            tmrMessage.Stop();
            statusText = "";
            shopMessage = "";
        }

        private void TmrMessage_Tick(object sender, EventArgs e)
        {
            ClearMessages();
            pnlGame.Invalidate();
            pnlShop.Invalidate();
        }

        private void ShowPanel(Panel panel)
        {
            // Повідомлення попереднього екрана не переносяться на новий
            ClearMessages();

            pnlMenu.Visible = false;
            pnlGame.Visible = false;
            pnlJournal.Visible = false;
            pnlShop.Visible = false;
            panel.Visible = true;
            btnMenu.Visible = panel != pnlMenu;
            if (panel == pnlJournal)
            {
                journalLocationId = player.CurrentLocationId; // журнал відкривається на поточній локації
            }
            if (panel == pnlGame)
            {
                pnlGame.BackColor = GameWaterColor(player.CurrentLocationId);
            }
            if (panel == pnlJournal || panel == pnlShop)
            {
                panel.Invalidate();
            }
            ActiveControl = null;
        }

        private void ReturnToMenu()
        {
            ResetGameState();
            ShowPanel(pnlMenu);
        }

        private void ResetGameState()
        {
            // Зупиняємо всі таймери, щоб гра не йшла далі у фоні, поки відкрите меню
            tmrCast.Stop();
            tmrBite.Stop();
            tmrReaction.Stop();
            tmrFight.Stop();

            isCharging = false;
            isFlying = false;
            isReeling = false;
            isBiting = false;
            isFighting = false;
            isHolding = false;
            isShowingResult = false;
            mouseDown = false;
            spaceDown = false;
            statusText = "";
            floatPos = FloatRest;
            floatTarget = FloatRest;
            pnlGame.Invalidate();
        }

        private void UpdateCoinsLabel()
        {
            lblScore.Text = $"Монети: {player.Money}";
        }

        private void PnlGame_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !mouseDown)
            {
                bool wasDown = AnyInputDown;
                mouseDown = true;
                if (!wasDown)
                {
                    HandlePress();
                }
            }
        }

        private void PnlGame_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && mouseDown)
            {
                mouseDown = false;
                if (!AnyInputDown)
                {
                    HandleRelease();
                }
            }
        }

        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
#if DEBUG
            // Лише для тестування в Debug-збірці: F9 додає 500 монет (у Release цього коду немає)
            if (e.KeyCode == Keys.F9)
            {
                e.Handled = true;
                player.Money += 500;
                UpdateCoinsLabel();
                pnlShop.Invalidate();
                return;
            }
#endif
            if (e.KeyCode == Keys.Escape && (pnlGame.Visible || pnlJournal.Visible || pnlShop.Visible))
            {
                e.Handled = true;
                ReturnToMenu();
            }
            else if (e.KeyCode == Keys.Space)
            {
                // Пробіл керує грою лише на ігровому екрані і без Alt/Ctrl/Shift.
                // У меню він, як звично, натискає кнопку, а Alt+Пробіл відкриває системне меню вікна.
                if (!pnlGame.Visible || e.Modifiers != Keys.None)
                {
                    return;
                }

                e.Handled = true;
                e.SuppressKeyPress = true;

                // Автоповтор утриманої клавіші не повинен знову запускати дію
                if (!spaceDown)
                {
                    bool wasDown = AnyInputDown;
                    spaceDown = true;
                    if (!wasDown)
                    {
                        HandlePress();
                    }
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
            if (e.KeyCode == Keys.Space && spaceDown)
            {
                e.Handled = true;
                spaceDown = false;
                if (!AnyInputDown)
                {
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
            mouseDown = false;
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

            // Запізнілий клік одразу після «Риба втекла» не запускає новий закид
            if (sinceReactionTimeout.IsRunning && sinceReactionTimeout.ElapsedMilliseconds < LatePressIgnoreMs)
            {
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
                StartFight();
                pnlGame.Invalidate();
                return;
            }

            // Поки поплавець летить або чекає поклювання, закидати не можна
            if (isFlying || tmrBite.Enabled)
            {
                return;
            }

            // Новий закид під час змотування: поплавець одразу на вудці
            if (isReeling)
            {
                isReeling = false;
                floatPos = FloatRest;
            }

            isCharging = true;
            castPower = 0;
            castDirection = 1;
            statusText = "";
            castClock.Restart();
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

        private void StartCast(float power)
        {
            // Чим сильніше заряд, тим далі летить поплавець
            float distance = CastDistanceFor(power);
            lastCastDistance = distance;
            floatPos = FloatRest;
            castFrom = floatPos;
            floatTarget = new PointF(Math.Min(rodTip.X + distance, LogicalWidth - 16), LogicalHeight - 100);
            isFlying = true;
            castClock.Restart();
            tmrCast.Start();
        }

        private void StartBiteWait()
        {
            // Випадковий час очікування від 2 до 5 секунд, наживка скорочує його на 0,5 с за рівень
            int baitBonusMs = (int)(UpgradeValue(UpgradeType.BiteSpeed) * 1000);
            tmrBite.Interval = Math.Max(500, rng.Next(2000, 5001) - baitBonusMs);
            tmrBite.Start();
        }

        private void StartFight()
        {
            isFighting = true;
            // Кнопка, якою підсікли, може бути ще затиснута: тоді вона одразу керує зоною
            isHolding = AnyInputDown;
            playerPos = 50f;
            fightProgress = 20f;
            fightElapsed = 0;
            fishPos = 50f;

            // Рибу обираємо одразу при підсіченні, щоб міні-гра знала, наскільки вона складна
            hookedFish = PickCaughtFish();
            float difficulty = (float)hookedFish.Difficulty; // 0.1 (легко) .. 1.0 (дуже важко)
            // Що складніша риба, то швидше вона плаває по смузі
            fishSpeed = 0.6f + 0.6f * difficulty;           // карась ~0,7, легендарна ~1,2
            fishJumpScale = 1f - 0.5f * difficulty;          // складні частіше міняють напрям

            PickNewFishTarget();
            ShowStatus("Риба на гачку!");
            fightClock.Restart();
            tmrFight.Start();
        }

        private void PickNewFishTarget()
        {
            // Риба обирає нову точку на смузі і плаває до неї.
            // Складна риба робить різкі ривки: нова ціль далеко від поточної позиції.
            float difficulty = hookedFish == null ? 0f : (float)hookedFish.Difficulty;
            float minJump = 35f * difficulty;
            fishTarget = rng.Next(5, 96);
            for (int attempt = 0; attempt < 5 && Math.Abs(fishTarget - fishPos) < minJump; attempt++)
            {
                fishTarget = rng.Next(5, 96);
            }
            fishRetargetMs = rng.Next(800, 2001) * fishJumpScale;
        }

        private void EndFight(bool caught)
        {
            tmrFight.Stop();
            isFighting = false;
            isHolding = false;
            if (caught)
            {
                // Рибу одразу продаємо: монети нараховує CatchService.Sell
                ShowCatchResult(hookedFish);
                CatchService.Sell(player, lastCatch);
                UpdateCoinsLabel();
                SaveProgress();
            }
            else
            {
                ShowStatus("Риба втекла");
            }
            StartReelIn();
            pnlGame.Invalidate();
        }

        private void StartReelIn()
        {
            // Після вилову чи втечі риби змотуємо волосінь: поплавець повертається до кінчика вудки
            isReeling = true;
            floatTarget = FloatRest;
            castClock.Restart();
            tmrCast.Start();
        }

        private bool IsFishInZone()
        {
            return fishPos >= playerPos - ZoneHalfSize && fishPos <= playerPos + ZoneHalfSize;
        }

        private void TmrBite_Tick(object sender, EventArgs e)
        {
            tmrBite.Stop();
            isBiting = true;
            // Базове вікно 1,5 с, «Швидка реакція» додає 0,25 с за рівень
            tmrReaction.Interval = 1500 + (int)(UpgradeValue(UpgradeType.ReactionWindow) * 1000);
            tmrReaction.Start();
            pnlGame.Invalidate();
        }

        private void TmrReaction_Tick(object sender, EventArgs e)
        {
            // Вікно реакції минуло без кліку
            tmrReaction.Stop();
            isBiting = false;
            ShowStatus("Риба втекла");
            sinceReactionTimeout.Restart();
            StartReelIn();
            pnlGame.Invalidate();
        }

        private void TmrFight_Tick(object sender, EventArgs e)
        {
            // k = 1, якщо з минулого тіку пройшло 16 мс; обмежено, щоб після підвисання не було стрибка
            float dt = (float)fightClock.Elapsed.TotalMilliseconds;
            fightClock.Restart();
            float k = Math.Min(dt / 16f, 4f);

            // Зелена зона піднімається під час утримання і опускається, коли кнопку відпущено
            playerPos += (isHolding ? 1.2f : -0.9f) * k;
            playerPos = Math.Max(ZoneHalfSize, Math.Min(100f - ZoneHalfSize, playerPos));

            // Риба плаває випадково: доходить до цілі і обирає нову
            if (fishRetargetMs <= 0 || Math.Abs(fishTarget - fishPos) < 1f)
            {
                PickNewFishTarget();
            }
            fishRetargetMs -= dt;
            fishPos += Math.Sign(fishTarget - fishPos) * fishSpeed * k;
            fishPos = Math.Max(0f, Math.Min(100f, fishPos));

            // Прогрес росте, поки риба в зеленій зоні, і падає поза нею
            // Складну рибу довше виводити: прогрес росте повільніше і падає швидше.
            // Чим довше триває виважування, тим швидше падає шкала поза зоною:
            // через 9 с — удвічі, через 18 с — утричі. Риба зривається лише тоді,
            // коли шкала опустилася до нуля (без окремого таймера).
            float hardness = (float)hookedFish.Difficulty;
            float pressure = 1f + fightElapsed / PressureGrowthMs;
            fightProgress += (IsFishInZone() ? 0.42f - 0.12f * hardness : -(0.25f + 0.1f * hardness) * pressure) * k;
            fightProgress = Math.Max(0f, Math.Min(100f, fightProgress));

            fightElapsed += dt;

            if (fightProgress >= 100f)
            {
                EndFight(true);
            }
            else if (fightProgress <= 0f)
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
            float dt = (float)castClock.Elapsed.TotalMilliseconds;
            castClock.Restart();
            float k = Math.Min(dt / 16f, 4f);

            if (isCharging)
            {
                // Заряд ходить туди-сюди від 0 до 100
                castPower += castDirection * 3 * k;
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

                float step = 8 * k;
                if (length <= step)
                {
                    floatPos = floatTarget;
                    isFlying = false;
                    tmrCast.Stop();
                    StartBiteWait();
                }
                else
                {
                    floatPos = new PointF(floatPos.X + dx / length * step, floatPos.Y + dy / length * step);
                }
            }
            else if (isReeling)
            {
                // Змотування: поплавець пливе назад до вудки трохи швидше, ніж летів
                float dx = FloatRest.X - floatPos.X;
                float dy = FloatRest.Y - floatPos.Y;
                float length = (float)Math.Sqrt(dx * dx + dy * dy);
                float step = 12 * k;
                if (length <= step)
                {
                    floatPos = FloatRest;
                    isReeling = false;
                    tmrCast.Stop();
                }
                else
                {
                    floatPos = new PointF(floatPos.X + dx / length * step, floatPos.Y + dy / length * step);
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
            // Фон локації: уже масштабований під панель, тому малюється швидко, без перерахунку
            Bitmap background = GetScaledBackground(pnlGame.ClientSize);
            if (background != null)
            {
                e.Graphics.DrawImageUnscaled(background, 0, 0);
            }

            // Масштаб під DPI і згладжування ліній та тексту
            e.Graphics.ScaleTransform(DpiScale, DpiScale);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // Піксельні елементи сцени — без згладжування, по сітці арт-пікселів
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;

            // Маркер падіння під час заряду
            DrawDepthMarkers(e.Graphics);

            // Вудка, волосінь і поплавець
            PointF shownFloat = ShownFloatPosition();
            DrawRod(e.Graphics);
            DrawFishingLine(e.Graphics, shownFloat);
            DrawFloat(e.Graphics, shownFloat);

            // Шкала сили закидання
            if (isCharging)
            {
                DrawPowerBar(e.Graphics);
            }

            // Міні-гра: смуга з зеленою зоною, рибою і шкала прогресу поруч
            if (isFighting)
            {
                DrawFightBar(e.Graphics);
            }

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // Повідомлення про результат — на темній підкладці, щоб читалось на будь-якому фоні
            if (!string.IsNullOrEmpty(statusText))
            {
                DrawTextPanel(e.Graphics, statusText, statusFont, new PointF(16, 14), false);
            }

            // Підказки для новачка — поки не спіймано жодної риби
            if (player.DiscoveredFishIds.Count == 0 && !isShowingResult)
            {
                string hint = isFighting ? "Тримай кнопку — зелена зона йде вгору. Тримай рибу в ній"
                    : isBiting ? "Клює! Швидко клікни або натисни пробіл"
                    : FloatOnWater ? "Чекай, поки клюне…"
                    : isCharging ? "Відпусти — поплавець полетить. Довше тримаєш — далі летить"
                    : isFlying || isReeling ? null
                    : "Затисни ЛКМ або пробіл, щоб зарядити закид";
                if (hint != null)
                {
                    SizeF size = e.Graphics.MeasureString(hint, shopButtonFont);
                    DrawTextPanel(e.Graphics, hint, shopButtonFont, new PointF((LogicalWidth - size.Width - 16) / 2, 52), false);
                }
            }

            // Назва поточної локації у правому верхньому куті поля
            DrawTextPanel(e.Graphics, Catalog.GetLocation(player.CurrentLocationId).Name, shopNameFont,
                new PointF(LogicalWidth - 16, 14), true);

            // Екран результату поверх гри
            if (isShowingResult && lastCatch != null)
            {
                DrawResultCard(e.Graphics);
            }
        }

        // Наживка для вибору риби (модель бекенду Bait). Поки в магазині наживок немає, тому null —
        // FishPicker тоді бере базові шанси рідкостей.
        private Bait currentBait = null;

        // Дальність останнього закиду в логічних пікселях від кінчика вудки
        private float lastCastDistance;

        private static float RequiredCastDistance(Rarity rarity)
        {
            // Далі — глибше, а глибше водиться рідкісніша риба.
            // Максимальний закид без прокачки — 550, тож легендарна потребує майже повного заряду,
            // а «Довга волосінь» (+50 за рівень) робить її досяжнішою.
            switch (rarity)
            {
                case Rarity.Common: return 0;
                case Rarity.Uncommon: return 250;
                case Rarity.Rare: return 350;
                case Rarity.Epic: return 450;
                default: return 540;
            }
        }

        private static float MaxCastDistance(Rarity rarity)
        {
            // Найдешевша риба тримається біля берега: далі 400 звичайна вже не клює
            return rarity == Rarity.Common ? 400 : float.MaxValue;
        }

        private static bool IsReachable(Fish fish, float distance)
        {
            return distance >= RequiredCastDistance(fish.Rarity) && distance < MaxCastDistance(fish.Rarity);
        }

        private Fish PickCaughtFish()
        {
            // Вибір риби — логіка бекенду (картка 21): поточна локація гравця, рідкість і наживка.
            // Дальність закиду лише відкриває рибу: досяжна риба клює з тим самим базовим шансом,
            // що й з самого початку, без жодного бонусу за відстань.
            Location location = Catalog.GetLocation(player.CurrentLocationId);
            var reachable = new Location { Id = location.Id, Name = location.Name, Price = location.Price };
            reachable.Fishes.AddRange(location.Fishes.Where(f => IsReachable(f, lastCastDistance)));
            if (reachable.Fishes.Count == 0)
            {
                reachable.Fishes.Add(location.Fishes[0]);
            }
            return FishPicker.Pick(reachable, currentBait, rng) ?? location.Fishes[0];
        }

        private float CastDistanceFor(float power)
        {
            // Чим сильніше заряд, тим далі летить поплавець; «Довга волосінь» додає дальність
            return 150 + power * 4 + (float)UpgradeValue(UpgradeType.CastRange);
        }

        // ---------- Піксель-арт сцени (картка 27): 1 арт-піксель = 2 логічні пікселі, як у фонах 400x200
        private const float Px = 2f;

        private static readonly SolidBrush RodDark = new SolidBrush(Color.FromArgb(79, 49, 37));
        private static readonly SolidBrush RodLight = new SolidBrush(Color.FromArgb(168, 112, 63));
        private static readonly SolidBrush Cork = new SolidBrush(Color.FromArgb(226, 200, 160));
        private static readonly SolidBrush MetalDark = new SolidBrush(Color.FromArgb(84, 91, 107));
        private static readonly SolidBrush MetalLight = new SolidBrush(Color.FromArgb(167, 176, 189));
        private static readonly SolidBrush LineBrush = new SolidBrush(Color.FromArgb(190, 228, 234, 242));
        private static readonly SolidBrush RippleBrush = new SolidBrush(Color.FromArgb(170, 225, 238, 248));
        private static readonly SolidBrush FloatRed = new SolidBrush(Color.FromArgb(228, 59, 68));
        private static readonly SolidBrush FloatRedLight = new SolidBrush(Color.FromArgb(246, 117, 122));
        private static readonly SolidBrush FloatWhite = new SolidBrush(Color.White);
        private static readonly SolidBrush Ink = new SolidBrush(Color.FromArgb(24, 20, 37));
        private static readonly SolidBrush PanelFill = new SolidBrush(Color.FromArgb(205, 24, 20, 37));
        private static readonly SolidBrush PanelEdge = new SolidBrush(Color.FromArgb(139, 155, 180));
        private static readonly SolidBrush Parchment = new SolidBrush(Color.FromArgb(234, 212, 170));
        private static readonly SolidBrush ParchmentDark = new SolidBrush(Color.FromArgb(228, 166, 114));
        private static readonly SolidBrush Wood = new SolidBrush(Color.FromArgb(184, 111, 80));
        private static readonly SolidBrush WoodDark = new SolidBrush(Color.FromArgb(115, 62, 57));
        private static readonly SolidBrush Track = new SolidBrush(Color.FromArgb(18, 78, 137));
        private static readonly SolidBrush TrackDark = new SolidBrush(Color.FromArgb(38, 43, 68));
        private static readonly SolidBrush Zone = new SolidBrush(Color.FromArgb(99, 199, 77));
        private static readonly SolidBrush ZoneLight = new SolidBrush(Color.FromArgb(190, 240, 150));
        private static readonly SolidBrush ZoneDark = new SolidBrush(Color.FromArgb(62, 137, 72));
        private static readonly SolidBrush Marker = new SolidBrush(Color.FromArgb(247, 118, 34));
        private static readonly SolidBrush Gold = new SolidBrush(Color.FromArgb(254, 231, 97));
        private static readonly SolidBrush GoldDark = new SolidBrush(Color.FromArgb(254, 174, 52));

        private static void PixelRect(Graphics g, Brush brush, float ax, float ay, float aw = 1, float ah = 1)
        {
            g.FillRectangle(brush, ax * Px, ay * Px, aw * Px, ah * Px);
        }

        private static void PixelLine(Graphics g, Brush brush, float x0, float y0, float x1, float y1)
        {
            // Лінія по сітці арт-пікселів: крок по довшій осі
            int steps = (int)Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            for (int i = 0; i <= steps; i++)
            {
                float t = steps == 0 ? 0 : (float)i / steps;
                PixelRect(g, brush, (float)Math.Round(x0 + (x1 - x0) * t), (float)Math.Round(y0 + (y1 - y0) * t));
            }
        }

        private static void PixelSprite(Graphics g, string[] rows, Dictionary<char, Brush> colors, float ax, float ay)
        {
            for (int y = 0; y < rows.Length; y++)
            {
                for (int x = 0; x < rows[y].Length; x++)
                {
                    if (colors.TryGetValue(rows[y][x], out Brush brush))
                    {
                        PixelRect(g, brush, ax + x, ay + y);
                    }
                }
            }
        }

        private static void DrawPixelPanel(Graphics g, RectangleF rect, Brush fill, Brush edge, Brush light, Brush shade)
        {
            // Панель з рамкою в 1 арт-піксель: обвідка, світлий верх-ліво, тінь знизу-праворуч
            float ax = (float)Math.Floor(rect.X / Px), ay = (float)Math.Floor(rect.Y / Px);
            float aw = (float)Math.Ceiling(rect.Width / Px), ah = (float)Math.Ceiling(rect.Height / Px);
            PixelRect(g, edge, ax + 1, ay, aw - 2, 1);
            PixelRect(g, edge, ax + 1, ay + ah - 1, aw - 2, 1);
            PixelRect(g, edge, ax, ay + 1, 1, ah - 2);
            PixelRect(g, edge, ax + aw - 1, ay + 1, 1, ah - 2);
            PixelRect(g, fill, ax + 1, ay + 1, aw - 2, ah - 2);
            if (light != null)
            {
                PixelRect(g, light, ax + 1, ay + 1, aw - 2, 1);
                PixelRect(g, light, ax + 1, ay + 2, 1, ah - 4);
            }
            if (shade != null)
            {
                PixelRect(g, shade, ax + 1, ay + ah - 2, aw - 2, 1);
                PixelRect(g, shade, ax + aw - 2, ay + 2, 1, ah - 4);
            }
        }

        private void DrawTextPanel(Graphics g, string text, Font font, PointF anchor, bool alignRight)
        {
            // Текст на темній піксельній підкладці: читається і вдень, і вночі
            SizeF size = g.MeasureString(text, font);
            float width = (float)Math.Ceiling((size.Width + 16) / Px) * Px;
            float height = (float)Math.Ceiling((size.Height + 6) / Px) * Px;
            float x = alignRight ? anchor.X - width : anchor.X;
            var rect = new RectangleF((float)Math.Round(x / Px) * Px, anchor.Y, width, height);
            var previous = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            DrawPixelPanel(g, rect, PanelFill, PanelEdge, null, null);
            g.SmoothingMode = previous;
            g.DrawString(text, font, Brushes.White, rect.X + 8, rect.Y + 3);
        }

        private void DrawRod(Graphics g)
        {
            // Вудлище з двох тонів, коркова ручка, котушка і підставка на помості
            float tipX = rodTip.X / Px, tipY = rodTip.Y / Px;
            PixelRect(g, MetalDark, 13, 168, 6, 2);                 // підставка
            PixelLine(g, RodDark, 15, 169, tipX - 1, tipY);
            PixelLine(g, RodLight, 16, 169, tipX, tipY);
            PixelRect(g, Cork, 13, 160, 2, 7);                      // корок
            PixelRect(g, RodDark, 15, 160, 1, 7);
            PixelRect(g, MetalDark, 17, 161, 3, 3);                 // котушка
            PixelRect(g, MetalLight, 17, 161, 1, 1);
            PixelRect(g, MetalLight, tipX, tipY - 1, 1, 1);          // кільце на кінчику
        }

        private bool FloatOnWater => !isFlying && !isReeling && (tmrBite.Enabled || isBiting || isFighting);

        private PointF ShownFloatPosition()
        {
            // Під час польоту поплавець летить дугою, а не по прямій
            if (!isFlying)
            {
                return floatPos;
            }
            float total = Distance(castFrom, floatTarget);
            float t = total > 0 ? Math.Min(1f, Distance(castFrom, floatPos) / total) : 1f;
            float height = Math.Min(90f, total * 0.3f);
            return new PointF(floatPos.X, floatPos.Y - (float)Math.Sin(Math.PI * t) * height);
        }

        private static float Distance(PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private void DrawFishingLine(Graphics g, PointF shownFloat)
        {
            // Волосінь: провисає, поки поплавець лежить на воді, натягнута під час виважування
            float x0 = rodTip.X / Px, y0 = rodTip.Y / Px;
            float x1 = (float)Math.Round(shownFloat.X / Px), y1 = (float)Math.Round(shownFloat.Y / Px) - 3;
            float sag = FloatOnWater && !isFighting ? 8f : 0f;
            int steps = (int)Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) + 1;
            float lastX = float.NaN, lastY = float.NaN;
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float x = (float)Math.Round(x0 + (x1 - x0) * t);
                float y = (float)Math.Round(y0 + (y1 - y0) * t + sag * 4 * t * (1 - t));
                if (x != lastX || y != lastY)
                {
                    PixelRect(g, LineBrush, x, y);
                    lastX = x; lastY = y;
                }
            }
        }

        private static readonly Dictionary<char, Brush> FloatColors = new Dictionary<char, Brush>
        {
            { 'k', Ink }, { 'r', FloatRed }, { 'h', FloatRedLight }, { 'w', FloatWhite },
        };

        private void DrawFloat(Graphics g, PointF shownFloat)
        {
            float ax = (float)Math.Round(shownFloat.X / Px), ay = (float)Math.Round(shownFloat.Y / Px);
            if (isFighting)
            {
                // Поплавець під водою: видно лише брижі там, де волосінь входить у воду
                PixelRect(g, RippleBrush, ax - 3, ay, 2, 1);
                PixelRect(g, RippleBrush, ax + 2, ay, 2, 1);
                PixelRect(g, RippleBrush, ax - 1, ay + 1, 3, 1);
                return;
            }
            if (FloatOnWater)
            {
                // Нижня половина під водою, навколо кола; під час поклювання поплавець пірнає глибше
                string[] sprite = isBiting
                    ? new[] { ".k.", "hrr" }
                    : new[] { ".k.", "hrr", "rrr" };
                PixelSprite(g, sprite, FloatColors, ax - 1, ay - sprite.Length + 1);
                int ring = isBiting ? 6 : 4;
                PixelRect(g, RippleBrush, ax - ring, ay + 1, 2, 1);
                PixelRect(g, RippleBrush, ax + ring - 1, ay + 1, 2, 1);
                if (isBiting)
                {
                    PixelRect(g, RippleBrush, ax - 3, ay + 2, 7, 1);
                    DrawBiteAlert(g, ax, ay - 19);
                }
                return;
            }
            // У повітрі: увесь поплавець, червоний верх і білий низ
            PixelSprite(g, new[] { ".k.", "hrr", "rrr", "www", "www", ".w." }, FloatColors, ax - 1, ay - 3);
        }

        private static readonly Dictionary<char, Brush> AlertColors = new Dictionary<char, Brush>
        {
            { 'k', Ink }, { 'w', FloatWhite }, { 'r', FloatRed },
        };

        private static void DrawBiteAlert(Graphics g, float ax, float ay)
        {
            // Бульбашка зі знаком оклику над поплавцем
            PixelSprite(g, new[]
            {
                ".kkkkkkkk.",
                "kwwwwwwwwk",
                "kwwwrrwwwk",
                "kwwwrrwwwk",
                "kwwwrrwwwk",
                "kwwwrrwwwk",
                "kwwwrrwwwk",
                "kwwwwwwwwk",
                "kwwwrrwwwk",
                "kwwwwwwwwk",
                ".kkkkkkkk.",
                "....kwk...",
                ".....k....",
            }, AlertColors, ax - 4, ay);
        }

        private void DrawPowerBar(Graphics g)
        {
            // Шкала сили закидання над помостом: зелена → жовта → червона
            const float left = 26, top = 160, width = 52;
            DrawPixelPanel(g, new RectangleF(left * Px, top * Px, (width + 4) * Px, 6 * Px), TrackDark, Ink, null, null);
            float fill = (float)Math.Round(castPower / 100f * width);
            Brush color = castPower < 40 ? Zone : castPower < 75 ? (Brush)ParchmentDark : FloatRed;
            if (fill > 0)
            {
                PixelRect(g, color, left + 2, top + 2, fill, 2);
                PixelRect(g, FloatWhite, left + 2, top + 2, fill, 1);
                PixelRect(g, color, left + 2, top + 2, Math.Max(0, fill - 1), 1);
            }
        }

        private static readonly Dictionary<char, Brush> MarkerColors = new Dictionary<char, Brush>
        {
            { 'k', Ink }, { 'o', Marker },
        };

        private void DrawDepthMarkers(Graphics g)
        {
            // Під час заряду: піксельна стрілка над місцем, куди впаде поплавець
            if (!isCharging)
            {
                return;
            }
            float landX = Math.Min(rodTip.X + CastDistanceFor(castPower), LogicalWidth - 16);
            float ax = (float)Math.Round(landX / Px), ay = (LogicalHeight - 100) / Px - 9;
            PixelSprite(g, new[] { "kkkkkkk", "koooook", ".koook.", "..kok..", "...k..." },
                MarkerColors, ax - 3, ay);
        }

        private static string FormatWeight(double kg)
        {
            // Хамса важить грами, мегалодон — тонни: точність залежить від ваги
            if (kg < 1) return $"{kg:0.000} кг";
            if (kg < 100) return $"{kg:0.0} кг";
            return $"{kg:0} кг";
        }

        private void ShowCatchResult(Fish fish)
        {
            // Обробка улову — логіка бекенду (картка 22): вага, ціна з бонусом «Торговця»,
            // відкриття виду в журналі гравця
            double weight = CatchService.RollWeight(fish, rng);
            lastCatch = CatchService.ProcessCatch(player, fish, weight, UpgradeValue(UpgradeType.SellBonus));

            // Статистика для карток журналу (кількість і рекорд); рекорд рахується, якщо вид уже ловили
            catchCounts[fish.Id] = (catchCounts.TryGetValue(fish.Id, out int count) ? count : 0) + 1;
            bool hadBest = bestWeights.TryGetValue(fish.Id, out double best);
            lastIsRecord = hadBest && weight > best;
            if (!hadBest || weight > best)
            {
                bestWeights[fish.Id] = weight;
            }
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
            var previousSmoothing = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            DrawPixelPanel(g, new RectangleF(cardLeft, cardTop, cardWidth, cardHeight), Parchment, WoodDark, FloatWhite, ParchmentDark);
            g.SmoothingMode = previousSmoothing;

            Color rarityColor = RarityColor(lastCatch.Fish.Rarity);

            // Заголовок
            g.DrawString("Зловлено!", resultTitleFont, Brushes.Black,
                new RectangleF(cardLeft, cardTop + 10, cardWidth, 36), centerFormat);

            // Спрайт спійманої риби
            float fishX = cardLeft + cardWidth / 2;
            float fishY = cardTop + 70;
            DrawFish(g, lastCatch.Fish, fishX, fishY, 116, true);

            // Назва, рідкість, вага, ціна
            using (var rarityBrush = new SolidBrush(rarityColor))
            {
                g.DrawString(lastCatch.Fish.Name, resultTitleFont, rarityBrush,
                    new RectangleF(cardLeft, cardTop + 98, cardWidth, 34), centerFormat);
                g.DrawString(RarityName(lastCatch.Fish.Rarity), resultTextFont, rarityBrush,
                    new RectangleF(cardLeft, cardTop + 130, cardWidth, 24), centerFormat);
            }
            g.DrawString($"Вага: {FormatWeight(lastCatch.Weight)}", resultTextFont, Brushes.Black,
                new RectangleF(cardLeft, cardTop + 156, cardWidth, 24), centerFormat);
            g.DrawString($"Ціна: {lastCatch.Price} {CoinsWord(lastCatch.Price)}", resultTextFont, Brushes.Black,
                new RectangleF(cardLeft, cardTop + 182, cardWidth, 24), centerFormat);

            // Позначка нового виду в правому верхньому куті картки
            if (lastCatch.IsNewSpecies)
            {
                var badge = new RectangleF(cardLeft + cardWidth - 128, cardTop - 14, 140, 30);
                var previousBadge = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                DrawPixelPanel(g, badge, Gold, WoodDark, FloatWhite, GoldDark);
                g.SmoothingMode = previousBadge;
                g.DrawString("Новий вид!", statusFont, Brushes.Black, badge, centerFormat);
            }

            // Позначка рекорду в лівому верхньому куті картки
            if (lastIsRecord)
            {
                var recordBadge = new RectangleF(cardLeft - 12, cardTop - 14, 150, 30);
                var previousRecord = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                DrawPixelPanel(g, recordBadge, Zone, WoodDark, ZoneLight, ZoneDark);
                g.SmoothingMode = previousRecord;
                g.DrawString("Новий рекорд!", statusFont, Brushes.White, recordBadge, centerFormat);
            }

            // Підказка
            g.DrawString("ЛКМ або пробіл — продовжити", resultHintFont, Brushes.DimGray,
                new RectangleF(cardLeft, cardTop + cardHeight - 30, cardWidth, 22), centerFormat);
        }

        private static void DrawFishShape(Graphics g, float centerX, float centerY, float scale, Color color, bool withEye)
        {
            // Запасна риба-заглушка, якщо спрайт не завантажився
            using (var brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, centerX - 35 * scale, centerY - 15 * scale, 60 * scale, 30 * scale);
                g.FillPolygon(brush, new[]
                {
                    new PointF(centerX + 20 * scale, centerY),
                    new PointF(centerX + 42 * scale, centerY - 14 * scale),
                    new PointF(centerX + 42 * scale, centerY + 14 * scale),
                });
            }
            if (withEye)
            {
                g.FillEllipse(Brushes.White, centerX - 25 * scale, centerY - 6 * scale, 7 * scale, 7 * scale);
            }
        }

        private void PnlJournal_MouseClick(object sender, MouseEventArgs e)
        {
            var point = new PointF(e.X / DpiScale, e.Y / DpiScale);
            foreach (var tab in journalTabs)
            {
                if (tab.Rect.Contains(point))
                {
                    journalLocationId = tab.LocationId;
                    pnlJournal.Invalidate();
                    return;
                }
            }
        }

        private static readonly SolidBrush ParchmentShade = new SolidBrush(Color.FromArgb(206, 190, 160));
        private static readonly SolidBrush StatLine = new SolidBrush(Color.FromArgb(190, 160, 120));

        private void PnlJournal_Paint(object sender, PaintEventArgs e)
        {
            // Журнал (статистика улову) у тому ж стилі, що й крамниця: стіна з дощок, вивіска, картки-пергаменти
            Graphics g = e.Graphics;
            g.ScaleTransform(DpiScale, DpiScale);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float width = pnlJournal.ClientSize.Width / DpiScale;
            float height = pnlJournal.ClientSize.Height / DpiScale;
            float aw = (float)Math.Floor(width / Px);
            DrawPlankWall(g, width, height);

            // Вивіска і лічильник відкритих видів з усього каталогу
            float signX = (float)Math.Round(aw / 2 - 55);
            PixelRect(g, Nail, signX + 10, 0, 1, 3);
            PixelRect(g, Nail, signX + 99, 0, 1, 3);
            DrawPixelPanel(g, ArtRect(signX, 3, 110, 16), PlankLight, Ink, Wood, PlankShade);
            g.DrawString("Журнал риб", shopTitleFont, Cream, ArtRect(signX, 3, 110, 16), centerFormat);
            g.DrawString(JournalService.GetProgressText(player), shopTextFont, Cream, new RectangleF(0, 20 * Px, width, 18), centerFormat);

            // Вкладки локацій — дерев'яні таблички, обрана — пергамент
            journalTabs.Clear();
            const float tabW = 82, tabH = 15, tabGap = 6, tabTop = 31;
            int count = Catalog.Locations.Count;
            float tabsLeft = (float)Math.Round((aw - (count * tabW + (count - 1) * tabGap)) / 2);
            for (int i = 0; i < count; i++)
            {
                Location location = Catalog.Locations[i];
                RectangleF rect = ArtRect(tabsLeft + i * (tabW + tabGap), tabTop, tabW, tabH);
                bool active = location.Id == journalLocationId;
                bool unlocked = ShopService.IsLocationUnlocked(player, location);
                int found = JournalService.GetEntries(player, location.Id).Count(entry => entry.IsDiscovered);

                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                if (active)
                {
                    DrawPixelPanel(g, rect, Parchment, Ink, FloatWhite, ParchmentDark);
                }
                else
                {
                    DrawPixelPanel(g, rect, PlankLight, Ink, Wood, PlankShade);
                }
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                string caption = unlocked ? $"{location.Name}  {found}/{location.Fishes.Count}" : $"{location.Name} (закрито)";
                g.DrawString(caption, shopButtonFont, active ? (Brush)CardText : Cream, rect, centerFormat);
                journalTabs.Add((rect, location.Id));
            }

            // 5 карток видів обраної локації
            Location shown = Catalog.GetLocation(journalLocationId);
            List<JournalEntry> entries = JournalService.GetEntries(player, shown.Id);
            const float cardW = 70, cardH = 110, gap = 6, cardsTop = 52;
            float cardsLeft = (float)Math.Round((aw - (entries.Count * cardW + (entries.Count - 1) * gap)) / 2);
            for (int i = 0; i < entries.Count; i++)
            {
                DrawJournalCard(g, entries[i], cardsLeft + i * (cardW + gap), cardsTop, cardW, cardH);
            }

            // Підказка для закритої локації
            if (!ShopService.IsLocationUnlocked(player, shown))
            {
                g.DrawString($"Локацію «{shown.Name}» можна відкрити в магазині за {shown.Price} {CoinsWord(shown.Price)}",
                    resultHintFont, Cream, new RectangleF(0, (cardsTop + cardH + 4) * Px, width, 22), centerFormat);
            }
        }

        private void DrawJournalCard(Graphics g, JournalEntry entry, float ax, float ay, float aw, float ah)
        {
            Fish fish = entry.Fish;
            bool discovered = entry.IsDiscovered;

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            DrawPixelPanel(g, ArtRect(ax, ay, aw, ah), discovered ? Parchment : ParchmentShade, WoodDark, FloatWhite, discovered ? ParchmentDark : StatLine);

            // Спрайт 64x32 малюємо рівно 1:1 до арт-пікселів фону (128 логічних пікселів)
            float centerX = (ax + aw / 2) * Px;
            float fishY = (ay + 20) * Px;
            DrawFish(g, fish, centerX, fishY, 128, discovered);
            PixelRect(g, StatLine, ax + 4, ay + 38, aw - 8, 1);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            if (!discovered)
            {
                // Невідкритий вид: силует, знак питання, «???»
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                PixelSprite(g, new[] { ".kkkkk.", "kwwwwwk", "kwkkkwk", "kwwwkwk", "kwwkwwk", "kwwwwwk", "kwwkwwk", ".kkkkk." },
                    AlertColors, ax + aw - 12, ay + 4);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.DrawString(entry.DisplayName, resultTitleFont, CardSubText, ArtRect(ax, ay + 42, aw, 16), centerFormat);
                g.DrawString("Ще не спіймано", resultHintFont, CardSubText, ArtRect(ax, ay + 62, aw, 10), centerFormat);
                return;
            }

            using (var rarityBrush = new SolidBrush(RarityColor(fish.Rarity)))
            {
                g.DrawString(fish.Name, shopNameFont, rarityBrush, ArtRect(ax, ay + 40, aw, 12), centerFormat);
                g.DrawString(RarityName(fish.Rarity), upgradeDescFont, rarityBrush, ArtRect(ax, ay + 52, aw, 8), centerFormat);
            }
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            PixelRect(g, StatLine, ax + 4, ay + 62, aw - 8, 1);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int caught = catchCounts.TryGetValue(fish.Id, out int c) ? c : 0;
            double best = bestWeights.TryGetValue(fish.Id, out double w) ? w : 0;
            g.DrawString($"Спіймано: {caught}", shopTextFont, CardText, ArtRect(ax, ay + 65, aw, 9), centerFormat);
            g.DrawString($"Рекорд: {FormatWeight(best)}", shopTextFont, CardText, ArtRect(ax, ay + 75, aw, 9), centerFormat);
            g.DrawString($"Ціна: від {fish.BasePrice}", shopTextFont, CardSubText, ArtRect(ax, ay + 85, aw, 9), centerFormat);
        }

        private int UpgradePrice(Upgrade upgrade)
        {
            // Ціна наступного рівня з ShopService: 100, 200, 400; -1, якщо вже максимум
            return ShopService.GetUpgradePrice(player, upgrade);
        }

        private int UpgradeLevel(Upgrade upgrade)
        {
            return ShopService.GetUpgradeLevel(player, upgrade.Id);
        }

        private double UpgradeValue(UpgradeType type)
        {
            // Ефект покращення = куплений рівень × приріст за рівень (ShopService, картка 24)
            return ShopService.GetUpgradeValue(player, type);
        }

        private void BuyLocation(Location location)
        {
            // Купівля з перевіркою балансу — ShopService; повідомлення для гравця теж звідти
            PurchaseResult result = ShopService.BuyLocation(player, location.Id);
            ShowShopMessage(result.Message);
            if (result.Success)
            {
                player.CurrentLocationId = location.Id; // одразу переходимо на куплену локацію
                UpdateCoinsLabel();
                SaveProgress();
            }
        }

        private void SelectLocation(Location location)
        {
            player.CurrentLocationId = location.Id;
            ShowShopMessage($"Поточна локація: {location.Name}");
            SaveProgress();
        }

        private void BuyUpgrade(Upgrade upgrade)
        {
            PurchaseResult result = ShopService.BuyUpgrade(player, upgrade.Id);
            ShowShopMessage(result.Message);
            if (result.Success)
            {
                UpdateCoinsLabel();
                SaveProgress();
            }
        }

        private void PnlShop_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            // Клік у пікселях екрана переводимо в логічні пікселі, в яких намальовано кнопки
            var point = new PointF(e.X / DpiScale, e.Y / DpiScale);
            foreach (var button in shopButtons)
            {
                if (button.Rect.Contains(point))
                {
                    // Навіть для неактивної кнопки викликаємо купівлю: ShopService поверне причину відмови
                    button.OnClick();
                    pnlShop.Invalidate();
                    return;
                }
            }
        }

        private static Color GameWaterColor(int locationId)
        {
            // Запасний колір води, якщо фон локації не завантажився
            switch (locationId)
            {
                case 1: return Color.LightBlue;
                case 2: return Color.FromArgb(150, 205, 215);
                default: return Color.FromArgb(120, 170, 225);
            }
        }

        private static Color LocationColor(int locationId)
        {
            // Кольори локацій: вкладки журналу і запасна смужка в магазині
            switch (locationId)
            {
                case 1: return Color.FromArgb(90, 165, 120);
                case 2: return Color.FromArgb(60, 150, 180);
                default: return Color.FromArgb(40, 90, 170);
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawCard(Graphics g, RectangleF card, Color border)
        {
            // Картка: м'яка тінь, біле тло, заокруглені кути
            using (var shadowPath = RoundedRect(new RectangleF(card.X + 2, card.Y + 3, card.Width, card.Height), 12))
            using (var shadow = new SolidBrush(Color.FromArgb(28, 20, 40, 70)))
            {
                g.FillPath(shadow, shadowPath);
            }
            using (var path = RoundedRect(card, 12))
            using (var borderPen = new Pen(border, 1.5f))
            {
                g.FillPath(Brushes.White, path);
                g.DrawPath(borderPen, path);
            }
        }

        private enum ShopButtonStyle { Buy, Select, Disabled, Current, Maxed }

        // ---------- Піксельна крамниця (картка 27): стіна з дощок, вивіска, картки-пергаменти
        private static readonly SolidBrush Plank = new SolidBrush(Color.FromArgb(122, 72, 48));
        private static readonly SolidBrush PlankLight = new SolidBrush(Color.FromArgb(140, 85, 56));
        private static readonly SolidBrush PlankShade = new SolidBrush(Color.FromArgb(78, 44, 32));
        private static readonly SolidBrush Nail = new SolidBrush(Color.FromArgb(60, 60, 70));
        private static readonly SolidBrush BtnGreen = new SolidBrush(Color.FromArgb(62, 137, 72));
        private static readonly SolidBrush BtnGreenDark = new SolidBrush(Color.FromArgb(38, 92, 66));
        private static readonly SolidBrush BtnGrey = new SolidBrush(Color.FromArgb(90, 96, 110));
        private static readonly SolidBrush BtnGreyDark = new SolidBrush(Color.FromArgb(58, 68, 102));
        private static readonly SolidBrush BtnOrangeDark = new SolidBrush(Color.FromArgb(190, 110, 30));
        private static readonly SolidBrush CoinFill = new SolidBrush(Color.FromArgb(247, 180, 40));
        private static readonly SolidBrush Cream = new SolidBrush(Color.FromArgb(255, 236, 190));
        private static readonly SolidBrush CardText = new SolidBrush(Color.FromArgb(60, 36, 30));
        private static readonly SolidBrush CardSubText = new SolidBrush(Color.FromArgb(110, 80, 60));
        private static readonly SolidBrush LockShade = new SolidBrush(Color.FromArgb(150, 24, 20, 37));

        private static RectangleF ArtRect(float ax, float ay, float aw, float ah)
        {
            return new RectangleF(ax * Px, ay * Px, aw * Px, ah * Px);
        }

        private static void DrawPixelCoin(Graphics g, float ax, float ay)
        {
            PixelRect(g, BtnOrangeDark, ax + 1, ay, 3, 1);
            PixelRect(g, CoinFill, ax, ay + 1, 5, 3);
            PixelRect(g, BtnOrangeDark, ax + 1, ay + 4, 3, 1);
            PixelRect(g, BtnOrangeDark, ax + 2, ay + 1, 1, 3);
            PixelRect(g, Gold, ax + 1, ay + 1, 1, 1);
        }

        private void DrawPlankWall(Graphics g, float width, float height)
        {
            // Стіна крамниці: горизонтальні дошки зі швами вперемішку і цвяхами
            float aw = (float)Math.Ceiling(width / Px), ah = (float)Math.Ceiling(height / Px);
            PixelRect(g, Plank, 0, 0, aw, ah);
            for (int y = 0, row = 0; y < ah; y += 8, row++)
            {
                PixelRect(g, PlankShade, 0, y, aw, 1);
                PixelRect(g, PlankLight, 0, y + 1, aw, 1);
                for (float x = (row % 2) * 23; x < aw; x += 46)
                {
                    PixelRect(g, PlankShade, x, y + 1, 1, 7);
                    PixelRect(g, Nail, x + 3, y + 4, 1, 1);
                }
            }
        }

        private void DrawShopButton(Graphics g, RectangleF rect, string text, ShopButtonStyle style, bool withCoin)
        {
            Brush face, light, dark;
            switch (style)
            {
                case ShopButtonStyle.Buy: face = BtnGreen; light = Zone; dark = BtnGreenDark; break;
                case ShopButtonStyle.Select:
                case ShopButtonStyle.Maxed: face = Marker; light = Gold; dark = BtnOrangeDark; break;
                case ShopButtonStyle.Current: face = TrackDark; light = PanelEdge; dark = Ink; break;
                default: face = BtnGrey; light = PanelEdge; dark = BtnGreyDark; break;
            }
            // Підсвітка при наведенні на активну кнопку
            bool active = style == ShopButtonStyle.Buy || style == ShopButtonStyle.Select;
            if (active && hoveredShopRect.HasValue && hoveredShopRect.Value == rect)
            {
                face = light;
            }

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            DrawPixelPanel(g, rect, face, Ink, light, dark);

            // Монетка перед ціною або галочка для поточної локації
            SizeF size = g.MeasureString(text, shopButtonFont);
            float iconWidth = withCoin ? 7 * Px : style == ShopButtonStyle.Current ? 7 * Px : 0;
            float x = rect.X + (rect.Width - size.Width - iconWidth) / 2;
            float midY = rect.Y + rect.Height / 2;
            float ax = (float)Math.Round(x / Px), ay = (float)Math.Round(midY / Px) - 2;
            if (withCoin)
            {
                DrawPixelCoin(g, ax, ay);
            }
            else if (style == ShopButtonStyle.Current)
            {
                PixelSprite(g, new[] { "....w", "w..w.", ".ww.." }, AlertColors, ax, ay + 1);
            }
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawString(text, shopButtonFont, Brushes.White, x + iconWidth, midY - size.Height / 2);
        }

        private void PnlShop_MouseMove(object sender, MouseEventArgs e)
        {
            // Підсвічуємо кнопку під курсором
            var point = new PointF(e.X / DpiScale, e.Y / DpiScale);
            RectangleF? hovered = null;
            foreach (var button in shopButtons)
            {
                if (button.Enabled && button.Rect.Contains(point))
                {
                    hovered = button.Rect;
                    break;
                }
            }
            if (hovered != hoveredShopRect)
            {
                hoveredShopRect = hovered;
                pnlShop.Cursor = hovered.HasValue ? Cursors.Hand : Cursors.Default;
                pnlShop.Invalidate();
            }
        }

        private void PnlShop_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.ScaleTransform(DpiScale, DpiScale);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float width = pnlShop.ClientSize.Width / DpiScale;
            float height = pnlShop.ClientSize.Height / DpiScale;
            float aw = (float)Math.Floor(width / Px);
            shopButtons.Clear();

            DrawPlankWall(g, width, height);

            // Вивіска на двох цвяхах
            float signX = (float)Math.Round(aw / 2 - 50);
            PixelRect(g, Nail, signX + 10, 0, 1, 3);
            PixelRect(g, Nail, signX + 89, 0, 1, 3);
            DrawPixelPanel(g, ArtRect(signX, 3, 100, 16), PlankLight, Ink, Wood, PlankShade);
            g.DrawString("Магазин", shopTitleFont, Cream, ArtRect(signX, 3, 100, 16), centerFormat);
            if (!string.IsNullOrEmpty(shopMessage))
            {
                g.DrawString(shopMessage, shopTextFont, Cream, new RectangleF(0, 21 * Px, width, 18), centerFormat);
            }

            // --- Локації: 3 картки в ряд
            g.DrawString("Локації", shopNameFont, Cream, 14 * Px, 21 * Px);
            const float locW = 120, locH = 62, locGap = 7, locTop = 34;
            float locLeft = (float)Math.Round((aw - (3 * locW + 2 * locGap)) / 2);
            for (int i = 0; i < Catalog.Locations.Count; i++)
            {
                DrawLocationCard(g, Catalog.Locations[i], locLeft + i * (locW + locGap), locTop, locW, locH);
            }

            // --- Покращення: 5 карток у ряд
            g.DrawString("Покращення", shopNameFont, Cream, 14 * Px, 99 * Px);
            const float upW = 70, upH = 82, upGap = 6, upTop = 112;
            float upLeft = (float)Math.Round((aw - (5 * upW + 4 * upGap)) / 2);
            for (int i = 0; i < UpgradeCatalog.All.Count; i++)
            {
                DrawUpgradeCard(g, UpgradeCatalog.All[i], upLeft + i * (upW + upGap), upTop, upW, upH);
            }
        }

        private void DrawLocationCard(Graphics g, Location location, float ax, float ay, float aw, float ah)
        {
            bool unlocked = ShopService.IsLocationUnlocked(player, location);
            bool current = player.CurrentLocationId == location.Id;

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            DrawPixelPanel(g, ArtRect(ax, ay, aw, ah), Parchment, WoodDark, FloatWhite, ParchmentDark);

            // Картина локації — смужка з її фону
            RectangleF picture = ArtRect(ax + 3, ay + 3, aw - 6, 28);
            if (backgrounds.TryGetValue(location.Id, out Image thumbnail))
            {
                float sourceHeight = thumbnail.Width * picture.Height / picture.Width;
                var source = new RectangleF(0, thumbnail.Height * 0.36f - sourceHeight / 2, thumbnail.Width, sourceHeight);
                var previousMode = g.InterpolationMode;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(thumbnail, picture, source, GraphicsUnit.Pixel);
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Default;
                g.InterpolationMode = previousMode;
            }
            else
            {
                using (var fallback = new SolidBrush(LocationColor(location.Id)))
                {
                    g.FillRectangle(fallback, picture);
                }
            }
            if (!unlocked)
            {
                // Закрита локація: затемнена картина із замком
                g.FillRectangle(LockShade, picture);
                PixelSprite(g, new[] { "..kkk..", ".k...k.", ".k...k.", "kkkkkkk", "kwwwwwk", "kwwkwwk", "kwwwwwk", "kkkkkkk" },
                    AlertColors, ax + aw / 2 - 3, ay + 13);
            }
            PixelRect(g, WoodDark, ax + 3, ay + 31, aw - 6, 1);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawString(location.Name, shopNameFont, CardText, ArtRect(ax, ay + 32, aw, 13), centerFormat);

            RectangleF buttonRect = ArtRect(ax + 10, ay + ah - 15, aw - 20, 11);
            if (current)
            {
                DrawShopButton(g, buttonRect, "Поточна", ShopButtonStyle.Current, false);
            }
            else if (unlocked)
            {
                DrawShopButton(g, buttonRect, "Обрати", ShopButtonStyle.Select, false);
                shopButtons.Add((buttonRect, true, () => SelectLocation(location)));
            }
            else if (player.Money >= location.Price)
            {
                DrawShopButton(g, buttonRect, $"Купити за {location.Price}", ShopButtonStyle.Buy, true);
                shopButtons.Add((buttonRect, true, () => BuyLocation(location)));
            }
            else
            {
                DrawShopButton(g, buttonRect, $"Бракує {location.Price - player.Money}", ShopButtonStyle.Disabled, true);
                shopButtons.Add((buttonRect, false, () => BuyLocation(location)));
            }
        }

        private static readonly Dictionary<char, Brush> UpgradeIconColors = new Dictionary<char, Brush>
        {
            { 'k', Ink }, { 'a', RodLight }, { 's', FloatRed }, { 'b', MetalLight }, { 'w', FloatWhite },
            { 'p', FloatRedLight }, { 'y', Gold },
        };

        // Кольори значків на піксельних кнопках меню (оголошено після пензлів, щоб вони вже були створені)
        private static readonly Dictionary<char, Brush> ButtonIconColors = new Dictionary<char, Brush>
        {
            { 'k', Ink }, { 'w', FloatWhite }, { 'y', Gold }, { 'o', BtnOrangeDark }, { 'a', Wood }, { 'd', WoodDark },
            { 'r', FloatRed }, { 'l', FloatRedLight }, { 'g', Zone }, { 'e', ZoneDark }, { 'm', MetalDark }, { 'b', MetalLight },
            { 'c', PlankLight }, { 't', Track }, { 'n', TrackDark }, { 'p', Parchment }, { 'q', ParchmentDark },
        };

        private static string[] UpgradeIcon(UpgradeType type)
        {
            // Значки 10x10 арт-пікселів: вудка, котушка, хробак, секундомір, гаманець
            switch (type)
            {
                case UpgradeType.CatchBar: return new[] { "........ka", ".......ka.", "......ka..", ".....ka...", "....ka....", "...ka.....", "..ka......", ".kssk.....", "kssk......", ".kk......." };
                case UpgradeType.CastRange: return new[] { ".kkkkkkkk.", "kbbbbbbbbk", "kbwwwwwwbk", "kbbbbbbbbk", "kbwwwwwwbk", "kbbbbbbbbk", "kbwwwwwwbk", "kbbbbbbbbk", ".kkkkkkkk.", ".........." };
                case UpgradeType.BiteSpeed: return new[] { "......kk..", ".....kppk.", "....kpkkpk", "...kpk..k.", "..kpk.....", ".kpk......", "kpk.......", "kpk.......", ".kpk......", "..kk......" };
                case UpgradeType.ReactionWindow: return new[] { "...kkkk...", "..kwwwwk..", ".kwwwkwwk.", "kwwwwkwwwk", "kwwwwkwwwk", "kwwwwkkkwk", "kwwwwwwwwk", ".kwwwwwwk.", "..kwwwwk..", "...kkkk..." };
                default: return new[] { "...kkkk...", "....kk....", "..kyyyyk..", ".kyyyyyyk.", "kyyykkyyyk", "kyykyyyyyk", "kyyykkyyyk", "kyyyyykyyk", ".kykkyyyk.", "..kkkkkk.." };
            }
        }

        private static void PixelSpriteScaled(Graphics g, string[] rows, Dictionary<char, Brush> colors, float ax, float ay, int scale)
        {
            for (int y = 0; y < rows.Length; y++)
            {
                for (int x = 0; x < rows[y].Length; x++)
                {
                    if (colors.TryGetValue(rows[y][x], out Brush brush))
                    {
                        PixelRect(g, brush, ax + x * scale, ay + y * scale, scale, scale);
                    }
                }
            }
        }

        private void DrawUpgradeCard(Graphics g, Upgrade upgrade, float ax, float ay, float aw, float ah)
        {
            int level = UpgradeLevel(upgrade);
            bool maxed = level >= upgrade.MaxLevel;
            int price = UpgradePrice(upgrade);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            DrawPixelPanel(g, ArtRect(ax, ay, aw, ah), Parchment, WoodDark, FloatWhite, ParchmentDark);
            PixelSpriteScaled(g, UpgradeIcon(upgrade.Type), UpgradeIconColors, ax + aw / 2 - 10, ay + 4, 2);

            // Рівень: заповнені комірки — куплені рівні
            float pipsLeft = ax + aw / 2 - (upgrade.MaxLevel * 11 - 2) / 2f;
            for (int i = 0; i < upgrade.MaxLevel; i++)
            {
                PixelRect(g, Ink, pipsLeft + i * 11, ay + 47, 9, 3);
                PixelRect(g, i < level ? (Brush)Zone : TrackDark, pipsLeft + i * 11 + 1, ay + 48, 7, 1);
            }

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawString(upgrade.Name, upgradeNameFont, CardText, ArtRect(ax, ay + 26, aw, 9), centerFormat);
            g.DrawString(upgrade.Description, upgradeDescFont, CardSubText, ArtRect(ax, ay + 35, aw, 9), centerFormat);

            RectangleF buttonRect = ArtRect(ax + 5, ay + ah - 16, aw - 10, 11);
            if (maxed)
            {
                DrawShopButton(g, buttonRect, "Максимум", ShopButtonStyle.Maxed, false);
            }
            else if (player.Money >= price)
            {
                DrawShopButton(g, buttonRect, price.ToString(), ShopButtonStyle.Buy, true);
                shopButtons.Add((buttonRect, true, () => BuyUpgrade(upgrade)));
            }
            else
            {
                DrawShopButton(g, buttonRect, price.ToString(), ShopButtonStyle.Disabled, true);
                shopButtons.Add((buttonRect, false, () => BuyUpgrade(upgrade)));
            }
        }

        private static readonly Dictionary<char, Brush> FishIconColors = new Dictionary<char, Brush>
        {
            { 'k', Ink }, { 'w', FloatWhite },
        };

        private void DrawFightBar(Graphics g)
        {
            // Піксельна смуга міні-гри в дерев'яній рамці; ліворуч — шкала прогресу
            float ax = (float)Math.Floor(LogicalWidth / Px) - 40, ay = 30;
            const float trackW = 13, trackH = 148;
            DrawPixelPanel(g, new RectangleF((ax - 2) * Px, (ay - 2) * Px, (trackW + 4) * Px, (trackH + 4) * Px), Track, WoodDark, Wood, null);
            PixelRect(g, TrackDark, ax, ay, trackW, 6);                      // темніше вгорі — глибина

            // Зелена зона навколо playerPos: значення 100 — верх смуги, 0 — низ
            float zoneHigh = Math.Min(100f, playerPos + ZoneHalfSize);
            float zoneLow = Math.Max(0f, playerPos - ZoneHalfSize);
            float zTop = (float)Math.Round(ay + (100f - zoneHigh) / 100f * trackH);
            float zBottom = (float)Math.Round(ay + (100f - zoneLow) / 100f * trackH);
            PixelRect(g, Zone, ax, zTop, trackW, Math.Max(1, zBottom - zTop));
            PixelRect(g, ZoneLight, ax, zTop, trackW, 1);
            PixelRect(g, ZoneDark, ax, zBottom - 1, trackW, 1);
            PixelRect(g, ZoneDark, ax + trackW - 1, zTop + 1, 1, Math.Max(0, zBottom - zTop - 2));

            // Риба: маленький піксельний силует кольору рідкості
            float fishY = (float)Math.Round(ay + (100f - fishPos) / 100f * trackH);
            Color fishColor = hookedFish == null ? Color.DarkRed : RarityColor(hookedFish.Rarity);
            using (var body = new SolidBrush(fishColor))
            {
                var colors = new Dictionary<char, Brush>(FishIconColors) { { 'f', body } };
                PixelSprite(g, new[]
                {
                    ".kkkk..k",
                    "kwkffkkf",
                    "kfffffff",
                    ".kkkk..k",
                }, colors, ax + 2, fishY - 2);
            }

            // Шкала прогресу: заповнюється знизу вгору, колір від червоного до зеленого
            float px = ax - 8;
            DrawPixelPanel(g, new RectangleF((px - 1) * Px, (ay - 2) * Px, 6 * Px, (trackH + 4) * Px), TrackDark, WoodDark, null, null);
            float fill = (float)Math.Round(fightProgress / 100f * trackH);
            Brush color = fightProgress < 30 ? FloatRed : fightProgress < 65 ? (Brush)ParchmentDark : Zone;
            if (fill > 0)
            {
                PixelRect(g, color, px, ay + trackH - fill, 4, fill);
                PixelRect(g, FloatWhite, px, ay + trackH - fill, 4, 1);
            }
        }
    }
}
