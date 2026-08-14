using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace POSAPP.Shift
{
    public class TenderDeclarationForm : Form
    {
        // ── Palette ───────────────────────────────────────────────────────
        private static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private static readonly Color PanelDark = Color.FromArgb(32, 35, 44);
        private static readonly Color PanelDark2 = Color.FromArgb(42, 46, 56);
        private static readonly Color SidebarBg = Color.FromArgb(26, 29, 38);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccOrange = Color.FromArgb(251, 146, 60);
        private static readonly Color AccYellow = Color.FromArgb(234, 179, 8);
        private static readonly Color AccRed = Color.FromArgb(239, 68, 68);
        private static readonly Color TextWhite = Color.FromArgb(240, 242, 246);
        private static readonly Color TextMuted = Color.FromArgb(130, 140, 158);
        private static readonly Color TextGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color Border = Color.FromArgb(50, 54, 66);
        private static readonly Color InputBg = Color.FromArgb(28, 32, 42);

        // ── Denominations ─────────────────────────────────────────────────
        private struct Denom { public string Label; public decimal Value; public bool IsNote; }
        private static readonly Denom[] Denoms =
        {
            new Denom { Label="P 200",    Value=200m,  IsNote=true  },
            new Denom { Label="P 100",    Value=100m,  IsNote=true  },
            new Denom { Label="P 50",     Value= 50m,  IsNote=true  },
            new Denom { Label="P 20",     Value= 20m,  IsNote=true  },
            new Denom { Label="P 10",     Value= 10m,  IsNote=true  },
            new Denom { Label="P 5",      Value=  5m,  IsNote=false },
            new Denom { Label="P 2",      Value=  2m,  IsNote=false },
            new Denom { Label="P 1",      Value=  1m,  IsNote=false },
            new Denom { Label="50 cents", Value= .50m, IsNote=false },
            new Denom { Label="20 cents", Value= .20m, IsNote=false },
            new Denom { Label="10 cents", Value= .10m, IsNote=false },
        };

        // Column x-positions inside the scrollable cash panel
        private const int ColLabel = 0;
        private const int ColQty = 220;
        private const int ColMult = 320;
        private const int ColSub = 460;
        private const int InnerW = 600;

        // Sidebar inner usable width
        private const int SB_W = 380;   // total sidebar width
        private const int SB_PAD = 20;    // left/right padding inside sidebar
        private const int SB_INNER = SB_W - SB_PAD * 2;  // 340 usable px

        // ── Fields ────────────────────────────────────────────────────────
        private readonly string _sym;
        private readonly int _closingUserId;

        private TextBox[] _denomQty = [];
        private Label[] _denomRowTotals = [];
        private Label? _cashTabTotalLabel;

        private Label? _lblCashTotal, _lblCardTotal, _lblBankTotal;
        private Label? _lblGrandTotal;
        private Label? _lblExpectedFloat, _lblVariance;
        private Panel? _pnlVarianceBar;
        private Button? _btnConfirm;

        private Panel? _tabCash, _tabCard, _tabBank;
        private Button? _btnTabCash, _btnTabCard, _btnTabBank;

        private TextBox? _txtCardAmt, _txtBankAmt;

        // ══════════════════════════════════════════════════════════════════
        public TenderDeclarationForm(int closingUserId,
                                     string currencySymbol = "P",
                                     int companyid = 1)
        {
            _closingUserId = closingUserId;
            _sym = string.IsNullOrWhiteSpace(currencySymbol) ? "P" : currencySymbol.Trim();
            Task _ = ShiftState.RefreshShiftStatusAsync(companyid, _closingUserId);
            InitForm();
            BuildUI();
            LoadExistingDeclaration();
        }

        private void InitForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgDark;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.DoubleBuffer, true);
            UpdateStyles();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        // ══════════════════════════════════════════════════════════════════
        //  BUILD UI
        // ══════════════════════════════════════════════════════════════════
        private void BuildUI()
        {
            // ── Header ────────────────────────────────────────────────────
            var header = new Panel
            {
                BackColor = PanelDark2,
                Dock = DockStyle.Top,
                Height = 54
            };

            var lblTitle = new Label
            {
                Text = "💵  Tender Declaration",
                Font = new Font("Segoe UI", 13F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(18, 0, 0, 0)
            };

            var btnX = new Button
            {
                Text = "✕",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(54, 54),
                Dock = DockStyle.Right,
                Cursor = Cursors.Hand
            };
            btnX.FlatAppearance.BorderSize = 0;
            btnX.Click += (s, e) => Close();
            btnX.MouseEnter += (s, e) => btnX.BackColor = Color.FromArgb(196, 30, 58);
            btnX.MouseLeave += (s, e) => btnX.BackColor = Color.Transparent;
            header.Controls.Add(lblTitle);
            header.Controls.Add(btnX);

            var mainLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BgDark,
                ColumnCount = 1,
                RowCount = 2,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = BgDark,
                ColumnCount = 2,
                RowCount = 1,
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, SB_W));

            var sidebar = BuildSidebar();
            sidebar.Dock = DockStyle.Fill;

            var leftCol = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = BgDark,
                AutoScroll = true,
                Padding = new Padding(18, 12, 18, 12)
            };

            var leftLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 2,
                ColumnCount = 1
            };

            leftLayout.RowStyles.Clear();
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // main content
            leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));  // fixed float height
            leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // footer

            var scrollArea = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            //var floatCard = BuildFloatCard();
           // floatCard.Dock = DockStyle.Fill;
           // floatCard.MinimumSize = new Size(0, 72);

            leftLayout.Controls.Add(scrollArea, 0, 0);
            //leftLayout.Controls.Add(floatCard, 0, 1);

            // Tab bar
            var tabBar = new Panel
            {
                BackColor = Color.FromArgb(28, 32, 42),
                Dock = DockStyle.Top,
                Height = 46,
                Padding = new Padding(0)
            };

            // Bottom border on tab bar
            tabBar.Paint += (s, pe) =>
            {
                using var pen = new Pen(Border, 1f);
                pe.Graphics.DrawLine(pen, 0, tabBar.Height - 1, tabBar.Width, tabBar.Height - 1);
            };

            _btnTabCash = MakeTabBtn("💵  Cash", 0);
            _btnTabCard = MakeTabBtn("💳  Card", 200);
            _btnTabBank = MakeTabBtn("🏦  Bank", 400);
            _btnTabCash.Click += (s, e) => ShowTab(0);
            _btnTabCard.Click += (s, e) => ShowTab(1);
            _btnTabBank.Click += (s, e) => ShowTab(2);
            tabBar.Controls.AddRange(new Control[] { _btnTabCash, _btnTabCard, _btnTabBank });

            // Tab host
            var tabHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            _tabCash = BuildCashTab();
            _tabCard = BuildCardTab();
            _tabBank = BuildDigitalTab("Bank Transfer", "🏦", out _txtBankAmt);

            foreach (var p in new[] { _tabCash, _tabCard, _tabBank })
            {
                p.Dock = DockStyle.Fill;
                p.Visible = false;
                tabHost.Controls.Add(p);
            }
            _tabCash.Visible = true;
            // Set docking FIRST
            tabBar.Dock = DockStyle.Top;
            tabHost.Dock = DockStyle.Top;
            //floatCard.Dock = DockStyle.Top;

            tabBar.Dock = DockStyle.Top;
            tabHost.Dock = DockStyle.Fill;

            scrollArea.Controls.Add(tabHost);
            scrollArea.Controls.Add(tabBar);
            //floatCard.Dock = DockStyle.Fill;

            // Add controls in correct order
            leftCol.Controls.Add(leftLayout);

            body.Controls.Add(leftCol, 0, 0);
            body.Controls.Add(sidebar, 1, 0);

            //var denominationCard = BuildCashTab();
            //denominationCard.Dock = DockStyle.Fill;
            //denominationCard.Padding = new Padding(8, 6, 8, 8);

            mainLayout.Controls.Add(header, 0, 0);
            mainLayout.Controls.Add(body, 0, 1);
            //mainLayout.Controls.Add(denominationCard, 0, 2);
            //mainLayout.Controls.Add(floatCard, 0, 3);
            Controls.Add(mainLayout);

            SetActiveTab(_btnTabCash);
            UpdateSummary();
        }

        //private Panel BuildFloatCard()
        //{
        //    var card = new Panel
        //    {
        //        BackColor = Color.FromArgb(28, 42, 36),
        //        Height = 72,
        //        Margin = new Padding(0, 8, 0, 0),
        //        Padding = new Padding(0)
        //    };

        //    card.Controls.Add(new Panel
        //    {
        //        BackColor = AccGreen,
        //        Size = new Size(4, 72),
        //        Location = Point.Empty
        //    });

        //    //card.Controls.Add(new Label
        //    //{
        //    //    Text = "Opening float to verify",
        //    //    Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
        //    //    ForeColor = TextWhite,
        //    //    BackColor = Color.Transparent,
        //    //    AutoSize = false,
        //    //    Size = new Size(260, 72),
        //    //    Location = new Point(18, 0),
        //    //    TextAlign = ContentAlignment.MiddleLeft
        //    //});

        //    //_lblExpectedFloat = new Label
        //    //{
        //    //    Text = Fmt(ShiftState.OpeningFloat),  // ← Should now show 1200, 12, etc.
        //    //    Font = new Font("Segoe UI", 16F, FontStyle.Bold),
        //    //    ForeColor = AccGreen,
        //    //    BackColor = Color.Transparent,
        //    //    Dock = DockStyle.Right,
        //    //    Width = 260,
        //    //    TextAlign = ContentAlignment.MiddleRight,
        //    //    Padding = new Padding(0, 0, 16, 0)
        //    //};
        //    //_lblExpectedFloat.AutoSize = false;
        //    //card.Controls.Add(_lblExpectedFloat);

        //    return card;
        //}

        // ══════════════════════════════════════════════════════════════════
        //  SIDEBAR
        // ══════════════════════════════════════════════════════════════════
        private Panel BuildSidebar()
        {
            var sb = new Panel
            {
                Width = SB_W,
                BackColor = SidebarBg,
                AutoScroll = true,
                Dock = DockStyle.Fill
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                ColumnCount = 1,
                RowCount = 3,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Top section — summary rows
            var topSection = new Panel
            {
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Padding = new Padding(SB_PAD, SB_PAD, SB_PAD, 0)
            };

            int ry = 0;

            // "SUMMARY" section label
            topSection.Controls.Add(new Label
            {
                Text = "SUMMARY",
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(SB_INNER, 20),
                Location = new Point(0, ry),
                TextAlign = ContentAlignment.MiddleLeft
            });
            ry += 28;

            // Summary rows
            _lblCashTotal = SideRowWide(topSection, "💵  Cash counted", ref ry);
            _lblCardTotal = SideRowWide(topSection, "💳  Card", ref ry);
            _lblBankTotal = SideRowWide(topSection, "🏦  Bank transfer", ref ry);

            ry += 6;

            // Divider
            topSection.Controls.Add(new Panel
            {
                BackColor = Border,
                Size = new Size(SB_INNER, 1),
                Location = new Point(0, ry)
            });
            ry += 16;

            // "GRAND TOTAL" caption
            topSection.Controls.Add(new Label
            {
                Text = "GRAND TOTAL",
                Font = new Font("Segoe UI", 8F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(SB_INNER, 20),
                Location = new Point(0, ry),
                TextAlign = ContentAlignment.MiddleLeft
            });
            ry += 28;

            // Grand total VALUE — full width, large font, own row
            _lblGrandTotal = new Label
            {
                Text = Fmt(0),
                Font = new Font("Segoe UI", 24F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(SB_INNER, 52),
                Location = new Point(0, ry),
                TextAlign = ContentAlignment.MiddleRight
            };
            topSection.Controls.Add(_lblGrandTotal);
            ry += 52;

            topSection.Height = Math.Max(220, ry + SB_PAD);
            layout.Controls.Add(topSection, 0, 0);

            // ── Variance bar ──────────────────────────────────────────────
            _pnlVarianceBar = new Panel
            {
                BackColor = Color.FromArgb(60, 239, 68, 68),
                Dock = DockStyle.Top,
                Height = 0,
                Padding = new Padding(SB_PAD, 10, SB_PAD, 10)
            };
            _lblVariance = new Label
            {
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(252, 165, 165),
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _pnlVarianceBar.Controls.Add(_lblVariance);
            layout.Controls.Add(_pnlVarianceBar, 0, 1);

            // ── Bottom buttons ────────────────────────────────────────────
            var btnPanel = new Panel
            {
                BackColor = SidebarBg,
                Dock = DockStyle.Bottom,
                Height = 110,
                Padding = new Padding(SB_PAD, 14, SB_PAD, 20)
            };

            _btnConfirm = new Button
            {
                Text = "✓  Confirm Declaration",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = AccGreen,
                FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Top,
                Height = 46,
                Cursor = Cursors.Hand
            };
            _btnConfirm.FlatAppearance.BorderSize = 0;
            _btnConfirm.Click += BtnConfirm_Click;
            _btnConfirm.MouseEnter += (s, e) =>
            {
                if (_btnConfirm.Enabled)
                    _btnConfirm.BackColor = ControlPaint.Dark(AccGreen, .12f);
            };
            _btnConfirm.MouseLeave += (s, e) =>
                _btnConfirm.BackColor = _btnConfirm.Enabled
                    ? AccGreen : Color.FromArgb(70, 75, 90);

            var btnClose = new Button
            {
                Text = "← Close",
                Font = new Font("Segoe UI", 10F),
                ForeColor = TextMuted,
                BackColor = PanelDark2,
                FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Bottom,
                Height = 38,
                Cursor = Cursors.Hand
            };
            btnClose.FlatAppearance.BorderSize = 0;
            btnClose.Click += (s, e) => Close();

            btnPanel.Controls.Add(_btnConfirm);
            btnPanel.Controls.Add(btnClose);
            layout.Controls.Add(btnPanel, 0, 2);
            sb.Controls.Add(layout);

            // Left border separator
            sb.Paint += (s, pe) =>
            {
                using var pen = new Pen(Border, 1f);
                pe.Graphics.DrawLine(pen, 0, 0, 0, sb.Height);
            };

            return sb;
        }

        // ══════════════════════════════════════════════════════════════════
        //  CASH TAB
        // ══════════════════════════════════════════════════════════════════
        private Panel BuildCashTab()
        {
            var outer = new Panel { BackColor = Color.Transparent };
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var inner = new Panel { Width = InnerW };

            int y = 8;

            // Column headers
            inner.Controls.Add(MakeColHdr("Denomination", ColLabel, y, 200, false));
            inner.Controls.Add(MakeColHdr("Qty", ColQty, y, 90, true));
            inner.Controls.Add(MakeColHdr("× Value", ColMult, y, 130, true));
            inner.Controls.Add(MakeColHdr("Subtotal", ColSub, y, 130, true));
            inner.Controls.Add(HLine(y + 24));
            y += 30;

            _denomQty = new TextBox[Denoms.Length];
            _denomRowTotals = new Label[Denoms.Length];

            const int rowH = 36;
            bool prevNote = true;

            for (int i = 0; i < Denoms.Length; i++)
            {
                var d = Denoms[i];

                if (i == 0)
                {
                    inner.Controls.Add(SectionLbl("💵  Notes", AccGreen, y));
                    y += 22;
                }
                else if (!d.IsNote && prevNote)
                {
                    inner.Controls.Add(HLine(y));
                    y += 4;
                    inner.Controls.Add(SectionLbl("🪙  Coins", AccYellow, y));
                    y += 22;
                }
                prevNote = d.IsNote;

                int idx = i;

                // Denomination label
                inner.Controls.Add(new Label
                {
                    Text = d.Label,
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    ForeColor = TextWhite,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(200, rowH),
                    Location = new Point(ColLabel, y),
                    TextAlign = ContentAlignment.MiddleLeft
                });

                // Qty input
                var qty = new TextBox
                {
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    ForeColor = TextWhite,
                    BackColor = InputBg,
                    BorderStyle = BorderStyle.FixedSingle,
                    Text = "0",
                    Size = new Size(72, 28),
                    Location = new Point(ColQty, y + (rowH - 28) / 2),
                    TextAlign = HorizontalAlignment.Center
                };
                qty.Enter += (s, e) => { if (qty.Text == "0") qty.Text = ""; qty.SelectAll(); };
                qty.Leave += (s, e) => { if (string.IsNullOrWhiteSpace(qty.Text)) qty.Text = "0"; RecalcCash(); };
                qty.KeyPress += NumericIntOnly;
                qty.TextChanged += (s, e) => RecalcCash();
                _denomQty[idx] = qty;
                inner.Controls.Add(qty);

                // × Value
                inner.Controls.Add(new Label
                {
                    Text = $"× {Fmt(d.Value)}",
                    Font = new Font("Segoe UI", 9.5F),
                    ForeColor = TextMuted,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(130, rowH),
                    Location = new Point(ColMult, y),
                    TextAlign = ContentAlignment.MiddleRight
                });

                // Subtotal
                var sub = new Label
                {
                    Text = "—",
                    Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                    ForeColor = TextMuted,
                    BackColor = Color.Transparent,
                    AutoSize = false,
                    Size = new Size(130, rowH),
                    Location = new Point(ColSub, y),
                    TextAlign = ContentAlignment.MiddleRight
                };
                _denomRowTotals[idx] = sub;
                inner.Controls.Add(sub);

                y += rowH + 2;
            }

            // Cash total footer
            y += 8;
            inner.Controls.Add(new Panel { BackColor = AccOrange, Size = new Size(InnerW, 2), Location = new Point(0, y) });
            y += 12;

            // Footer background
            var footerBg = new Panel
            {
                BackColor = Color.FromArgb(28, 32, 42),
                Size = new Size(InnerW, 44),
                Location = new Point(0, y)
            };

            footerBg.Controls.Add(new Label
            {
                Text = "Cash total",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(300, 44),
                Location = new Point(12, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            _cashTabTotalLabel = new Label
            {
                Text = Fmt(0),
                Font = new Font("Segoe UI", 15F, FontStyle.Bold),
                ForeColor = TextGreen,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(280, 44),
                Location = new Point(InnerW - 292, 0),
                TextAlign = ContentAlignment.MiddleRight
            };
            footerBg.Controls.Add(_cashTabTotalLabel);
            inner.Controls.Add(footerBg);

            inner.MinimumSize = new Size(InnerW, y + 56);
            // inner is a fixed-width table (denomination rows use fixed
            // column x-offsets, which doesn't lend itself to percentage
            // reflow the way a card grid does) — but Dock=Top would stretch
            // it to the FULL width of `scroll`, leaving the fixed-position
            // rows clustered on the left with a large empty gap on wide
            // screens (this form is always opened maximized). Instead, keep
            // it at its designed width and re-center it horizontally
            // whenever the scroll area resizes, so it makes sense at any
            // screen width instead of just hugging the left edge.
            inner.Size = new Size(InnerW, y + 56);
            void CenterInner()
            {
                int cx = Math.Max(0, (scroll.ClientSize.Width - inner.Width) / 2);
                inner.Location = new Point(cx, 0);
            }
            CenterInner();
            scroll.Resize += (s, e) => CenterInner();
            scroll.Controls.Add(inner);
            outer.Controls.Add(scroll);
            return outer;
        }

        // ══════════════════════════════════════════════════════════════════
        //  CARD TAB
        // ══════════════════════════════════════════════════════════════════
        private Panel BuildCardTab()
        {
            var pnl = new Panel { BackColor = Color.Transparent };

            pnl.Controls.Add(new Label
            {
                Text = "💳  Card — amount collected",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = AccOrange,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(600, 30),
                Location = new Point(0, 16),
                TextAlign = ContentAlignment.MiddleLeft
            });

            _txtCardAmt = MakeDigitalInput(pnl, $"Amount received ({_sym})", 58);
            _txtCardAmt.KeyPress += NumericDecOnly;
            _txtCardAmt.TextChanged += (s, e) => UpdateSummary();
            return pnl;
        }

        // ══════════════════════════════════════════════════════════════════
        //  BANK TAB
        // ══════════════════════════════════════════════════════════════════
        private Panel BuildDigitalTab(string title, string icon, out TextBox tbAmt)
        {
            var pnl = new Panel { BackColor = Color.Transparent };

            pnl.Controls.Add(new Label
            {
                Text = $"{icon}  {title} — amount collected",
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = AccOrange,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(600, 30),
                Location = new Point(0, 16),
                TextAlign = ContentAlignment.MiddleLeft
            });

            tbAmt = MakeDigitalInput(pnl, $"Amount received ({_sym})", 58);
            tbAmt.KeyPress += NumericDecOnly;
            tbAmt.TextChanged += (s, e) => UpdateSummary();
            return pnl;
        }

        // ══════════════════════════════════════════════════════════════════
        //  RECALC / SUMMARY / VARIANCE
        // ══════════════════════════════════════════════════════════════════
        private void RecalcCash()
        {
            decimal total = 0;
            for (int i = 0; i < Denoms.Length; i++)
            {
                int.TryParse(_denomQty[i]?.Text, out int q);
                decimal sub = Denoms[i].Value * q;
                total += sub;
                if (_denomRowTotals[i] != null)
                {
                    _denomRowTotals[i].Text = q > 0 ? Fmt(sub) : "—";
                    _denomRowTotals[i].ForeColor = q > 0 ? TextWhite : TextMuted;
                }
            }
            if (_cashTabTotalLabel != null) _cashTabTotalLabel.Text = Fmt(total);
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            decimal cash = GetCashTotal();
            decimal card = decimal.TryParse(_txtCardAmt?.Text, out decimal c) ? c : 0;
            decimal bank = decimal.TryParse(_txtBankAmt?.Text, out decimal b) ? b : 0;
            decimal grand = cash + card + bank;

            SetSumLabel(_lblCashTotal ?? new Label(), cash);
            SetSumLabel(_lblCardTotal ?? new Label(), card);
            SetSumLabel(_lblBankTotal ?? new Label(), bank);

            if (_lblGrandTotal != null)
            {
                _lblGrandTotal.Text = Fmt(grand);
                _lblGrandTotal.Font = new Font("Segoe UI", 24F, FontStyle.Bold);
                _lblGrandTotal.ForeColor = grand >= 0 ? TextGreen : AccRed;
            }

            UpdateVarianceBar(grand);
        }

        private void UpdateVarianceBar(decimal grand)
        {
            if (_pnlVarianceBar == null || _lblVariance == null || _btnConfirm == null)
                return;

            if (!ShiftState.IsOpen)
            {
                _pnlVarianceBar.Height = 0;
                _btnConfirm.Enabled = true;
                _btnConfirm.BackColor = AccGreen;
                return;
            }

            decimal expected = ShiftState.OpeningFloat;
            decimal variance = grand - expected;

            if (variance >= 0)
            {
                _pnlVarianceBar.Height = 0;
                _btnConfirm.Enabled = true;
                _btnConfirm.BackColor = AccGreen;
            }
            else
            {
                decimal shortfall = Math.Abs(variance);
                _pnlVarianceBar.Visible = true;
                _pnlVarianceBar.Height = 60;
                _pnlVarianceBar.BackColor = Color.FromArgb(60, 239, 68, 68);
                _lblVariance.Text =
                    $"⚠  Shortfall: {Fmt(shortfall)}\n" +
                    $"Declared {Fmt(grand)}  vs  expected {Fmt(expected)}";
                _btnConfirm.Enabled = false;
                _btnConfirm.BackColor = Color.FromArgb(70, 75, 90);
            }
        }

        private decimal GetCashTotal()
        {
            decimal t = 0;
            if (_denomQty == null) return t;
            for (int i = 0; i < Denoms.Length; i++)
            {
                int.TryParse(_denomQty[i]?.Text, out int q);
                t += Denoms[i].Value * q;
            }
            return t;
        }

        // ══════════════════════════════════════════════════════════════════
        //  CONFIRM
        // ══════════════════════════════════════════════════════════════════
        private void BtnConfirm_Click(object? sender, EventArgs e)
        {
            decimal cash = GetCashTotal();
            decimal card = decimal.TryParse(_txtCardAmt?.Text, out decimal c) ? c : 0;
            decimal bank = decimal.TryParse(_txtBankAmt?.Text, out decimal b) ? b : 0;

            decimal expected = ShiftState.IsOpen ? ShiftState.OpeningFloat : 0m;
            decimal variance = (cash + card + bank) - expected;

            if (variance < 0)
            {
                MessageBox.Show(
                    $"Declaration cannot be confirmed.\n\n" +
                    $"Expected : {Fmt(expected)}\n" +
                    $"Declared : {Fmt(cash + card + bank)}\n" +
                    $"Shortfall: {Fmt(Math.Abs(variance))}\n\n" +
                    "Please account for the missing amount before closing.",
                    "Shortfall Detected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                int[] quantities = new int[Denoms.Length];
                for (int i = 0; i < Denoms.Length; i++)
                    int.TryParse(_denomQty[i]?.Text, out quantities[i]);

                int shiftId = ShiftState.ShiftId > 0 ? ShiftState.ShiftId : 1;
                ShiftState.SaveTenderDeclaration(shiftId, _closingUserId, quantities, cash, card, bank);

                string surplusLine = variance > 0 ? $"Surplus : {Fmt(variance)}\n" : "";
                string msg =
                    $"Tender Declaration — {DateTime.Now:dd MMM yyyy HH:mm}\n" +
                    $"──────────────────────────────\n" +
                    $"Opening float  : {Fmt(expected)}\n\n" +
                    $"Cash counted   : {Fmt(cash)}\n" +
                    (card > 0 ? $"Card collected : {Fmt(card)}\n" : "") +
                    (bank > 0 ? $"Bank collected : {Fmt(bank)}\n" : "") +
                    $"──────────────────────────────\n" +
                    $"Grand total    : {Fmt(cash + card + bank)}\n" +
                    surplusLine +
                    "\n✔  Declaration saved — you can now close the shift.";

                MessageBox.Show(msg, "Declaration Saved",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);

                // ── Print AFTER message box, THEN close ───────────────────────
                PrintTenderDeclaration(quantities, cash, card, bank, expected, variance);

                // Close() is called here — AFTER print dialog is fully done
                if (!IsDisposed) Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving declaration:\n" + ex.Message,
                    "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        // ══════════════════════════════════════════════════════════════════
        //  PRINT TENDER DECLARATION — A4
        // ══════════════════════════════════════════════════════════════════
        private void PrintTenderDeclaration(
            int[] quantities, decimal cash, decimal card, decimal bank,
            decimal expected, decimal variance)
        {
            var doc = new System.Drawing.Printing.PrintDocument();
            doc.DefaultPageSettings.PaperSize =
                new System.Drawing.Printing.PaperSize("A4", 827, 1169); // A4 at 100dpi
            doc.DefaultPageSettings.Landscape = false;
            doc.DefaultPageSettings.Margins =
                new System.Drawing.Printing.Margins(60, 60, 60, 60);

            doc.PrintPage += (s, pe) =>
            {
                var g = pe.Graphics ?? throw new InvalidOperationException("No graphics surface available for printing.");
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                // ── Page bounds ───────────────────────────────────────────────
                int left = pe.MarginBounds.Left;
                int right = pe.MarginBounds.Right;
                int pageW = pe.MarginBounds.Width;   // ~707px
                int y = pe.MarginBounds.Top;

                // ── Fonts ─────────────────────────────────────────────────────
                var fCompany = new Font("Segoe UI", 18F, FontStyle.Bold);
                var fTitle = new Font("Segoe UI", 13F, FontStyle.Bold);
                var fSection = new Font("Segoe UI", 10F, FontStyle.Bold);
                var fNormal = new Font("Segoe UI", 9.5F, FontStyle.Regular);
                var fBold = new Font("Segoe UI", 9.5F, FontStyle.Bold);
                var fSmall = new Font("Segoe UI", 8.5F, FontStyle.Regular);
                var fLargeAmt = new Font("Segoe UI", 20F, FontStyle.Bold);
                var fMedAmt = new Font("Segoe UI", 13F, FontStyle.Bold);

                // ── Brushes ───────────────────────────────────────────────────
                var bBlack = new SolidBrush(Color.FromArgb(30, 30, 30));
                var bGray = new SolidBrush(Color.FromArgb(110, 110, 110));
                var bLtGray = new SolidBrush(Color.FromArgb(180, 180, 180));
                var bGreen = new SolidBrush(Color.FromArgb(22, 163, 74));
                var bRed = new SolidBrush(Color.FromArgb(185, 28, 28));
                var bBlue = new SolidBrush(Color.FromArgb(37, 99, 235));
                var bWhite = new SolidBrush(Color.White);
                var bDarkHdr = new SolidBrush(Color.FromArgb(30, 41, 59));
                var bRowAlt = new SolidBrush(Color.FromArgb(248, 250, 252));

                // ── Helpers ───────────────────────────────────────────────────
                void FillRect(Color c, int rx, int ry, int rw, int rh) =>
                    g.FillRectangle(new SolidBrush(c), rx, ry, rw, rh);

                void DrawStr(string t, Font f, Brush br, int x, int cy,
                             int w = 0, StringAlignment ha = StringAlignment.Near,
                             StringAlignment va = StringAlignment.Near)
                {
                    var sf = new StringFormat
                    { Alignment = ha, LineAlignment = va };
                    g.DrawString(t, f, br,
                        w > 0 ? new RectangleF(x, cy, w, f.Height + 6) : new RectangleF(x, cy, 2000, f.Height + 6),
                        sf);
                }

                void HRule(ref int cy, Color? col = null, float thick = 1f)
                {
                    g.DrawLine(new Pen(col ?? Color.FromArgb(200, 200, 200), thick),
                               left, cy, right, cy);
                    cy += 8;
                }

                void Spacer(ref int cy, int px = 10) => cy += px;

                // Row helper: left label + right value on same line, given row height
                void TableRow(string label, string value,
                              Font fL, Font fV, Brush bL, Brush bV,
                              ref int cy, int rowH, Color? bg = null)
                {
                    if (bg.HasValue) FillRect(bg.Value, left, cy, pageW, rowH);
                    DrawStr(label, fL, bL, left + 10, cy + (rowH - (int)fL.Height) / 2);
                    DrawStr(value, fV, bV, left, cy + (rowH - (int)fV.Height) / 2,
                            pageW - 10, StringAlignment.Far);
                    cy += rowH;
                }

                // ══════════════════════════════════════════════════════════════
                //  [1] COMPANY HEADER BAND
                // ══════════════════════════════════════════════════════════════
                FillRect(Color.FromArgb(30, 41, 59), left - 60, y - 10, pageW + 120, 90);

                DrawStr("TENDER DECLARATION", fTitle, bWhite,
                        left, y + 8, pageW, StringAlignment.Near);

                DrawStr(DateTime.Now.ToString("dd MMM yyyy   HH:mm"),
                        fNormal, bLtGray,
                        left, y + 34, pageW, StringAlignment.Near);

                // Shift badge (right side)
                int shiftId = ShiftState.ShiftId > 0 ? ShiftState.ShiftId : 1;
                FillRect(Color.FromArgb(59, 130, 246), right - 160, y + 10, 160, 60);
                DrawStr($"SHIFT  #{shiftId}", fSection, bWhite,
                        right - 160, y + 22, 160, StringAlignment.Center);

                y += 100;

                // ══════════════════════════════════════════════════════════════
                //  [2] META ROW — operator / date
                // ══════════════════════════════════════════════════════════════
                FillRect(Color.FromArgb(241, 245, 249), left - 60, y, pageW + 120, 32);
                DrawStr($"Operator ID: {_closingUserId}", fSmall, bGray, left + 4, y + 8);
                DrawStr($"Print date: {DateTime.Now:dd/MM/yyyy HH:mm:ss}",
                        fSmall, bGray, left, y + 8, pageW - 4, StringAlignment.Far);
                y += 40;
                Spacer(ref y, 14);

                // ══════════════════════════════════════════════════════════════
                //  [3] CASH BREAKDOWN TABLE
                // ══════════════════════════════════════════════════════════════

                // Table header band
                FillRect(Color.FromArgb(51, 65, 85), left, y, pageW, 30);
                DrawStr("DENOMINATION", fSection, bWhite, left + 10, y + 6);
                DrawStr("QTY", fSection, bWhite, left + 10, y + 6, pageW / 2, StringAlignment.Center);
                DrawStr("UNIT VALUE", fSection, bWhite, left + 10, y + 6, (int)(pageW * 0.75), StringAlignment.Far);
                DrawStr("SUBTOTAL", fSection, bWhite, left, y + 6, pageW - 10, StringAlignment.Far);
                y += 30;

                // Notes section label
                Spacer(ref y, 6);
                DrawStr("▸  NOTES", fSection, bBlue, left + 6, y);
                y += 22;

                bool altRow = false;
                bool hitCoin = false;

                for (int i = 0; i < Denoms.Length; i++)
                {
                    // Coins sub-header
                    if (!Denoms[i].IsNote && !hitCoin)
                    {
                        hitCoin = true;
                        Spacer(ref y, 6);
                        DrawStr("▸  COINS", fSection, bBlue, left + 6, y);
                        y += 22;
                        altRow = false;
                    }

                    Color? bg = altRow ? Color.FromArgb(248, 250, 252) : (Color?)null;
                    altRow = !altRow;

                    int rowH = 26;
                    if (bg.HasValue) FillRect(bg.Value, left, y, pageW, rowH);

                    // Denomination label
                    DrawStr(Denoms[i].Label, fNormal, bBlack, left + 14, y + 4);

                    // Qty (center col)
                    string qtyStr = quantities[i].ToString("N0");
                    DrawStr(qtyStr, quantities[i] > 0 ? fBold : fNormal,
                            quantities[i] > 0 ? bBlack : bLtGray,
                            left, y + 4, pageW / 2, StringAlignment.Center);

                    // Unit value
                    DrawStr(Fmt(Denoms[i].Value), fNormal, bGray,
                            left, y + 4, (int)(pageW * 0.75), StringAlignment.Far);

                    // Subtotal
                    decimal sub = Denoms[i].Value * quantities[i];
                    DrawStr(quantities[i] > 0 ? Fmt(sub) : "—",
                            quantities[i] > 0 ? fBold : fNormal,
                            quantities[i] > 0 ? bBlack : bLtGray,
                            left, y + 4, pageW - 10, StringAlignment.Far);

                    y += rowH;
                }

                Spacer(ref y, 10);
                HRule(ref y, Color.FromArgb(148, 163, 184), 1.5f);

                // Cash total row
                FillRect(Color.FromArgb(240, 253, 244), left, y, pageW, 34);
                DrawStr("CASH TOTAL", fSection, bGreen, left + 10, y + 7);
                DrawStr(Fmt(cash), fMedAmt, bGreen, left, y + 4, pageW - 10, StringAlignment.Far);
                y += 34;
                Spacer(ref y, 14);

                // ══════════════════════════════════════════════════════════════
                //  [4] OTHER PAYMENTS
                // ══════════════════════════════════════════════════════════════
                if (card > 0 || bank > 0)
                {
                    FillRect(Color.FromArgb(51, 65, 85), left, y, pageW, 30);
                    DrawStr("OTHER PAYMENTS", fSection, bWhite, left + 10, y + 6);
                    y += 30;
                    Spacer(ref y, 4);

                    if (card > 0)
                        TableRow("💳  Card / POS", Fmt(card), fNormal, fBold,
                                 bBlack, bBlack, ref y, 28, Color.FromArgb(248, 250, 252));

                    if (bank > 0)
                        TableRow("🏦  Bank Transfer", Fmt(bank), fNormal, fBold,
                                 bBlack, bBlack, ref y, 28);

                    Spacer(ref y, 10);
                }

                // ══════════════════════════════════════════════════════════════
                //  [5] GRAND TOTAL BAND
                // ══════════════════════════════════════════════════════════════ 
                decimal grand = cash + card + bank;

                HRule(ref y, Color.FromArgb(180, 180, 180));

                TableRow("GRAND TOTAL", Fmt(grand), fSection, fSection,
                         bBlack, bBlack, ref y, 30);

                HRule(ref y, Color.FromArgb(180, 180, 180));
                Spacer(ref y, 14);

                // ══════════════════════════════════════════════════════════════
                //  [6] VARIANCE SUMMARY BOX
                // ══════════════════════════════════════════════════════════════
                Color varBg = variance >= 0
                    ? Color.FromArgb(240, 253, 244)
                    : Color.FromArgb(254, 242, 242);
                Color varBdr = variance >= 0
                    ? Color.FromArgb(134, 239, 172)
                    : Color.FromArgb(252, 165, 165);

                FillRect(varBg, left, y, pageW, 80);
                g.DrawRectangle(new Pen(varBdr, 1.5f), left, y, pageW, 80);

                // Three sub-columns inside box
                int col1 = left + 16;
                int col2 = left + pageW / 3;
                int col3 = left + (pageW * 2 / 3);

                DrawStr("Opening Float", fSmall, bGray, col1, y + 10);
                DrawStr("Grand Total", fSmall, bGray, col2, y + 10);
                DrawStr("Variance", fSmall, bGray, col3, y + 10);

                DrawStr(Fmt(expected), fMedAmt, bBlack, col1, y + 28);
                DrawStr(Fmt(grand), fMedAmt, bBlack, col2, y + 28);

                string varLabel = variance > 0
                    ? $"+ {Fmt(variance)}"
                    : variance == 0 ? "BALANCED" : $"- {Fmt(Math.Abs(variance))}";
                Brush varBrush = variance >= 0 ? bGreen : bRed;
                DrawStr(varLabel, fMedAmt, varBrush, col3, y + 28);

                y += 92;
                Spacer(ref y, 20);

                // ══════════════════════════════════════════════════════════════
                //  [7] SIGNATURE BLOCK
                // ══════════════════════════════════════════════════════════════
                HRule(ref y, Color.FromArgb(200, 200, 200));

                int sigY = y;
                int sigW = (pageW - 40) / 2;

                // Left sig — Declared by
                DrawStr("Declared by:", fSmall, bGray, left, sigY);
                g.DrawLine(new Pen(Color.Black, 1f),
                           left, sigY + 44, left + sigW, sigY + 44);
                DrawStr("Name / Signature / Date", fSmall, bLtGray, left, sigY + 48);

                // Right sig — Verified by
                int sigRight = left + sigW + 40;
                DrawStr("Verified / Approved by:", fSmall, bGray, sigRight, sigY);
                g.DrawLine(new Pen(Color.Black, 1f),
                           sigRight, sigY + 44, right, sigY + 44);
                DrawStr("Name / Signature / Date", fSmall, bLtGray, sigRight, sigY + 48);

                y = sigY + 70;
                Spacer(ref y, 14);
                HRule(ref y, Color.FromArgb(200, 200, 200));

                // ══════════════════════════════════════════════════════════════
                //  [8] FOOTER
                // ══════════════════════════════════════════════════════════════
                DrawStr("** This is an authorised tender declaration document. Please retain for records. **",
                        fSmall, bLtGray, left, y, pageW, StringAlignment.Center);

                pe.HasMorePages = false;
            };

            // ── Print dialog ──────────────────────────────────────────────────────
            using var dlg = new PrintDialog
            {
                Document = doc,
                UseEXDialog = true
            };

            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                try { doc.Print(); }
                catch (Exception ex)
                {
                    MessageBox.Show("Print error: " + ex.Message,
                        "Print Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  TAB SWITCHING
        // ══════════════════════════════════════════════════════════════════
        private void ShowTab(int idx)
        {
            _tabCash.Visible = idx == 0;
            _tabCard.Visible = idx == 1;
            _tabBank.Visible = idx == 2;

            _tabCash.BringToFront();
            _tabCard.BringToFront();
            _tabBank.BringToFront();
            Panel[] panels = { _tabCash ?? new Panel(), _tabCard ?? new Panel(), _tabBank ?? new Panel() };
            Button[] btns = { _btnTabCash ?? new Button(), _btnTabCard ?? new Button(), _btnTabBank ?? new Button() };
            for (int i = 0; i < panels.Length; i++) panels[i].Visible = (i == idx);
            for (int i = 0; i < btns.Length; i++) SetActiveTab(btns[i], i == idx);
        }

        private void SetActiveTab(Button b, bool active = true)
        {
            b.ForeColor = active ? TextWhite : TextMuted;
            b.BackColor = active ? Color.FromArgb(44, 50, 64) : Color.Transparent;
        }

        // ══════════════════════════════════════════════════════════════════
        //  LOAD EXISTING
        // ══════════════════════════════════════════════════════════════════
        private void LoadExistingDeclaration()
        {
            int shiftId = ShiftState.ShiftId > 0 ? ShiftState.ShiftId : 1;
            var existing = ShiftState.GetExistingTenderDeclaration(shiftId);
            if (!existing.Exists) return;

            for (int i = 0; i < Denoms.Length; i++)
                if (_denomQty[i] != null)
                    _denomQty[i].Text = existing.Quantities[i].ToString();

            if (_txtCardAmt != null) _txtCardAmt.Text = existing.CardAmount.ToString("0.00");
            if (_txtBankAmt != null) _txtBankAmt.Text = existing.BankAmount.ToString("0.00");

            RecalcCash();
            UpdateSummary();
        }

        // ══════════════════════════════════════════════════════════════════
        //  UI HELPERS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>Wide sidebar row — icon+label left, value right, full SB_INNER width.</summary>
        private Label SideRowWide(Panel parent, string text, ref int y)
        {
            int halfW = SB_INNER / 2;

            parent.Controls.Add(new Label
            {
                Text = text,
                Font = new Font("Segoe UI Emoji", 9.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(halfW, 28),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var val = new Label
            {
                Text = Fmt(0),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(halfW, 28),
                Location = new Point(halfW, y),
                TextAlign = ContentAlignment.MiddleRight
            };
            parent.Controls.Add(val);
            y += 32;
            return val;
        }

        private TextBox MakeDigitalInput(Panel parent, string caption, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = caption,
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(560, 20),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var tb = new TextBox
            {
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = InputBg,
                BorderStyle = BorderStyle.None,
                Size = new Size(500, 36),
                Location = new Point(0, y + 24)
            };
            parent.Controls.Add(tb);

            parent.Controls.Add(new Panel
            {
                BackColor = AccBlue,
                Size = new Size(500, 2),
                Location = new Point(0, y + 62)
            });

            return tb;
        }

        private void SetSumLabel(Label lbl, decimal val)
        {
            if (lbl == null) return;
            lbl.Text = Fmt(val);
            lbl.ForeColor = val > 0 ? TextWhite : TextMuted;
        }

        private Button MakeTabBtn(string text, int x)
        {
            var b = new Button
            {
                Text = text,
                Font = new Font("Segoe UI Emoji", 10F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(200, 46),
                Location = new Point(x, 0),
                Cursor = Cursors.Hand
            };
            b.FlatAppearance.BorderSize = 0;
            return b;
        }

        private Label MakeColHdr(string text, int x, int y, int w, bool right) =>
            new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(w, 22),
                Location = new Point(x, y),
                TextAlign = right ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft
            };

        private Label SectionLbl(string text, Color color, int y) =>
            new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = color,
                BackColor = Color.Transparent,
                AutoSize = true,
                Location = new Point(ColLabel, y)
            };

        private Panel HLine(int y) =>
            new Panel { BackColor = Border, Size = new Size(InnerW, 1), Location = new Point(0, y) };

        private string Fmt(decimal v) => $"{_sym} {v:N2}";

        private static void NumericIntOnly(object? sender, KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar))
                e.Handled = true;
        }

        private static void NumericDecOnly(object? sender, KeyPressEventArgs e)
        {
            if (sender is not TextBox tb)
                return;

            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar) && e.KeyChar != '.')
                e.Handled = true;
            if (e.KeyChar == '.' && tb.Text.Contains('.'))
                e.Handled = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BgDark);
        }
    }
}