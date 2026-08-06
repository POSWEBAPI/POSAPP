// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  PrinterSettingsForm.cs — Dashboard Printer Settings for ShriPOS        ║
// ║  Drop this file into POSAPP project alongside PrintReceiptDialog.cs     ║
// ║  Saves printer preference to AppSettings so it persists across sessions ║
// ╚══════════════════════════════════════════════════════════════════════════╝

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace POSAPP.Printer
{

    // ══════════════════════════════════════════════════════════════════════════
    //  PrinterPreference  —  singleton settings bag (in-memory + persisted)
    // ══════════════════════════════════════════════════════════════════════════
    public static class PrinterPreference
    {
        // ── File that stores preferences next to the EXE ───────────────────────
        private static readonly string _filePath =
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "printer_settings.cfg");

        // ── Defaults ───────────────────────────────────────────────────────────
        public static PrinterType SelectedType { get; set; } = PrinterType.Thermal;
        public static string ThermalPrinterName { get; set; } = "";
        public static string ThermalNetworkIp { get; set; } = "";
        public static int ThermalNetworkPort { get; set; } = 9100;
        public static string A4PrinterName { get; set; } = "";

        public enum PrinterType { Thermal, A4 }


        // ── Persist ────────────────────────────────────────────────────────────
        public static void Save()
        {
            try
            {
                var lines = new[]
                {
                    $"SelectedType={(int)SelectedType}",
                    $"ThermalPrinterName={ThermalPrinterName}",
                    $"ThermalNetworkIp={ThermalNetworkIp}",
                    $"ThermalNetworkPort={ThermalNetworkPort}",
                    $"A4PrinterName={A4PrinterName}"
                };
                File.WriteAllLines(_filePath, lines);
            }
            catch { /* non-critical */ }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                foreach (string raw in File.ReadAllLines(_filePath))
                {
                    int eq = raw.IndexOf('=');
                    if (eq < 0) continue;
                    string key = raw[..eq].Trim();
                    string val = raw[(eq + 1)..].Trim();
                    switch (key)
                    {
                        case "SelectedType":
                            if (int.TryParse(val, out int t))
                                SelectedType = (PrinterType)t;
                            break;
                        case "ThermalPrinterName": ThermalPrinterName = val; break;
                        case "ThermalNetworkIp": ThermalNetworkIp = val; break;
                        case "ThermalNetworkPort":
                            if (int.TryParse(val, out int p)) ThermalNetworkPort = p;
                            break;
                        case "A4PrinterName": A4PrinterName = val; break;
                    }
                }
            }
            catch { /* non-critical */ }
        }
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  StockSettings  —  persisted stock-related preferences
    // ══════════════════════════════════════════════════════════════════════════
    public static class StockSettings
    {
        private static readonly string _filePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stock_settings.cfg");

        public static bool AllowOutOfStockSale { get; set; } = false;

        // Auto-load the first time anything touches this class
        static StockSettings()
        {
            Load();
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(_filePath, new[]
                {
                $"AllowOutOfStockSale={AllowOutOfStockSale}"
            });
            }
            catch { /* non-critical */ }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                foreach (string raw in File.ReadAllLines(_filePath))
                {
                    int eq = raw.IndexOf('=');
                    if (eq < 0) continue;
                    string key = raw[..eq].Trim();
                    string val = raw[(eq + 1)..].Trim();
                    if (key == "AllowOutOfStockSale" && bool.TryParse(val, out bool b))
                        AllowOutOfStockSale = b;
                }
            }
            catch { /* non-critical */ }
        }
    }
    // ══════════════════════════════════════════════════════════════════════════
    //  PrinterSettingsForm
    // ══════════════════════════════════════════════════════════════════════════
    public class PrinterSettingsForm : Form
    {
        // ── Palette (matches ShriPOS dark theme) ──────────────────────────────
        private static readonly Color BgDark = Color.FromArgb(22, 24, 30);
        private static readonly Color PanelDark = Color.FromArgb(32, 35, 44);
        private static readonly Color Panel2 = Color.FromArgb(42, 46, 56);
        private static readonly Color AccGreen = Color.FromArgb(34, 197, 94);
        private static readonly Color AccBlue = Color.FromArgb(59, 130, 246);
        private static readonly Color AccOrange = Color.FromArgb(251, 146, 60);
        private static readonly Color AccRed = Color.FromArgb(239, 68, 68);
        private static readonly Color TextWhite = Color.FromArgb(240, 242, 246);
        private static readonly Color TextMuted = Color.FromArgb(130, 140, 158);
        private static readonly Color TextGreen = Color.FromArgb(52, 211, 153);
        private static readonly Color Border = Color.FromArgb(50, 54, 66);
        private static readonly Color InputBg = Color.FromArgb(28, 32, 42);
        private static readonly Color CardSel = Color.FromArgb(28, 50, 38);
        private static readonly Color CardNorm = Color.FromArgb(32, 35, 44);
      
        private CheckBox _chkAllowOutOfStock;   // ← ADD THIS

        // ── Controls ──────────────────────────────────────────────────────────
        private Panel _cardThermal, _cardA4;
        private Label _lblStatus;
        private ComboBox _cmbThermal, _cmbA4;
        private TextBox _txtIp;
        private Panel _pThermalOpts, _pA4Opts;

        public PrinterSettingsForm()
        {
            PrinterPreference.Load();
            InitUI();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Build UI
        // ══════════════════════════════════════════════════════════════════════
        private void InitUI()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = BgDark;
            ClientSize = new Size(600, 580);
            ShowInTaskbar = false;
            KeyPreview = true;
            Text = "Printer Settings";
            Region = MakeRound(Size, 14);

            // Dragging
            bool drag = false; Point dragStart = Point.Empty;

            // ── Header ────────────────────────────────────────────────────────
            var pHead = new Panel
            {
                BackColor = Panel2,
                Size = new Size(600, 54),
                Location = Point.Empty
            };
            pHead.MouseDown += (s, e) => { drag = true; dragStart = e.Location; };
            pHead.MouseMove += (s, e) => { if (drag) { var p = Location; p.Offset(e.X - dragStart.X, e.Y - dragStart.Y); Location = p; } };
            pHead.MouseUp += (s, e) => drag = false;

            pHead.Controls.Add(new Label
            {
                Text = "🖨️  Printer Settings",
                Font = new Font("Segoe UI", 12F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(500, 54),
                Location = new Point(18, 0),
                TextAlign = ContentAlignment.MiddleLeft
            });

            var btnX = MakeBtn("✕", TextMuted, Color.Transparent, new Point(556, 0), new Size(44, 54));
            btnX.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            btnX.Click += (s, e) => Close();
            pHead.Controls.Add(btnX);

            // ── Sub-heading ───────────────────────────────────────────────────
            var lblSub = new Label
            {
                Text = "Choose your default print method. This setting applies automatically after every sale.",
                Font = new Font("Segoe UI", 9F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(564, 32),
                Location = new Point(18, 62),
                TextAlign = ContentAlignment.MiddleLeft
            };

            // ── Printer-type selector cards ───────────────────────────────────
            var lblChoose = SectionLabel("SELECT PRINTER TYPE", new Point(18, 100));

            _cardThermal = BuildTypeCard(
                "🖨️", "Thermal Printer",
                "80 mm roll · ESC/POS · USB or Network",
                new Point(18, 124));

            _cardA4 = BuildTypeCard(
    "📄", "A4 / Standard Printer",
    "Full-page invoice · GDI · any Windows printer",
    new Point(306, 124));

            // ── Stock setting — sits right under the printer-type cards ────────
            var pnlStockSetting = new Panel
            {
                BackColor = CardNorm,
                Size = new Size(564, 40),
                Location = new Point(18, 230)
            };
            pnlStockSetting.Region = MakeRound(pnlStockSetting.Size, 8);
            pnlStockSetting.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Border, 1f);
                using var path = RoundedPath(new Rectangle(1, 1, pnlStockSetting.Width - 2, pnlStockSetting.Height - 2), 8);
                pe.Graphics.DrawPath(pen, path);
            };

            _chkAllowOutOfStock = new CheckBox
            {
                Text = "🚫  Allow selling items even when out of stock",
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(540, 40),
                Location = new Point(14, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Checked = StockSettings.AllowOutOfStockSale
            };
            pnlStockSetting.Controls.Add(_chkAllowOutOfStock);

            // ── Thermal options ───────────────────────────────────────────────
            _pThermalOpts = new Panel
            {
                BackColor = Color.Transparent,
                Size = new Size(564, 200),
                Location = new Point(18, 282)
            };
            int oy = 0;

            _pThermalOpts.Controls.Add(SectionLabel("THERMAL PRINTER  (USB / LPT)", Point.Empty)); oy += 22;

            _cmbThermal = new ComboBox
            {
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextWhite,
                BackColor = InputBg,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(564, 28),
                Location = new Point(0, oy),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            PopulateThermalPrinters(_cmbThermal);
            _pThermalOpts.Controls.Add(_cmbThermal);
            oy += 36;

            _pThermalOpts.Controls.Add(SectionLabel("— OR  Network / TCP  (port 9100) —", new Point(0, oy))); oy += 22;

            _txtIp = new TextBox
            {
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextMuted,
                BackColor = InputBg,
                BorderStyle = BorderStyle.FixedSingle,
                Size = new Size(564, 28),
                Location = new Point(0, oy),
                Text = string.IsNullOrWhiteSpace(PrinterPreference.ThermalNetworkIp)
                              ? "e.g. 192.168.1.100" : PrinterPreference.ThermalNetworkIp
            };
            if (!_txtIp.Text.StartsWith("e.g")) _txtIp.ForeColor = TextWhite;
            _txtIp.Enter += (s, e) => { if (_txtIp.Text.StartsWith("e.g")) { _txtIp.Text = ""; _txtIp.ForeColor = TextWhite; } };
            _txtIp.Leave += (s, e) => { if (string.IsNullOrWhiteSpace(_txtIp.Text)) { _txtIp.Text = "e.g. 192.168.1.100"; _txtIp.ForeColor = TextMuted; } };
            _pThermalOpts.Controls.Add(_txtIp);

            // ── A4 options ────────────────────────────────────────────────────
            _pA4Opts = new Panel
            {
                BackColor = Color.Transparent,
                Size = new Size(564, 80),
                Location = new Point(18, 282),
                Visible = false
            };
            int ay = 0;

            _pA4Opts.Controls.Add(SectionLabel("A4 / STANDARD PRINTER", Point.Empty)); ay += 22;

            // ── Stock settings section ──────────────────────────────────────────
      

            _cmbA4 = new ComboBox
            {
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = TextWhite,
                BackColor = InputBg,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(564, 28),
                Location = new Point(0, ay),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            // For Thermal
            PopulateThermalPrinters(_cmbThermal);

            // For A4
            PopulateA4Printers(_cmbA4);
            if (_cmbA4.Items.Count > 0)
            {
                int idx = _cmbA4.Items.IndexOf(PrinterPreference.A4PrinterName);
                _cmbA4.SelectedIndex = idx >= 0 ? idx : 0;
            }
            _pA4Opts.Controls.Add(_cmbA4);

            // ── Status label ──────────────────────────────────────────────────
            _lblStatus = new Label
            {
                Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(564, 24),
                Location = new Point(18, 456),
                TextAlign = ContentAlignment.MiddleCenter
            };

            // ── Save button ───────────────────────────────────────────────────
            // ── Save button ───────────────────────────────────────────────────
            var btnSave = MakeBtn("💾 Save Printer Settings", AccGreen, Color.White,
                                  new Point(18, 486), new Size(280, 42));
            btnSave.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnSave.Click += BtnSave_Click;
            btnSave.Click += (s, e) => Close();   // Auto close after save

            // ── Close button ──────────────────────────────────────────────────
            var btnClose2 = MakeBtn("✖ Close", Color.FromArgb(55, 60, 78), TextMuted,
                                    new Point(310, 486), new Size(272, 42));
            btnClose2.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            btnClose2.Click += (s, e) => Close();

            // ── Wire card clicks ──────────────────────────────────────────────
            _cardThermal.Click += (s, e) => SelectType(PrinterPreference.PrinterType.Thermal);
            foreach (Control c in _cardThermal.Controls) c.Click += (s, e) => SelectType(PrinterPreference.PrinterType.Thermal);

            _cardA4.Click += (s, e) => SelectType(PrinterPreference.PrinterType.A4);
            foreach (Control c in _cardA4.Controls) c.Click += (s, e) => SelectType(PrinterPreference.PrinterType.A4);

            // ── Assemble ──────────────────────────────────────────────────────
            Controls.AddRange(new Control[]
            {
                pHead, lblSub, lblChoose,
                _cardThermal, _cardA4,
                _pThermalOpts, _pA4Opts,pnlStockSetting,
                _lblStatus, btnSave, btnClose2
            });
            pHead.BringToFront();

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            // Restore saved state
            SelectType(PrinterPreference.SelectedType);

            // Restore saved thermal printer selection
            if (!string.IsNullOrWhiteSpace(PrinterPreference.ThermalPrinterName))
            {
                for (int i = 0; i < _cmbThermal.Items.Count; i++)
                {
                    string item = _cmbThermal.Items[i].ToString()
                                  .Replace("🔥 ", "").Trim();
                    if (item.Equals(PrinterPreference.ThermalPrinterName,
                                    StringComparison.OrdinalIgnoreCase))
                    { _cmbThermal.SelectedIndex = i; break; }
                }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Select printer type — update card highlight + visible options panel
        // ══════════════════════════════════════════════════════════════════════
        private void SelectType(PrinterPreference.PrinterType type)
        {
            PrinterPreference.SelectedType = type;

            // Redraw cards
            ApplyCardStyle(_cardThermal, type == PrinterPreference.PrinterType.Thermal);
            ApplyCardStyle(_cardA4, type == PrinterPreference.PrinterType.A4);

            _pThermalOpts.Visible = type == PrinterPreference.PrinterType.Thermal;
            _pA4Opts.Visible = type == PrinterPreference.PrinterType.A4;
        }

        private void ApplyCardStyle(Panel card, bool selected)
        {
            card.BackColor = selected ? CardSel : CardNorm;
            card.Invalidate();
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Save
        // ══════════════════════════════════════════════════════════════════════
        private void BtnSave_Click(object sender, EventArgs e)
        {
            // Thermal printer name (strip emoji prefix)
            string thermalName = _cmbThermal.SelectedItem?.ToString() ?? "";
            if (thermalName.StartsWith("🔥 ")) thermalName = thermalName[3..].Trim();
            if (thermalName == "(Select Printer)") thermalName = "";
            PrinterPreference.ThermalPrinterName = thermalName;

            // Network IP
            string ip = _txtIp.Text.Trim();
            PrinterPreference.ThermalNetworkIp =
                ip.StartsWith("e.g") || string.IsNullOrWhiteSpace(ip) ? "" : ip;

            // A4 printer name
            PrinterPreference.A4PrinterName = _cmbA4.SelectedItem?.ToString() ?? "";
             
            PrinterPreference.Save();
            // ── Stock settings ──────────────────────────────────────────────
            StockSettings.AllowOutOfStockSale = _chkAllowOutOfStock.Checked;
            StockSettings.Save();

            SetStatus("✓  Settings saved successfully!", true);
        }

        private void SetStatus(string msg, bool ok)
        {
            _lblStatus.Text = msg;
            _lblStatus.ForeColor = ok ? TextGreen : AccRed;
        }

        // ══════════════════════════════════════════════════════════════════════
        //  Helpers
        // ══════════════════════════════════════════════════════════════════════
        private Panel BuildTypeCard(string icon, string title, string sub, Point loc)
        {
            var card = new Panel
            {
                BackColor = CardNorm,
                Size = new Size(270, 100),
                Location = loc,
                Cursor = Cursors.Hand
            };
            card.Region = MakeRound(card.Size, 10);
            card.Paint += (s, pe) =>
            {
                pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                bool sel = card == _cardThermal &&
                            PrinterPreference.SelectedType == PrinterPreference.PrinterType.Thermal
                        || card == _cardA4 &&
                            PrinterPreference.SelectedType == PrinterPreference.PrinterType.A4;
                Color borderC = sel ? AccGreen : Border;
                float thick = sel ? 2f : 1f;
                using var pen = new Pen(borderC, thick);
                using var path = RoundedPath(new Rectangle(1, 1, card.Width - 2, card.Height - 2), 10);
                pe.Graphics.DrawPath(pen, path);
            };

            card.Controls.Add(new Label
            {
                Text = icon,
                Font = new Font("Segoe UI Emoji", 22F),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(60, 100),
                Location = new Point(10, 0),
                TextAlign = ContentAlignment.MiddleCenter
            });
            card.Controls.Add(new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = TextWhite,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(192, 28),
                Location = new Point(70, 28),
                TextAlign = ContentAlignment.BottomLeft
            });
            card.Controls.Add(new Label
            {
                Text = sub,
                Font = new Font("Segoe UI", 7.8F),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(192, 30),
                Location = new Point(70, 56),
                TextAlign = ContentAlignment.TopLeft
            });

            return card;
        }

        private static Label SectionLabel(string text, Point loc)
        {
            return new Label
            {
                Text = text,
                Font = new Font("Segoe UI", 7.5F, FontStyle.Bold),
                ForeColor = TextMuted,
                BackColor = Color.Transparent,
                AutoSize = false,
                Size = new Size(564, 20),
                Location = loc,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        // ══════════════════════════════════════════════════════════════════════
        // Populate Thermal Printers (Only Thermal)
        // ══════════════════════════════════════════════════════════════════════
        // Populate Thermal Printers - ONLY Thermal Printers
        // ══════════════════════════════════════════════════════════════════════
        private static void PopulateThermalPrinters(ComboBox cmb)
        {
            cmb.Items.Clear();
            cmb.Items.Add("(Select Thermal Printer)");

            try
            {
                int count = 0;
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    if (IsThermalPrinter(printer))
                    {
                        cmb.Items.Add("🔥 " + printer);
                        count++;
                    }
                }

                if (count == 0)
                {
                    cmb.Items.Add("No Thermal Printer Found");
                }
                else
                {
                    // Restore previously saved selection
                    if (!string.IsNullOrWhiteSpace(PrinterPreference.ThermalPrinterName))
                    {
                        for (int i = 0; i < cmb.Items.Count; i++)
                        {
                            string item = cmb.Items[i].ToString().Replace("🔥 ", "").Trim();
                            if (item.Equals(PrinterPreference.ThermalPrinterName, StringComparison.OrdinalIgnoreCase))
                            {
                                cmb.SelectedIndex = i;
                                break;
                            }
                        }
                    }
                    else
                    {
                        cmb.SelectedIndex = 1; // Select first thermal printer
                    }
                }
            }
            catch
            {
                cmb.Items.Add("Error loading printers");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Populate A4 Printers - ONLY Non-Thermal (Standard) Printers
        // ══════════════════════════════════════════════════════════════════════
        private void PopulateA4Printers(ComboBox cmb)
        {
            cmb.Items.Clear();
            cmb.Items.Add("(Select A4 Printer)");

            try
            {
                int count = 0;
                foreach (string printer in PrinterSettings.InstalledPrinters)
                {
                    if (!IsThermalPrinter(printer))
                    {
                        cmb.Items.Add(printer);
                        count++;
                    }
                }

                if (count == 0)
                {
                    cmb.Items.Add("No Standard A4 Printer Found");
                }
                else
                {
                    // Restore saved A4 printer
                    if (!string.IsNullOrWhiteSpace(PrinterPreference.A4PrinterName))
                    {
                        int idx = cmb.Items.IndexOf(PrinterPreference.A4PrinterName);
                        if (idx >= 0) cmb.SelectedIndex = idx;
                        else cmb.SelectedIndex = 1;
                    }
                    else
                    {
                        cmb.SelectedIndex = 1;
                    }
                }
            }
            catch
            {
                cmb.Items.Add("Error loading printers");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // Improved Thermal Detection Logic
        // ══════════════════════════════════════════════════════════════════════
        private static bool IsThermalPrinter(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName)) return false;

            string name = printerName.ToLowerInvariant().Trim();

            // Strong thermal indicators
            bool isThermal = name.Contains("thermal") ||
                             name.Contains("receipt") ||
                             name.Contains("pos printer") ||
                             name.Contains("80mm") ||
                             name.Contains("58mm") ||
                             name.Contains("roll paper") ||
                             name.Contains("tm-t") ||
                             name.Contains("tm-u") ||
                             name.Contains("xp-") ||
                             name.Contains("epson") && (name.Contains("tm") || name.Contains("receipt"));

            // Star Micronics
            if (name.Contains("star") && (name.Contains("thermal") || name.Contains("receipt")))
                isThermal = true;

            // Bixolon
            if (name.Contains("bixolon"))
                isThermal = true;

            // Avoid classifying common A4 printers as thermal
            if (name.Contains("microsoft print") ||
                name.Contains("pdf") ||
                name.Contains("xps") ||
                name.Contains("fax") ||
                name.Contains("one note"))
                isThermal = false;

            return isThermal;
        }

        private static Button MakeBtn(string text, Color bg, Color fg, Point loc, Size sz)
        {
            var b = new Button
            {
                Text = text,
                BackColor = bg,
                ForeColor = fg,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F),
                Location = loc,
                Size = sz,
                Cursor = Cursors.Hand,
                FlatAppearance = { BorderSize = 0 }
            };
            b.Region = MakeRound(sz, 8);
            return b;
        }

        private static Region MakeRound(Size size, int r)
        {
            var path = new GraphicsPath();
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(size.Width - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(size.Width - r * 2, size.Height - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, size.Height - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            return new Region(path);
        }

        private static GraphicsPath RoundedPath(Rectangle rect, int r)
        {
            int d = r * 2;
            var path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}