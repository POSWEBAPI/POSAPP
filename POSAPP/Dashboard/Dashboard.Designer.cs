using POSAPP.Sales;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Point = System.Drawing.Point;
using Size = System.Drawing.Size;
using Rectangle = System.Drawing.Rectangle;

namespace POSAPP
{
    partial class Dashboard
    {
        private System.ComponentModel.IContainer components = null;

        private POSAPP.BufferedPanel panelSidebar;
        private POSAPP.BufferedPanel panelTitleBar;
        private POSAPP.BufferedPanel panelTopBar;
        private POSAPP.BufferedPanel panelMain;
        private POSAPP.BufferedPanel panelContent;
        private BufferedPanel _salesCard;
        private POSAPP.BufferedPanel panelProfileSubmenu;
        private int _topProductsHoverIndex = -1;
        private Button btnNavPurchaseOrder;
        private Panel _topProductsPanel; // store reference to the panel

        private Label lblTitleBrand;
        private Button btnMinimize, btnMaximize, btnClose;

        private BufferedPanel panelBrand;
        private Label lblBrandName, lblBrandSub;
        private Button btnNavDashboard, btnNavSales, btnNavCustomers,
                       btnNavInventory, btnNavSalesReturn, btnNavAccounting,
                       btnNavReports, btnNavProfile, btnNavSettings;
        private Panel panelSideFooter;
        private Button btnNavCloseShift;

        private Button btnSidebarToggle;
        //private Label lblGreeting, lblGreetingSub;
        private Panel panelBellWrap;
        private Label lblBell, lblBellBadge;
        private Panel panelAvatar;

        private DataGridView dgvTransactions;
        private Button btnNavTenderDeclaration;

        private Panel panelFooterBar; 
        private Button btnSubLogout;
        private Button btnNavFloat;
        private Panel panelReportsSubmenu;

        private Label lblDateText;


        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        // ── Palette ────────────────────────────────────────────────────────
        // Refreshed, softer palette — lighter navy, cooler accents, airier backgrounds
        static readonly Color C_Navy = Color.FromArgb(23, 32, 58);        // sidebar / header text
        static readonly Color C_NavyMid = Color.FromArgb(33, 46, 82);
        static readonly Color C_Blue = Color.FromArgb(59, 130, 246);      // primary accent
        static readonly Color C_BlueHov = Color.FromArgb(45, 110, 225);
        static readonly Color C_Green = Color.FromArgb(34, 197, 130);
        static readonly Color C_Amber = Color.FromArgb(245, 158, 11);
        static readonly Color C_Purple = Color.FromArgb(139, 92, 246);
        static readonly Color C_Pink = Color.FromArgb(236, 72, 153);
        static readonly Color C_Red = Color.FromArgb(239, 68, 68);
        static readonly Color C_Slate = Color.FromArgb(120, 132, 156);
        static readonly Color C_LightBg = Color.FromArgb(247, 249, 253);
        static readonly Color C_White = Color.White;
        static readonly Color C_Border = Color.FromArgb(228, 233, 245);
        static readonly Color C_CardBg = Color.White;

        // ── Fonts ─────────────────────────────────────────────────────────
        static readonly Font F_H1 = new Font("Segoe UI", 14F, FontStyle.Bold);
        static readonly Font F_H2 = new Font("Segoe UI", 11F, FontStyle.Bold);
        static readonly Font F_H3 = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        static readonly Font F_Body = new Font("Segoe UI", 9F);
        static readonly Font F_Small = new Font("Segoe UI", 8F);
        static readonly Font F_Micro = new Font("Segoe UI", 7.5F);
        static readonly Font F_NavBtn = new Font("Segoe UI Emoji", 9.5F);
        static readonly Font F_Num = new Font("Segoe UI", 16F, FontStyle.Bold);
        static readonly Font F_NumSm = new Font("Segoe UI", 10F, FontStyle.Bold);

        // ── Helpers ───────────────────────────────────────────────────────
        static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var path = new GraphicsPath();

            if (r.Width <= 0 || r.Height <= 0)
                return path;

            // Prevent radius from being larger than the rectangle.
            int maxRadius = Math.Min(r.Width, r.Height) / 2;
            radius = Math.Max(0, Math.Min(radius, maxRadius));

            // No rounding required.
            if (radius <= 0)
            {
                path.AddRectangle(r);
                return path;
            }

            int d = radius * 2;

            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);

            path.CloseFigure();

            return path;
        }

        static BufferedPanel MakeCard(int x, int y, int w, int h)
        {
            var p = new BufferedPanel { Location = new Point(x, y), Size = new Size(w, h), BackColor = C_CardBg };

            GraphicsPath cachedPath = null;
            GraphicsPath cachedShadowPath = null;
            Size cachedSize = Size.Empty;

            void RebuildPaths()
            {
                cachedPath?.Dispose();
                cachedShadowPath?.Dispose();
                var rc = new Rectangle(0, 0, p.Width - 1, p.Height - 1);
                cachedPath = RoundRect(rc, 14);
                cachedShadowPath = RoundRect(new Rectangle(2, 3, p.Width - 1, p.Height - 1), 14);
                cachedSize = p.Size;
                p.Region = new Region(cachedPath);
            }

            p.Paint += (s, e) =>
            {
                if (cachedPath == null || cachedSize != p.Size) RebuildPaths();

                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                using var shadowBrush = new SolidBrush(Color.FromArgb(10, 20, 40, 90));
                g.FillPath(shadowBrush, cachedShadowPath);

                g.FillPath(new SolidBrush(C_CardBg), cachedPath);
                using var borderPen = new Pen(C_Border, 1f);
                g.DrawPath(borderPen, cachedPath);
            };
            return p;
        }

        static Button MakeNavBtn(string icon, string label, int y, EventHandler handler)
        {
            var b = new Button
            {
                Text = $"  {icon}   {label}",
                Font = F_NavBtn,
                ForeColor = Color.FromArgb(160, 175, 205),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(220, 44),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(18, 0, 0, 0),
                Cursor = Cursors.Hand,
                Tag = label
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(18, 255, 255, 255);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(34, 255, 255, 255);
            if (handler != null) b.Click += handler;
            b.MouseEnter += (s, e) => { if ((string)b.Tag != "active") b.ForeColor = Color.White; };
            b.MouseLeave += (s, e) => { if ((string)b.Tag != "active") b.ForeColor = Color.FromArgb(160, 175, 205); };
            return b;
        }

        private static void DrawFallbackLogo(Graphics g, Rectangle rc)
        {
            using var br = new LinearGradientBrush(rc, C_Blue, Color.FromArgb(56, 189, 248), 135f);
            using var path = RoundRect(rc, 14);
            g.FillPath(br, path);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("E", new Font("Segoe UI", 20F, FontStyle.Bold), Brushes.White, rc, sf);
        }

        // ══════════════════════════════════════════════════════════════════
        private void InitializeComponent()
        {
            this.SuspendLayout();

            var workArea = Screen.FromControl(this) != null
      ? Screen.PrimaryScreen.WorkingArea
      : new Rectangle(0, 0, 1280, 800);

            MinimumSize = new Size(1100, 650);      // never collapse below usable size
            ClientSize = new Size(
                Math.Max(MinimumSize.Width, workArea.Width),
                Math.Max(MinimumSize.Height, workArea.Height));
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Location = workArea.Location;
            BackColor = C_LightBg;
            Load += Form2_Load;

            // ── 1. TITLE BAR ───────────────────────────────────────────────
            panelTitleBar = new BufferedPanel
            {
                Dock = DockStyle.Top,
                Height = 32,
                BackColor = C_Navy
            };
            panelTitleBar.MouseDown += panelTitleBar_MouseDown;
            panelTitleBar.MouseMove += panelTitleBar_MouseMove;
            panelTitleBar.MouseUp += panelTitleBar_MouseUp;
            panelTitleBar.DoubleClick += panelTitleBar_DoubleClick;

            lblTitleBrand = new Label
            {
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(150, 170, 215),
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(228, 8)
            };

            Button TitleBtn(string sym, Color hoverColor, EventHandler click)
            {
                var b = new Button
                {
                    Text = sym,
                    Font = new Font("Segoe UI", 10F),
                    Size = new Size(44, 32),
                    FlatStyle = FlatStyle.Flat,
                    ForeColor = Color.FromArgb(180, 195, 225),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand,
                    TabStop = false
                };
                b.FlatAppearance.BorderSize = 0;
                b.Click += click;
                b.MouseEnter += (s, e) => b.BackColor = hoverColor;
                b.MouseLeave += (s, e) => b.BackColor = Color.Transparent;
                return b;
            }

            btnClose = TitleBtn("✕", Color.FromArgb(220, 38, 38), btnClose_Click);
            btnMaximize = TitleBtn("□", Color.FromArgb(40, 255, 255, 255), btnMaximize_Click);
            btnMinimize = TitleBtn("─", Color.FromArgb(40, 255, 255, 255), btnMinimize_Click);

            void PositionTitleBtns()
            {
                int tw = panelTitleBar.Width;
                btnClose.Location = new Point(tw - 44, 0);
                btnMaximize.Location = new Point(tw - 88, 0);
                btnMinimize.Location = new Point(tw - 132, 0);
            }
            PositionTitleBtns();
            panelTitleBar.Resize += (s, _) => PositionTitleBtns();
            panelTitleBar.Controls.AddRange(new Control[] { lblTitleBrand, btnClose, btnMaximize, btnMinimize });

            // ── 2. SIDEBAR ─────────────────────────────────────────────────
            panelSidebar = new BufferedPanel
            {
                Dock = DockStyle.Left,
                Width = 224,
                BackColor = C_Navy
            };
            panelSidebar.Paint += (s, e) =>
            {
                using var pen = new Pen(Color.FromArgb(24, 100, 140, 240), 1);
                e.Graphics.DrawLine(pen, panelSidebar.Width - 1, 0, panelSidebar.Width - 1, panelSidebar.Height);
            };

            // Brand block
            panelBrand = new BufferedPanel
            {
                Size = new Size(224, 108),
                Location = new Point(0, 0),
                BackColor = Color.Transparent
            };
            panelBrand.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var logoRc = new Rectangle(85, 14, 54, 54);
                string logoPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logo1.jpg");
                if (System.IO.File.Exists(logoPath))
                {
                    try
                    {
                        using var img = Image.FromFile(logoPath);
                        using var clipPath = RoundRect(logoRc, 14);
                        var savedClip = g.Clip;
                        g.SetClip(clipPath);
                        g.DrawImage(img, logoRc);
                        g.Clip = savedClip;
                        using var ring = new Pen(Color.FromArgb(60, 255, 255, 255), 1.2f);
                        using var ringPath = RoundRect(logoRc, 14);
                        g.DrawPath(ring, ringPath);
                    }
                    catch { DrawFallbackLogo(g, logoRc); }
                }
                else DrawFallbackLogo(g, logoRc);
            };

            // Nav buttons
            const int NAV_START = 116;
            const int NAV_H = 44;

            btnNavDashboard = MakeNavBtn("🏠", "Dashboard", NAV_START + NAV_H * 0, btnNavDashboard_Click);
            btnNavSales = MakeNavBtn("🛒", "Sales", NAV_START + NAV_H * 1, btnNavSales_Click);
            btnNavCustomers = MakeNavBtn("↩", "Sales Return", NAV_START + NAV_H * 2, btnNavSalesReturn_Click);
            btnNavSalesReturn = MakeNavBtn("📦", "Inventory", NAV_START + NAV_H * 3, btnNavInventory_Click);
            //btnNavAccounting = MakeNavBtn("📄", "Payment", NAV_START + NAV_H * 4, btnPending_Click);
            btnNavReports = MakeNavBtn("📊", "Reports", NAV_START + NAV_H * 4, btnNavReports_Click);
            btnNavSettings = MakeNavBtn("⚙", "Settings", NAV_START + NAV_H * 5, btnSettings_Click);
            btnNavFloat = MakeNavBtn("🪙", "Float Entry", NAV_START + NAV_H * 6, btnNavFloat_Click);
            btnNavTenderDeclaration = MakeNavBtn("📋", "Tender Declaration", NAV_START + NAV_H * 7, btnNavTenderDeclaration_Click);
            btnNavCloseShift = MakeNavBtn("🔒", "Shift", NAV_START + NAV_H * 8, btnNavCloseShift_Click);
            btnNavProfile = MakeNavBtn("👤", "My Profile", NAV_START + NAV_H * 9, btnNavProfile_Click);
        
 
//btnNavPurchaseOrder = MakeNavBtn("🧾", "Purchase Orders", NAV_START + NAV_H* 10, btnNavPurchaseOrder_Click);

        btnNavInventory = btnNavSalesReturn;
            SetActiveNav(btnNavDashboard);

            // ── Reports popup (floats on Form) ─────────────────────────────
            panelReportsSubmenu = new BufferedPanel
            {
                BackColor = Color.FromArgb(28, 38, 66),
                Size = new Size(214, NAV_H * 4),
                Visible = false
            };
            panelReportsSubmenu.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rc = new Rectangle(0, 0, panelReportsSubmenu.Width - 1, panelReportsSubmenu.Height - 1);
                using var path = RoundRect(rc, 12);
                using var fillBr = new SolidBrush(Color.FromArgb(28, 38, 66));
                g.FillPath(fillBr, path);
                using var border = new Pen(Color.FromArgb(70, 110, 150, 240), 1.2f);
                g.DrawPath(border, path);
                panelReportsSubmenu.Region = new Region(path);
            };

            Button MakeSubBtn(string icon, string label, int subY, EventHandler handler)
            {
                var b = new Button
                {
                    Text = $"        {icon}   {label}",
                    Font = new Font("Segoe UI Emoji", 9F),
                    ForeColor = Color.FromArgb(165, 195, 240),
                    BackColor = Color.Transparent,
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(214, NAV_H),
                    Location = new Point(0, subY),
                    TextAlign = ContentAlignment.MiddleLeft,
                    Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 255, 255, 255);
                b.FlatAppearance.MouseDownBackColor = Color.FromArgb(40, 255, 255, 255);
                b.MouseEnter += (s, e) => b.ForeColor = Color.White;
                b.MouseLeave += (s, e) => b.ForeColor = Color.FromArgb(165, 195, 240);
                if (handler != null) b.Click += handler;
                b.Click += (s, e) => panelReportsSubmenu.Visible = false;
                return b;
            }

            var btnSubInventoryReport = MakeSubBtn("📦", "Inventory Report", NAV_H * 0, null);
            var btnSubSalesReport = MakeSubBtn("🛒", "Sales Report", NAV_H * 1, null);
            var btnSubDayEnd = MakeSubBtn("📅", "Day End Report", NAV_H * 2, btnSubDayEnd_Click);
            var btnSubReturnReport = MakeSubBtn("↩", "Return Report", NAV_H * 3, btnSubReturnReport_Click);
            panelReportsSubmenu.Controls.AddRange(new Control[] { btnSubInventoryReport, btnSubSalesReport, btnSubDayEnd, btnSubReturnReport });

            // Profile submenu
            panelProfileSubmenu = new BufferedPanel
            {
                BackColor = Color.FromArgb(33, 44, 78),
                Size = new Size(224, NAV_H),
                Location = new Point(0, NAV_START + NAV_H * 11),
                Visible = false
            };
            btnSubLogout = new Button
            {
                Text = "⏻   Logout",
                Font = new Font("Segoe UI Emoji", 9.5F),
                ForeColor = Color.FromArgb(252, 165, 165),
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(20, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btnSubLogout.FlatAppearance.BorderSize = 0;
            btnSubLogout.Click += btnSubLogout_Click;
            panelProfileSubmenu.Controls.Add(btnSubLogout);

            panelSidebar.Controls.AddRange(new Control[]
            {
                panelBrand,
                btnNavDashboard, btnNavSales, btnNavCustomers,
                btnNavSalesReturn, btnNavAccounting, btnNavReports,
                btnNavSettings, btnNavFloat, btnNavTenderDeclaration,
                btnNavCloseShift, btnNavProfile,
                panelProfileSubmenu
            });
            panelProfileSubmenu.BringToFront();

            // ── 3. MAIN PANEL ──────────────────────────────────────────────
            panelMain = new BufferedPanel { Dock = DockStyle.Fill, BackColor = C_LightBg };

            panelTopBar = new BufferedPanel
            {
                Dock = DockStyle.Top,
                Height = 70,
                BackColor = C_White
            };
            panelTopBar.Paint += (s, e) =>
            {
                using var pen = new Pen(C_Border, 1);
                e.Graphics.DrawLine(pen, 0, panelTopBar.Height - 1, panelTopBar.Width, panelTopBar.Height - 1);
            };

            btnSidebarToggle = new Button
            {
                Text = "☰",
                Font = new Font("Segoe UI", 12F),
                ForeColor = C_Navy,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(40, 40),
                Location = new Point(20, 15),
                Cursor = Cursors.Hand
            };
            btnSidebarToggle.FlatAppearance.BorderSize = 0;
            btnSidebarToggle.Click += (s, e) =>
            {
                panelSidebar.Visible = !panelSidebar.Visible;
                panelReportsSubmenu.Visible = false;
            };

            // REPLACE:
            //lblGreeting = new Label
            //{
            //    Text = "Good Morning, Admin 👋",
            //    Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            //    ForeColor = C_Navy,
            //    BackColor = Color.Transparent,
            //    AutoSize = true,
            //    Location = new Point(68, 10),
            //    MaximumSize = new Size(600, 0)   // ← prevents clipping
            //};
            //lblGreetingSub = new Label
            //{
            //    Text = "Here's what's happening in your store today.",
            //    Font = new Font("Segoe UI", 8.5F),
            //    ForeColor = C_Slate,
            //    BackColor = Color.Transparent,
            //    AutoSize = true,
            //    Location = new Point(68, 40)
            //};

            panelTopBar.Controls.AddRange(new Control[]
                { btnSidebarToggle,  panelBellWrap, panelAvatar });//lblGreeting, lblGreetingSub,

            lblDateText = new Label
            {
                Text = DateTime.Now.ToString("ddd, dd MMM yyyy  HH:mm"),
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = C_Slate,
                BackColor = Color.Transparent,
                AutoSize = true
            };

            panelContent = new BufferedPanel
            {
                Dock = DockStyle.Fill,
                BackColor = C_LightBg,
                AutoScroll = true,
                Padding = new Padding(22, 18, 22, 22)
            };

            panelContent.MouseClick += (s, e) => panelReportsSubmenu.Visible = false;
            panelMain.MouseClick += (s, e) => panelReportsSubmenu.Visible = false;
            panelTopBar.MouseClick += (s, e) => panelReportsSubmenu.Visible = false;

            BuildContent();

            panelMain.Controls.Add(panelContent);
            panelMain.Controls.Add(panelTopBar);

            this.Controls.Add(panelMain);
            this.Controls.Add(panelSidebar);
            this.Controls.Add(panelTitleBar);
            this.Controls.Add(panelReportsSubmenu);
            panelReportsSubmenu.BringToFront();

            this.ResumeLayout(false);
        }

        // ── ShowReportsPopup ──────────────────────────────────────────────
        private void ShowReportsPopup()
        {
            if (panelReportsSubmenu.Visible) { panelReportsSubmenu.Visible = false; return; }
            var btnScreenPt = btnNavReports.PointToScreen(Point.Empty);
            var formClientPt = this.PointToClient(btnScreenPt);
            int popupX = panelSidebar.Width + 6;
            int popupY = formClientPt.Y;
            int maxY = this.ClientSize.Height - panelReportsSubmenu.Height - 10;
            if (popupY > maxY) popupY = maxY;
            panelReportsSubmenu.Location = new Point(popupX, popupY);
            panelReportsSubmenu.BringToFront();
            panelReportsSubmenu.Visible = true;
        }

        // ── RepositionNavButtons ──────────────────────────────────────────
        private void RepositionNavButtons()
        {
            const int NAV_START = 116;
            const int NAV_H = 44;
            var buttons = new Button[]
            {
                btnNavDashboard, btnNavSales, btnNavCustomers, btnNavSalesReturn,
                btnNavAccounting, btnNavReports, btnNavSettings, btnNavFloat,
                btnNavTenderDeclaration, btnNavCloseShift, btnNavProfile,
            };
            int y = NAV_START;
            foreach (var btn in buttons) { btn.Location = new Point(0, y); y += NAV_H; }
            if (panelProfileSubmenu.Visible) { panelProfileSubmenu.Location = new Point(0, y); y += panelProfileSubmenu.Height; }
            panelSidebar.Invalidate();
        }

        // ── BuildContent ──────────────────────────────────────────────────
        private void BuildContent()
        {
            var inner = new BufferedPanel
            {
                Location = new Point(0, 0),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            inner.Width = Math.Max(800, panelContent.Width - panelContent.Padding.Horizontal - 4);
            // Width tracking only — the row TableLayoutPanels below are Dock=Top,
            // so they (and their percent-sized columns/Dock=Fill cards) automatically
            // restretch to inner's new width without a full rebuild. A full data +
            // layout rebuild still happens on actual form resize (debounced, see
            // Dashboard.cs Form2_Load) which is where fresh stats get reloaded.
            panelContent.Resize += (s, _) =>
                inner.Width = Math.Max(800, panelContent.Width - panelContent.Padding.Horizontal - 4);
            RebuildInner(inner);
            panelContent.Controls.Add(inner);
        }

        // ── RebuildInner ──────────────────────────────────────────────────
        // Builds the dashboard's three content rows as TableLayoutPanels with
        // percentage-width columns (instead of hand-computed pixel offsets).
        // Each row is Dock=Top inside `inner`, and cards inside each row are
        // Dock=Fill within their cell — so the whole grid restretches to any
        // window width/DPI automatically, without needing to recompute a
        // single coordinate by hand.
        private void RebuildInner(Panel inner)
        {
            inner.Controls.Clear();
            const int gap = 16;

            int availH = Math.Max(650, panelContent.ClientSize.Height - panelContent.Padding.Vertical - 8);

            // ── STAT CARDS ROW ───────────────────────────────────────────
            _dashStats = LoadDashboardStats();
            _statAnimTimer = AnimateProgress(_statAnimTimer, v => _statAnimProgress = v,
           () => { if (_statCards != null) foreach (var sc in _statCards) sc?.Invalidate(); },
           durationMs: 1700);

            var (salesVals, salesLabels) = LoadSalesOverviewData();
            _salesChartVals = salesVals;
            _salesChartLabels = salesLabels;
            _salesChartMax = Math.Max(10m, _salesChartVals.Max() * 1.15m);

            var statData = new[]
            {
        new { Icon="🛒", Val=FormatAmount(_dashStats.salesToday),    Lbl="Total Sales Today",  Pos=true,  Accent=C_Blue,   Bg=Color.FromArgb(232,241,255) },
        new { Icon="📋", Val=_dashStats.orderCount.ToString(),       Lbl="Sales Orders",       Pos=true,  Accent=C_Green,  Bg=Color.FromArgb(226,250,240) },
        new { Icon="⏳", Val=_dashStats.unpaidCount.ToString(),      Lbl="Unpaid Invoices",    Pos=_dashStats.unpaidCount == 0, Accent=C_Amber, Bg=Color.FromArgb(255,246,228) },
        new { Icon="↩",  Val=FormatAmount(_dashStats.returnsTotal),  Lbl="Sales Returns",      Pos=false, Accent=C_Purple, Bg=Color.FromArgb(241,236,255) },
    };

            int statH = Math.Max(96, (int)(availH * 0.13));
            var statsRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = statH,
                Margin = new Padding(0, 4, 0, gap),
                ColumnCount = statData.Length,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            for (int i = 0; i < statData.Length; i++)
                statsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / statData.Length));
            statsRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            for (int i = 0; i < statData.Length; i++)
            {
                var d = statData[i];
                int capturedI = i;
                var card = MakeCard(0, 0, 0, 0);
                card.Dock = DockStyle.Fill;
                card.Margin = new Padding(i == 0 ? 0 : gap / 2, 0, i == statData.Length - 1 ? 0 : gap / 2, 0);
                card.Cursor = Cursors.Hand;
                _statCards[i] = card;

                card.Paint += (s, e) =>
                {
                    var g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    string liveVal = capturedI switch
                    {
                        0 => FormatAmount(_dashStats.salesToday * (decimal)_statAnimProgress),
                        1 => ((int)(_dashStats.orderCount * _statAnimProgress)).ToString(),
                        2 => ((int)(_dashStats.unpaidCount * _statAnimProgress)).ToString(),
                        3 => FormatAmount(_dashStats.returnsTotal * (decimal)_statAnimProgress),
                        _ => ""
                    };

                    // Top accent stripe
                    using var stripePath = RoundRect(new Rectangle(0, 0, card.Width, 8), 4);
                    g.SetClip(new Rectangle(0, 0, card.Width, 4));
                    g.FillPath(new SolidBrush(d.Accent), stripePath);
                    g.ResetClip();

                    // Icon circle
                    var iconRc = new Rectangle(20, 20, 46, 46);
                    using var iconPath = RoundRect(iconRc, 23);
                    g.FillPath(new SolidBrush(d.Bg), iconPath);
                    using var sfC = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString(d.Icon, new Font("Segoe UI Emoji", 16F),
                        new SolidBrush(d.Accent), iconRc, sfC);

                    // Label
                    g.DrawString(d.Lbl, F_Small, new SolidBrush(C_Slate), new PointF(78, 22));

                    // Value
                    g.DrawString(liveVal, F_Num, new SolidBrush(C_Navy), new PointF(76, 42));
                };

                card.Click += (s, e) => StatCard_Click(capturedI);
                statsRow.Controls.Add(card, i, 0);
            }

            // ── MIDDLE ROW ────────────────────────────────────────────────
            // Column weights (42/30/28) mirror the original proportions, but
            // as TableLayoutPanel percent columns instead of cw-multiplied
            // pixel math, so they hold their proportions at any width.
            int midH = Math.Max(260, (int)(availH * 0.34));
            var middleRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = midH,
                Margin = new Padding(0, 0, 0, gap),
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            middleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
            middleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
            middleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            middleRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var salesCard = MakeCard(0, 0, 0, 0);
            salesCard.Dock = DockStyle.Fill;
            salesCard.Margin = new Padding(0, 0, gap / 2, 0);
            salesCard.Paint += PaintSalesChart;
            middleRow.Controls.Add(salesCard, 0, 0);
            _chartAnimTimer = AnimateProgress(_chartAnimTimer, v => _chartAnimProgress = v,
     () => salesCard.Invalidate(),
     durationMs: 1700);

            var topProdCard = MakeCard(0, 0, 0, 0);
            topProdCard.Dock = DockStyle.Fill;
            topProdCard.Margin = new Padding(gap / 2, 0, gap / 2, 0);
            topProdCard.Paint += PaintTopProducts;
            _topProductsPanel = topProdCard;
            _topProductsPanel.MouseMove += TopProducts_MouseMove;
            _topProductsPanel.MouseLeave += TopProducts_MouseLeave;
            middleRow.Controls.Add(topProdCard, 1, 0);

            _topProdAnimTimer = AnimateProgress(_topProdAnimTimer, v => _topProdAnimProgress = v,
       () => _topProductsPanel?.Invalidate(),
       durationMs: 1700);

            var lowStockCard = MakeCard(0, 0, 0, 0);
            lowStockCard.Dock = DockStyle.Fill;
            lowStockCard.Margin = new Padding(gap / 2, 0, 0, 0);
            lowStockCard.Paint += PaintLowStock;
            middleRow.Controls.Add(lowStockCard, 2, 0);

            // ── BOTTOM ROW ────────────────────────────────────────────────
            int botH = Math.Max(280, availH - statH - midH - gap * 3 - 60);
            var bottomRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = botH,
                Margin = new Padding(0),
                ColumnCount = 3,
                RowCount = 1,
                BackColor = Color.Transparent
            };
            bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48f));
            bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28f));
            bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24f));
            bottomRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var txCard = MakeCard(0, 0, 0, 0);
            txCard.Dock = DockStyle.Fill;
            txCard.Margin = new Padding(0, 0, gap / 2, 0);
            BuildTransactionCard(txCard);
            bottomRow.Controls.Add(txCard, 0, 0);

            _payCard = MakeCard(0, 0, 0, 0);
            _payCard.Dock = DockStyle.Fill;
            _payCard.Margin = new Padding(gap / 2, 0, gap / 2, 0);
            _payCard.Paint += PaintPaymentMethods;
            bottomRow.Controls.Add(_payCard, 1, 0);

            _pieAnimTimer = AnimateProgress(_pieAnimTimer, v => _pieAnimProgress = v,
      () => { _payCard?.Invalidate(); _paymentPanel?.Invalidate(); },
      durationMs: 1700);

            var qaCard = MakeCard(0, 0, 0, 0);
            qaCard.Dock = DockStyle.Fill;
            qaCard.Margin = new Padding(gap / 2, 0, 0, 0);
            BuildQuickActionsCard(qaCard);
            bottomRow.Controls.Add(qaCard, 2, 0);

            // Dock=Top stacking in WinForms places the LAST-added control
            // outermost (closest to the edge) — matching the pattern already
            // used above for panelTitleBar/panelSidebar/panelMain. So to get
            // statsRow → middleRow → bottomRow top-to-bottom, they must be
            // added in the reverse order: bottomRow, middleRow, statsRow.
            inner.Controls.Add(bottomRow);
            inner.Controls.Add(middleRow);
            inner.Controls.Add(statsRow);

            inner.Height = statH + midH + botH + gap * 3 + 44 + 10;
        }

        // ── PAINT: Sales Chart ────────────────────────────────────────────
        static readonly int[] _weekVals = { 42, 68, 55, 90, 110, 75, 95 };
        static readonly string[] _weekLabels = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

        private void PaintSalesChart(object sender, PaintEventArgs e)
        {
            var p = (Panel)sender;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            g.DrawString("Sales Overview", F_H2, new SolidBrush(C_Navy), new PointF(20, 18));

            var chartRect = new Rectangle(20, 76, p.Width - 40, p.Height - 102);
            decimal max = _salesChartMax > 0 ? _salesChartMax : 100m;
            int n = _salesChartVals.Length;

            using var gridPen = new Pen(Color.FromArgb(14, 0, 0, 0), 1) { DashStyle = DashStyle.Dash };
            for (int row = 0; row <= 4; row++)
            {
                int gy = chartRect.Bottom - (int)(row / 4.0 * chartRect.Height);
                g.DrawLine(gridPen, chartRect.Left, gy, chartRect.Right, gy);
                decimal gridVal = max * row / 4;
                g.DrawString(FormatAmount(gridVal), F_Micro, new SolidBrush(Color.FromArgb(150, C_Slate)), new PointF(0, gy - 7));
            }

            var pts = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                float px = chartRect.Left + i * (chartRect.Width / (float)(n - 1));
                float py = chartRect.Bottom - (float)(_salesChartVals[i] / max) * chartRect.Height;
                pts[i] = new PointF(px, py);
            }

            int visibleCount = Math.Max(1, (int)Math.Ceiling(n * _chartAnimProgress));
            visibleCount = Math.Min(visibleCount, n);
            var visiblePts = pts.Take(visibleCount).ToArray();

            if (visiblePts.Length >= 2)
            {
                var areaPts = new PointF[visiblePts.Length + 2];
                areaPts[0] = new PointF(visiblePts[0].X, chartRect.Bottom);
                for (int i = 0; i < visiblePts.Length; i++) areaPts[i + 1] = visiblePts[i];
                areaPts[areaPts.Length - 1] = new PointF(visiblePts[visiblePts.Length - 1].X, chartRect.Bottom);

                using var fillBr = new LinearGradientBrush(chartRect,
                    Color.FromArgb(60, 59, 130, 246), Color.FromArgb(4, 59, 130, 246),
                    LinearGradientMode.Vertical);
                g.FillPolygon(fillBr, areaPts);
            }

            if (visiblePts.Length >= 2)
            {
                using var linePen = new Pen(C_Blue, 2.4f) { LineJoin = LineJoin.Round };
                g.DrawLines(linePen, visiblePts);
            }

            foreach (var pt in visiblePts)
            {
                g.FillEllipse(new SolidBrush(C_Blue), pt.X - 4, pt.Y - 4, 8, 8);
                g.FillEllipse(Brushes.White, pt.X - 2.5f, pt.Y - 2.5f, 5, 5);
            }

            using var xSf = new StringFormat { Alignment = StringAlignment.Center };
            for (int i = 0; i < n; i++)
            {
                float lx = chartRect.Left + i * (chartRect.Width / (float)(n - 1));
                g.DrawString(_salesChartLabels[i], F_Micro, new SolidBrush(C_Slate),
                    new RectangleF(lx - 20, chartRect.Bottom + 3, 40, 14), xSf);
            }
        }
        private void TopProducts_MouseMove(object sender, MouseEventArgs e)
        {
            const int ROW_H = 52;
            const int START_Y = 56;
            const int NAME_W = 160;
            const int PAD_L = 16;

            int count = _topProductsData?.Count ?? 0;
            int newHover = -1;

            for (int i = 0; i < count; i++)
            {
                int rowTop = START_Y + i * ROW_H;
                int rowBot = rowTop + ROW_H;
                if (e.Y >= rowTop && e.Y < rowBot
                    && e.X >= PAD_L && e.X <= PAD_L + NAME_W)
                {
                    newHover = i;
                    break;
                }
            }

            if (newHover != _topProductsHoverIndex)
            {
                _topProductsHoverIndex = newHover;
                ((Panel)sender).Cursor = newHover >= 0 ? Cursors.Hand : Cursors.Default;
                ((Panel)sender).Invalidate();
            }
        }

        private void TopProducts_MouseLeave(object sender, EventArgs e)
        {
            _topProductsHoverIndex = -1;
            ((Panel)sender).Cursor = Cursors.Default;
            ((Panel)sender).Invalidate();
        }

        // ── PAINT: Top Products ───────────────────────────────────────────
        private void PaintTopProducts(object sender, PaintEventArgs e)
        {
            var p = (Panel)sender;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            g.DrawString("Top Products", F_H2, new SolidBrush(C_Navy), new PointF(18, 18));

            if (_topProductsData == null || _topProductsData.Count == 0)
            {
                using var sf = new StringFormat
                { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                //g.DrawString("No sales data available.\nData syncs every 10 minutes.",
                //    F_Body, new SolidBrush(C_Slate),
                //    new RectangleF(0, 40, p.Width, p.Height - 40), sf);
                return;
            }

            // ── Layout constants ──────────────────────────────────────────────────
            const int PAD_L = 16;
            const int PAD_R = 16;
            const int ROW_H = 52;
            const int BAR_H = 6;
            const int SOLD_W = 68;
            const int PCT_W = 36;
            const int NAME_W = 160;
            const int GAP = 14;

            int rightEdge = p.Width - PAD_R;
            int barColX = PAD_L + NAME_W + GAP;
            int barMaxW = rightEdge - SOLD_W - PCT_W - 8 - barColX;

            int y = 56;

            for (int idx = 0; idx < _topProductsData.Count; idx++)
            {
                var prod = _topProductsData[idx];
                if (y + ROW_H > p.Height) break;

                int midY = y + ROW_H / 2;
                bool hover = idx == _topProductsHoverIndex;

                // ── Hover background on name column ───────────────────────────
                if (hover)
                {
                    using var hoverPath = RoundRect(
                        new Rectangle(PAD_L - 4, y + 4, NAME_W + 8, ROW_H - 8), 6);
                    g.FillPath(new SolidBrush(Color.FromArgb(18, 59, 130, 246)), hoverPath);
                }

                // ── Divider ───────────────────────────────────────────────────
                if (idx > 0)
                {
                    using var divPen = new Pen(Color.FromArgb(228, 236, 250), 1f);
                    g.DrawLine(divPen, PAD_L, y, rightEdge, y);
                }

                // ── [LEFT] Product name ───────────────────────────────────────
                using var nameSf = new StringFormat
                {
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap,
                    LineAlignment = StringAlignment.Center
                };

                // Hover: blue + underline; Normal: navy
                Font nameFont = hover
                    ? new Font("Segoe UI", F_H3.Size, FontStyle.Bold | FontStyle.Underline)
                    : F_H3;
                Color nameColor = hover ? C_Blue : C_Navy;

                g.DrawString(prod.ItemName, nameFont, new SolidBrush(nameColor),
                    new RectangleF(PAD_L, midY - 10, NAME_W, 20), nameSf);

                // ── [MIDDLE] Bar track ────────────────────────────────────────
                int barY = midY - BAR_H / 2;
                var trackRect = new Rectangle(barColX, barY, barMaxW, BAR_H);
                using (var trackPath = RoundRect(trackRect, 3))
                    g.FillPath(new SolidBrush(Color.FromArgb(228, 236, 252)), trackPath);

                // Bar fill
                int fillW = Math.Max((int)(barMaxW * prod.BarPercent / 100.0 * _topProdAnimProgress), 0);
                if (fillW > 0)
                {
                    var fillRect = new Rectangle(barColX, barY, Math.Max(fillW, 6), BAR_H);
                    using var fillPath = RoundRect(fillRect, 3);

                    // Brighter gradient on hover
                    Color gradStart = hover
                        ? Color.FromArgb(147, 197, 253)
                        : Color.FromArgb(99, 179, 255);
                    Color gradEnd = hover
                        ? Color.FromArgb(37, 99, 235)
                        : C_Blue;

                    using var grad = new System.Drawing.Drawing2D.LinearGradientBrush(
                        fillRect, gradStart, gradEnd,
                        System.Drawing.Drawing2D.LinearGradientMode.Horizontal);
                    g.FillPath(grad, fillPath);
                }

                // ── [PCT] Percentage ──────────────────────────────────────────
                int pctX = barColX + barMaxW + 4;
                using var pctSf = new StringFormat
                {
                    Alignment = StringAlignment.Near,
                    LineAlignment = StringAlignment.Center
                };
                //g.DrawString($"{prod.BarPercent:N0}%",
                //    new Font("Segoe UI", 7.5F, FontStyle.Bold),
                //    new SolidBrush(hover ? C_Blue : C_Blue),
                //    new RectangleF(pctX, midY - 10, PCT_W, 20), pctSf);

                // ── [RIGHT] Sold label ────────────────────────────────────────
                int soldX = rightEdge - SOLD_W;
                using var soldSf = new StringFormat
                {
                    Alignment = StringAlignment.Far,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString($"Sold: {prod.TotalSold:N0}",
                    F_Micro,
                    new SolidBrush(hover ? C_Navy : C_Slate),
                    new RectangleF(soldX, midY - 10, SOLD_W, 20), soldSf);

                y += ROW_H;
            }
        }

        // ── PAINT: Low Stock ──────────────────────────────────────────────
        private void PaintLowStock(object sender, PaintEventArgs e)
        {
            var p = (Panel)sender;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            g.DrawString("Low Stock", F_H2, new SolidBrush(C_Navy), new PointF(16, 18));

            if (_lowStockData == null || _lowStockData.Count == 0)
            {
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("✓  All stock levels healthy.",
                    new Font("Segoe UI", 9F, FontStyle.Bold),
                    new SolidBrush(C_Green),
                    new RectangleF(0, 40, p.Width, p.Height - 40), sf);
                return;
            }

            int y = 62;
            foreach (var item in _lowStockData)
            {
                string name = item.ItemName.Length > 18 ? item.ItemName.Substring(0, 16) + "…" : item.ItemName;
                g.DrawString(name, F_H3, new SolidBrush(C_Navy), new PointF(16, y));
                g.DrawString($"Qty: {item.CurrentQty:F0}", F_Micro, new SolidBrush(C_Slate), new PointF(16, y + 17));

                bool isCrit = item.Status == "Critical";
                var badgeBg = isCrit ? Color.FromArgb(255, 228, 228) : Color.FromArgb(255, 244, 210);
                var badgeFg = isCrit ? C_Red : C_Amber;
                var badgeRc = new Rectangle(p.Width - 74, y + 3, 58, 20);
                using var badgePath = RoundRect(badgeRc, 10);
                g.FillPath(new SolidBrush(badgeBg), badgePath);
                using var sfC = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(item.Status, new Font("Segoe UI", 7.5F, FontStyle.Bold),
                    new SolidBrush(badgeFg), badgeRc, sfC);

                if (y + 46 < p.Height - 10)
                    g.DrawLine(new Pen(Color.FromArgb(238, 242, 252), 1), 16, y + 40, p.Width - 16, y + 40);

                y += 46;
                if (y + 46 > p.Height) break;
            }
        }

        // ── Transactions Grid ─────────────────────────────────────────────
        private void BuildTransactionCard(Panel card)
        {
            card.Paint += (s, e) =>
            {
                e.Graphics.DrawString("Recent Transactions", F_H2, new SolidBrush(C_Navy), new PointF(18, 18));
            };

            // Dock=Fill + card.Padding (rather than a fixed Location/Size +
            // Anchor) reserves room for the header above and recalculates
            // fresh against the card's *current* size on every layout pass —
            // so it's correct immediately even though `card` is still 0x0 at
            // this point (it hasn't been placed into its TableLayoutPanel
            // cell yet), unlike Anchor which would freeze in a bad initial
            // delta computed from that 0x0 size.
            card.Padding = new Padding(14, 60, 14, 14);
            dgvTransactions = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = C_White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(238, 242, 250),
                Font = F_Body,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            dgvTransactions.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(248, 250, 255),
                ForeColor = C_Slate,
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                Padding = new Padding(6, 7, 4, 7)
            };
            dgvTransactions.DefaultCellStyle = new DataGridViewCellStyle
            {
                SelectionBackColor = Color.FromArgb(232, 241, 255),
                SelectionForeColor = C_Navy,
                ForeColor = C_Navy,
                BackColor = C_White,
                Padding = new Padding(6, 6, 4, 6)
            };
            dgvTransactions.ColumnHeadersHeightSizeMode =
                DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            dgvTransactions.ColumnHeadersHeight = 36;
            dgvTransactions.RowTemplate.Height = 36;
            dgvTransactions.EnableHeadersVisualStyles = false;

            // ── Columns ───────────────────────────────────────────────────────────
            dgvTransactions.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "InvoiceNo",
                HeaderText = "Invoice",
                FillWeight = 22
            });
            dgvTransactions.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "CustomerName",
                HeaderText = "Customer",
                FillWeight = 28
            });
            dgvTransactions.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "GrandTotal",
                HeaderText = "Amount",
                FillWeight = 18,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleRight,
                    Padding = new Padding(4, 6, 12, 6)
                },
                HeaderCell =
        {
            Style = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
                Padding   = new Padding(4, 7, 12, 7)
            }
        }
            });
            dgvTransactions.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "SaleDate",
                HeaderText = "Date",
                FillWeight = 18
            }); 

            // ── Cell formatting ───────────────────────────────────────────────────
            dgvTransactions.CellFormatting += (s, ev) =>
            {
                if (ev.RowIndex < 0 || ev.Value == null) return;

                string colName = dgvTransactions.Columns[ev.ColumnIndex].Name;

                // Amount: split symbol (left) and number (right)
                if (colName == "GrandTotal")
                {
                    string raw = ev.Value.ToString() ?? "";

                    string sym = "";
                    string num = raw;

                    // Find first digit to split symbol from number
                    int firstDigit = -1;
                    for (int ci = 0; ci < raw.Length; ci++)
                    {
                        if (char.IsDigit(raw[ci]))
                        { firstDigit = ci; break; }
                    }

                    if (firstDigit > 0)
                    {
                        sym = raw.Substring(0, firstDigit).Trim();
                        num = raw.Substring(firstDigit).Trim();
                    }

                    // Store symbol in Tag for CellPainting
                    dgvTransactions.Rows[ev.RowIndex].Cells[ev.ColumnIndex].Tag = sym;

                    // Show only the number — symbol drawn separately via CellPainting
                    ev.Value = num;
                    ev.FormattingApplied = true;
                    ev.CellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    ev.CellStyle.Padding = new Padding(4, 6, 12, 6);
                    return;
                }

                // Status colour
                if (colName == "Status")
                {
                    switch (ev.Value.ToString())
                    {
                        case "Paid": ev.CellStyle.ForeColor = C_Green; break;
                        case "Unpaid": ev.CellStyle.ForeColor = C_Amber; break;
                        case "Void": ev.CellStyle.ForeColor = C_Purple; break;
                    }
                    ev.CellStyle.Font = new Font("Segoe UI", 8.5F );
                }

                // Invoice No colour
                if (colName == "InvoiceNo")
                    ev.CellStyle.ForeColor = C_Blue;
            };

            // ── Cell painting — currency symbol LEFT, amount RIGHT ────────────────
            dgvTransactions.CellPainting += (s, ev) =>
            {
                if (ev.RowIndex < 0) return;
                if (dgvTransactions.Columns[ev.ColumnIndex].Name != "GrandTotal") return;
                if (ev.Value == null) return;

                ev.Handled = true;
                ev.PaintBackground(ev.ClipBounds, true);

                string sym = dgvTransactions.Rows[ev.RowIndex]
                                 .Cells[ev.ColumnIndex].Tag?.ToString() ?? "P";
                string num = ev.FormattedValue?.ToString() ?? "";

                using var fSym = new Font("Segoe UI", 8F, FontStyle.Regular);
                using var fNum = new Font("Segoe UI", 9F);

                bool selected = (ev.State & DataGridViewElementStates.Selected) != 0;
                Color fgNum = selected ? C_Navy : C_Navy;
                Color fgSym = selected ? C_Slate : C_Slate;

                // Symbol — left edge
                var symSz = ev.Graphics.MeasureString(sym, fSym);
                float symX = ev.CellBounds.Left + 8;
                float symY = ev.CellBounds.Top + (ev.CellBounds.Height - symSz.Height) / 2f;
                ev.Graphics.DrawString(sym, fSym, new SolidBrush(fgSym), symX, symY);

                // Number — right edge
                var numSz = ev.Graphics.MeasureString(num, fNum);
                float numX = ev.CellBounds.Right - numSz.Width - 12;
                float numY = ev.CellBounds.Top + (ev.CellBounds.Height - numSz.Height) / 2f;
                ev.Graphics.DrawString(num, fNum, new SolidBrush(fgNum), numX, numY);
            };

            // ── Invoice hover highlight ───────────────────────────────────────────
            dgvTransactions.CellMouseEnter += (s, ev) =>
            {
                if (ev.RowIndex < 0) return;
                if (dgvTransactions.Columns[ev.ColumnIndex].Name != "InvoiceNo") return;

                dgvTransactions.Cursor = Cursors.Hand;
                var cell = dgvTransactions.Rows[ev.RowIndex].Cells["InvoiceNo"];
                cell.Style.BackColor = Color.FromArgb(224, 238, 255);
                cell.Style.ForeColor = C_Blue;
                cell.Style.Font = new Font("Segoe UI", 9F, FontStyle.Bold | FontStyle.Underline);
            };

            dgvTransactions.CellMouseLeave += (s, ev) =>
            {
                if (ev.RowIndex < 0 || ev.ColumnIndex < 0
                    || ev.ColumnIndex >= dgvTransactions.Columns.Count) return;
                if (dgvTransactions.Columns[ev.ColumnIndex].Name != "InvoiceNo") return;

                dgvTransactions.Cursor = Cursors.Default;
                var cell = dgvTransactions.Rows[ev.RowIndex].Cells["InvoiceNo"];
                cell.Style.BackColor = C_White;
                cell.Style.ForeColor = C_Blue;
                cell.Style.Font = F_Body;
            };

            // ── Invoice click → copy with animation ──────────────────────────────
            dgvTransactions.CellClick += (s, ev) =>
            {
                if (ev.RowIndex < 0 || ev.ColumnIndex < 0) return;
                if (dgvTransactions.Columns[ev.ColumnIndex].Name != "InvoiceNo") return;

                string invoiceNo = dgvTransactions.Rows[ev.RowIndex]
                                       .Cells["InvoiceNo"].Value?.ToString() ?? "";
                if (string.IsNullOrEmpty(invoiceNo) || invoiceNo == "—") return;

                // 1. Copy to clipboard
                Clipboard.SetText(invoiceNo);

                // 2. Flash cell green → "Copied!" → restore
                var cell = dgvTransactions.Rows[ev.RowIndex].Cells["InvoiceNo"];
                cell.Style.BackColor = Color.FromArgb(214, 248, 232);
                cell.Style.ForeColor = C_Green;
                cell.Style.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
                string originalValue = cell.Value?.ToString() ?? "";
                cell.Value = "✓  Copied!";

                // 3. Floating label that fades upward
                var cellRect = dgvTransactions.GetCellDisplayRectangle(
                                   ev.ColumnIndex, ev.RowIndex, false);
                var screenPt = dgvTransactions.PointToScreen(cellRect.Location);
                var formPt = this.PointToClient(screenPt);

                var floatLbl = new Label
                {
                    Text = "✓ Copied!",
                    Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = C_Green,
                    AutoSize = false,
                    Size = new Size(90, 26),
                    Location = new Point(formPt.X + cellRect.Width / 2 - 45, formPt.Y - 10),
                    TextAlign = ContentAlignment.MiddleCenter
                };
                floatLbl.Region = new Region(RoundRect(new Rectangle(0, 0, 90, 26), 13));
                this.Controls.Add(floatLbl);
                floatLbl.BringToFront();

                // 4. Animate upward + fade, then restore cell
                int step = 0;
                var anim = new System.Windows.Forms.Timer { Interval = 18 };
                anim.Tick += (ts, te) =>
                {
                    step++;
                    floatLbl.Top -= 2;

                    int alpha = Math.Max(0, 255 - step * 14);
                    if (alpha <= 0 || step >= 20)
                    {
                        anim.Stop();
                        anim.Dispose();
                        this.Controls.Remove(floatLbl);
                        floatLbl.Dispose();

                        if (ev.RowIndex < dgvTransactions.Rows.Count)
                        {
                            cell.Value = originalValue;
                            cell.Style.BackColor = C_White;
                            cell.Style.ForeColor = C_Blue;
                            cell.Style.Font = F_Body;
                        }
                    }
                };
                anim.Start();
            };

            card.Controls.Add(dgvTransactions);
        }

        // ── Quick Actions ─────────────────────────────────────────────────
        private void BuildQuickActionsCard(Panel card)
        {
            card.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                g.DrawString(
                    "Quick Actions",
                    F_H2,
                    new SolidBrush(C_Navy),
                    new PointF(16, 16));
            };

            var actions = new[]
            {
        new { Icon = "🛒", Lbl = "New Sale", Clr = C_Blue,   Bg = Color.FromArgb(222, 235, 255) },
        new { Icon = "📊", Lbl = "Reports",  Clr = C_Green,  Bg = Color.FromArgb(214, 248, 232) },
        new { Icon = "💰", Lbl = "Payment",  Clr = C_Pink,   Bg = Color.FromArgb(252, 222, 238) },
        new { Icon = "↩",  Lbl = "Return",   Clr = C_Red,    Bg = Color.FromArgb(255, 222, 222) },
    };

            const int cols = 2;
            const int rows = 2;
            const int gap = 10;

            card.Padding = new Padding(14, 54, 14, 14);

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = cols,
                RowCount = rows,
                BackColor = Color.Transparent,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));

            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            for (int i = 0; i < actions.Length; i++)
            {
                var a = actions[i];

                int col = i % cols;
                int row = i / cols;

                var btn = new Button
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(
                        col == 0 ? 0 : gap / 2,
                        row == 0 ? 0 : gap / 2,
                        col == cols - 1 ? 0 : gap / 2,
                        row == rows - 1 ? 0 : gap / 2),

                    FlatStyle = FlatStyle.Flat,
                    BackColor = a.Bg,
                    ForeColor = C_Navy,
                    Cursor = Cursors.Hand,
                    Text = "",
                    TabStop = false,
                    UseVisualStyleBackColor = false
                };

                btn.FlatAppearance.BorderSize = 0;
                btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(
                    Math.Max(0, a.Bg.R - 10),
                    Math.Max(0, a.Bg.G - 10),
                    Math.Max(0, a.Bg.B - 10));

                // Rounded corners
                void ApplyRegion()
                {
                    if (btn.Width < 2 || btn.Height < 2)
                        return;

                    btn.Region?.Dispose();

                    int radius = Math.Min(
                        12,
                        Math.Min(btn.Width, btn.Height) / 4);

                    if (radius <= 0)
                        return;

                    using var path = RoundRect(
                        new Rectangle(0, 0, btn.Width - 1, btn.Height - 1),
                        radius);

                    btn.Region = new Region(path);
                }

                btn.Resize += (s, e) => ApplyRegion();
                ApplyRegion();

                btn.Paint += (s, e) =>
                {
                    var g = e.Graphics;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint =
                        System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                    int w = btn.ClientSize.Width;
                    int h = btn.ClientSize.Height;

                    if (w <= 10 || h <= 10)
                        return;

                    // ─────────────────────────────────────────────
                    // Responsive sizing
                    // ─────────────────────────────────────────────

                    // Icon badge scales with available button size.
                    int badgeSize = Math.Min(
                        42,
                        Math.Max(30, Math.Min(w - 18, h / 3)));

                    int badgeX = (w - badgeSize) / 2;

                    // Keep everything centered vertically.
                    int totalContentHeight =
                        badgeSize + 8 + 20;

                    int startY = Math.Max(
                        8,
                        (h - totalContentHeight) / 2);

                    // ─────────────────────────────────────────────
                    // Icon background
                    // ─────────────────────────────────────────────

                    var iconRc = new Rectangle(
                        badgeX,
                        startY,
                        badgeSize,
                        badgeSize);

                    int iconRadius = Math.Min(11, badgeSize / 3);

                    using (var iconPath = RoundRect(
                        iconRc,
                        iconRadius))
                    using (var iconBg = new SolidBrush(Color.White))
                    {
                        g.FillPath(iconBg, iconPath);
                    }

                    // ─────────────────────────────────────────────
                    // Icon
                    // ─────────────────────────────────────────────

                    float iconFontSize = Math.Max(
                        12f,
                        Math.Min(17f, badgeSize * 0.40f));

                    using var iconFont =
                        new Font("Segoe UI Emoji", iconFontSize);

                    using var iconSf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center
                    };

                    g.DrawString(
                        a.Icon,
                        iconFont,
                        new SolidBrush(a.Clr),
                        iconRc,
                        iconSf);

                    // ─────────────────────────────────────────────
                    // Label
                    // ─────────────────────────────────────────────

                    int labelY = startY + badgeSize + 7;

                    using var labelFont =
                        new Font(
                            "Segoe UI",
                            8.5F,
                            FontStyle.Bold);

                    using var labelSf = new StringFormat
                    {
                        Alignment = StringAlignment.Center,
                        LineAlignment = StringAlignment.Center,
                        Trimming = StringTrimming.EllipsisCharacter,
                        FormatFlags = StringFormatFlags.NoWrap
                    };

                    var labelRc = new RectangleF(
                        6,
                        labelY,
                        w - 12,
                        20);

                    g.DrawString(
                        a.Lbl,
                        labelFont,
                        new SolidBrush(C_Navy),
                        labelRc,
                        labelSf);
                };

                // ─────────────────────────────────────────────
                // Actions
                // ─────────────────────────────────────────────

                if (a.Lbl == "New Sale")
                {
                    btn.Click += (s, e2) =>
                    {
                        SetActiveNav(btnNavSales);

                        ShowPage(
                            new SalesForm(_selectedCompanyId));
                    };
                }

                if (a.Lbl == "Return")
                {
                    btn.Click += (s, e2) =>
                    {
                        SetActiveNav(btnNavSalesReturn);

                        ShowPage(
                            new SalesReturnForm(
                                _selectedCompanyId,
                                _currencySymbol));
                    };
                }

                if (a.Lbl == "Reports")
                {
                    btn.Click += (s, e2) =>
                        ShowReportsPopup();
                }

                grid.Controls.Add(btn, col, row);
            }

            card.Controls.Add(grid);
        }
    }
}