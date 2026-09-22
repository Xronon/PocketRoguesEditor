using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PocketRoguesEditor
{
    /// <summary>
    /// Светлая и тёмная тема окон.
    ///
    /// Палитра общая с другими программами того же автора, чтобы они выглядели роднёй.
    ///
    /// ⚠️ Windows Forms своей тёмной темы не имеют вовсе: каждый элемент красится
    /// вручную. Что покрасить нельзя и что останется светлым:
    ///   * системные окна вопросов (MessageBox, выбор цвета) — их рисует Windows;
    ///   * квадратик самой галочки у CheckBox и кружок у RadioButton;
    ///   * ползунок прозрачности (TrackBar) — системный контрол без своих цветов.
    /// Решено пока пожить так.
    /// </summary>
    internal static class Theme
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr,
                                                        ref int value, int size);

        public static bool Dark;

        // --- цвета ------------------------------------------------------------
        public static Color Bg { get { return Dark ? Rgb(0x21252b) : SystemColors.Control; } }
        public static Color Fg { get { return Dark ? Rgb(0xd7dae0) : SystemColors.ControlText; } }
        public static Color Muted { get { return Dark ? Rgb(0x9aa0a6) : SystemColors.GrayText; } }
        public static Color Entry { get { return Dark ? Rgb(0x1b1f24) : SystemColors.Window; } }
        public static Color Btn { get { return Dark ? Rgb(0x2d333b) : SystemColors.Control; } }
        public static Color BtnHover { get { return Dark ? Rgb(0x3a414b) : SystemColors.ControlLight; } }
        public static Color Border { get { return Dark ? Rgb(0x3a3f47) : SystemColors.ControlDark; } }
        public static Color Sel { get { return Dark ? Rgb(0x3a4250) : SystemColors.Highlight; } }
        public static Color SelText { get { return Dark ? Rgb(0xf0f2f6) : SystemColors.HighlightText; } }
        public static Color Accent { get { return Dark ? Rgb(0x268bd2) : SystemColors.Highlight; } }

        private static Color Rgb(int value)
        {
            return Color.FromArgb((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
        }

        // ------------------------------------------------------------------

        /// <summary>Покрасить окно целиком, вместе с системной полосой заголовка.</summary>
        public static void ApplyToForm(Form form)
        {
            if (form == null) return;

            form.BackColor = Bg;
            form.ForeColor = Fg;
            DarkTitleBar(form);
            Apply(form);
            form.Invalidate(true);
        }

        /// <summary>
        /// Перекрасить дерево элементов. Обходит всё вглубь: у каждого типа
        /// свои свойства, общего «покрасить всё» в Windows Forms нет.
        /// </summary>
        public static void Apply(Control root)
        {
            if (root == null) return;

            foreach (Control c in root.Controls)
            {
                Paint(c);
                Apply(c);
            }
        }

        private static void Paint(Control c)
        {
            DataGridView grid = c as DataGridView;
            if (grid != null) { PaintGrid(grid); return; }

            Button button = c as Button;
            if (button != null) { PaintButton(button); return; }

            TextBoxBase text = c as TextBoxBase;
            if (text != null)
            {
                text.BackColor = Entry;
                text.ForeColor = Fg;
                text.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            ListBox list = c as ListBox;
            if (list != null)
            {
                list.BackColor = Entry;
                list.ForeColor = Fg;
                list.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            ComboBox combo = c as ComboBox;
            if (combo != null)
            {
                combo.FlatStyle = Dark ? FlatStyle.Flat : FlatStyle.Standard;
                combo.BackColor = Dark ? Entry : SystemColors.Window;
                combo.ForeColor = Fg;
                return;
            }

            NumericUpDown spin = c as NumericUpDown;
            if (spin != null)
            {
                spin.BackColor = Entry;
                spin.ForeColor = Fg;
                spin.BorderStyle = BorderStyle.FixedSingle;
                return;
            }

            GroupBox group = c as GroupBox;
            if (group != null)
            {
                group.BackColor = Bg;
                group.ForeColor = Dark ? Muted : SystemColors.ControlText;
                return;
            }

            Label label = c as Label;
            if (label != null)
            {
                // подсказки серым остаются серыми, обычные надписи — обычными
                bool wasGray = label.ForeColor == SystemColors.GrayText
                            || label.ForeColor == Rgb(0x9aa0a6);
                label.BackColor = Color.Transparent;
                label.ForeColor = wasGray ? Muted : Fg;
                return;
            }

            TrackBar bar = c as TrackBar;
            if (bar != null)
            {
                // сам ползунок системный и цветов не принимает — красим хотя бы фон
                bar.BackColor = Bg;
                return;
            }

            c.BackColor = Bg;
            c.ForeColor = Fg;
        }

        private static void PaintGrid(DataGridView grid)
        {
            grid.EnableHeadersVisualStyles = false;
            grid.BackgroundColor = Entry;
            grid.GridColor = Border;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.ForeColor = Fg;

            grid.DefaultCellStyle.BackColor = Entry;
            grid.DefaultCellStyle.ForeColor = Fg;
            grid.DefaultCellStyle.SelectionBackColor = Sel;
            grid.DefaultCellStyle.SelectionForeColor = SelText;

            grid.ColumnHeadersDefaultCellStyle.BackColor = Dark ? Rgb(0x2d333b) : SystemColors.Control;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Fg;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor =
                Dark ? Rgb(0x2d333b) : SystemColors.Control;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Fg;
        }

        private static void PaintButton(Button button)
        {
            if (Dark)
            {
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = Btn;
                button.ForeColor = Fg;
                button.FlatAppearance.BorderColor = Border;
                button.FlatAppearance.MouseOverBackColor = BtnHover;
                button.FlatAppearance.MouseDownBackColor = Sel;
            }
            else
            {
                button.FlatStyle = FlatStyle.Standard;
                button.UseVisualStyleBackColor = true;
                button.BackColor = SystemColors.Control;
                button.ForeColor = SystemColors.ControlText;
            }
        }

        /// <summary>
        /// Попросить Windows перекрасить системную полосу заголовка. Без этого
        /// выходит тёмное окно со светлой шапкой. Приём тот же, что в транскрайбере.
        /// </summary>
        public static void DarkTitleBar(Form form)
        {
            if (form == null || !form.IsHandleCreated) return;

            try
            {
                int value = Dark ? 1 : 0;
                // 20 — Windows 10 2004 и новее, 19 — то же самое в более старых
                if (DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int));
            }
            catch { }
        }

        // ------------------------------------------------------------------

        /// <summary>Значок переключателя: солнце для светлой темы, луна для тёмной.</summary>
        public static void DrawSwitch(Graphics g, Rectangle box, bool dark, bool hover)
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            Color ink = dark ? Rgb(0xd7dae0) : Rgb(0x5a6070);
            if (hover) ink = dark ? Color.White : Rgb(0x21252b);

            using (Pen pen = new Pen(ink, 1.6f))
            using (SolidBrush brush = new SolidBrush(ink))
            {
                int cx = box.Left + box.Width / 2;
                int cy = box.Top + box.Height / 2;
                int r = Math.Min(box.Width, box.Height) / 2 - 3;
                if (r < 3) return;

                if (dark)
                {
                    // луна: круг с вырезанным полумесяцем
                    using (System.Drawing.Drawing2D.GraphicsPath path =
                           new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        path.AddEllipse(cx - r, cy - r, r * 2, r * 2);
                        Region region = new Region(path);
                        using (System.Drawing.Drawing2D.GraphicsPath cut =
                               new System.Drawing.Drawing2D.GraphicsPath())
                        {
                            cut.AddEllipse(cx - r + r / 2, cy - r - r / 3, r * 2, r * 2);
                            region.Exclude(cut);
                        }
                        g.FillRegion(brush, region);
                        region.Dispose();
                    }
                }
                else
                {
                    // солнце: круг с лучами
                    g.FillEllipse(brush, cx - r / 2, cy - r / 2, r, r);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = Math.PI * i / 4.0;
                        g.DrawLine(pen,
                            cx + (float)(Math.Cos(a) * (r * 0.8)),
                            cy + (float)(Math.Sin(a) * (r * 0.8)),
                            cx + (float)(Math.Cos(a) * r * 1.25f),
                            cy + (float)(Math.Sin(a) * r * 1.25f));
                    }
                }
            }
        }
    }
}
