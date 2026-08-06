using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace POSAPP
{
    internal static class UiLayoutService
    {
        private static readonly HashSet<Form> StyledForms = new();
        private static readonly HashSet<Control> WiredControls = new();
        private static readonly Font AppFont = new("Segoe UI", 9F);
        private static readonly Font AppFontBold = new("Segoe UI", 9F, FontStyle.Bold);

        public static void Install()
        {
            Application.Idle += (_, _) => ApplyToOpenForms();
        }

        private static void ApplyToOpenForms()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form == null || form.IsDisposed) continue;

                if (StyledForms.Add(form))
                {
                    form.Font = AppFont;
                    form.AutoScaleMode = AutoScaleMode.Dpi;
                    if (form.MinimumSize.Width == 0 || form.MinimumSize.Height == 0)
                    {
                        var minW = Math.Min(Math.Max(760, form.ClientSize.Width / 2), 1024);
                        var minH = Math.Min(Math.Max(520, form.ClientSize.Height / 2), 700);
                        form.MinimumSize = new Size(minW, minH);
                    }

                    form.Shown += (_, _) => ApplyRecursive(form);
                    form.Resize += (_, _) => ApplyResponsiveLayout(form);
                }

                ApplyRecursive(form);
                ApplyResponsiveLayout(form);
            }
        }

        private static void ApplyRecursive(Control root)
        {
            if (root == null || root.IsDisposed) return;

            StyleControl(root);
            WireControl(root);

            foreach (Control child in root.Controls)
                ApplyRecursive(child);
        }

        private static void WireControl(Control control)
        {
            if (!WiredControls.Add(control)) return;

            control.ControlAdded += (_, e) =>
            {
                if (e.Control == null) return;
                ApplyRecursive(e.Control);
                Form? form = e.Control.FindForm();
                if (form != null) ApplyResponsiveLayout(form);
            };

            control.Disposed += (_, _) => WiredControls.Remove(control);
        }

        private static void StyleControl(Control c)
        {
            switch (c)
            {
                case TextBox tb:
                    StyleTextBox(tb);
                    break;
                case ComboBox cb:
                    StyleComboBox(cb);
                    break;
                case NumericUpDown nud:
                    StyleNumericUpDown(nud);
                    break;
                case DataGridView grid:
                    StyleGrid(grid);
                    break;
                case Button btn:
                    StyleButton(btn);
                    break;
                case Label label:
                    StyleLabel(label);
                    break;
                case ListBox list:
                    list.IntegralHeight = false;
                    if (list.ItemHeight < 22) list.ItemHeight = 24;
                    break;
                case TableLayoutPanel table:
                    table.Padding = MaxPadding(table.Padding, new Padding(8));
                    break;
                case FlowLayoutPanel flow:
                    flow.WrapContents = true;
                    flow.AutoScroll = true;
                    flow.Padding = MaxPadding(flow.Padding, new Padding(8));
                    break;
                case Panel:
                    break;
            }
        }

        private static void StyleTextBox(TextBox tb)
        {
            if (tb.Font.FontFamily.Name != "Segoe UI" && tb.Font.FontFamily.Name != "Consolas")
                tb.Font = AppFont;

            tb.Margin = MaxPadding(tb.Margin, new Padding(4, 4, 4, 6));
            if (!tb.Multiline && tb.Height < 30) tb.Height = 30;
            if (tb.Width < 90) tb.Width = 90;
            tb.MinimumSize = MaxSize(tb.MinimumSize, new Size(80, tb.Multiline ? 60 : 30));
        }

        private static void StyleComboBox(ComboBox cb)
        {
            if (cb.Font.FontFamily.Name != "Segoe UI") cb.Font = AppFont;
            cb.Margin = MaxPadding(cb.Margin, new Padding(4, 4, 4, 6));
            if (cb.Height < 32) cb.Height = 32;
            if (cb.Width < 120) cb.Width = 120;
            cb.MinimumSize = MaxSize(cb.MinimumSize, new Size(110, 32));
            cb.IntegralHeight = false;
            cb.DropDownWidth = Math.Max(cb.DropDownWidth, cb.Width);
        }

        private static void StyleNumericUpDown(NumericUpDown nud)
        {
            if (nud.Font.FontFamily.Name != "Segoe UI") nud.Font = AppFont;
            nud.Margin = MaxPadding(nud.Margin, new Padding(4, 4, 4, 6));
            if (nud.Height < 30) nud.Height = 30;
            if (nud.Width < 90) nud.Width = 90;
            nud.MinimumSize = MaxSize(nud.MinimumSize, new Size(80, 30));
        }

        private static void StyleButton(Button btn)
        {
            bool titleButton = btn.Parent is Panel p && p.Height <= 60 && btn.Width <= 60;
            if (!titleButton)
            {
                if (btn.Font.FontFamily.Name != "Segoe UI") btn.Font = AppFontBold;
                if (btn.Height < 34) btn.Height = 34;
                if (btn.Width < 72) btn.Width = 72;
                btn.MinimumSize = MaxSize(btn.MinimumSize, new Size(64, 32));
            }

            btn.TextAlign = ContentAlignment.MiddleCenter;
            btn.UseCompatibleTextRendering = false;
            btn.Margin = MaxPadding(btn.Margin, new Padding(4));
        }

        private static void StyleLabel(Label label)
        {
            if (!label.AutoSize)
                label.AutoEllipsis = true;

            if (label.Height < 20 && !label.AutoSize)
                label.Height = 20;
        }

        private static void StyleGrid(DataGridView grid)
        {
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.ScrollBars = ScrollBars.Both;
            grid.RowTemplate.Height = Math.Max(grid.RowTemplate.Height, 32);
            grid.ColumnHeadersHeight = Math.Max(grid.ColumnHeadersHeight, 36);
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
            grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
            grid.EnableHeadersVisualStyles = false;

            foreach (DataGridViewColumn col in grid.Columns)
            {
                string key = $"{col.Name} {col.HeaderText}".ToLowerInvariant();
                int minWidth = key switch
                {
                    var s when s.Contains("amount") || s.Contains("total") || s.Contains("price") || s.Contains("grand") => 115,
                    var s when s.Contains("qty") || s.Contains("quantity") || s.Contains("on hand") => 90,
                    var s when s.Contains("invoice") || s.Contains("receipt") => 115,
                    var s when s.Contains("date") || s.Contains("time") => 110,
                    var s when s.Contains("customer") || s.Contains("product") || s.Contains("item") || s.Contains("name") => 160,
                    var s when s.Contains("status") || s.Contains("method") => 95,
                    _ => 80
                };

                col.MinimumWidth = Math.Max(col.MinimumWidth, minWidth);
                col.FillWeight = Math.Max(col.FillWeight, minWidth);

                if (key.Contains("amount") || key.Contains("total") || key.Contains("price") ||
                    key.Contains("qty") || key.Contains("quantity") || key.Contains("on hand"))
                {
                    col.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    col.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
                }
            }
        }

        private static void ApplyResponsiveLayout(Form form)
        {
            foreach (Control c in GetAllControls(form))
            {
                if (c is TextBox or ComboBox or NumericUpDown)
                {
                    var parent = c.Parent;
                    if (parent == null) continue;

                    int maxWidth = Math.Max(80, parent.ClientSize.Width - c.Left - 12);
                    if (c.Right > parent.ClientSize.Width - 8 && c.Width > maxWidth)
                        c.Width = maxWidth;
                }
            }
        }

        private static IEnumerable<Control> GetAllControls(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in GetAllControls(child))
                    yield return nested;
            }
        }

        private static Size MaxSize(Size current, Size required) =>
            new(Math.Max(current.Width, required.Width), Math.Max(current.Height, required.Height));

        private static Padding MaxPadding(Padding current, Padding required) =>
            new(
                Math.Max(current.Left, required.Left),
                Math.Max(current.Top, required.Top),
                Math.Max(current.Right, required.Right),
                Math.Max(current.Bottom, required.Bottom));
    }
}
