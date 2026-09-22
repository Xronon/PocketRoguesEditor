using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PocketRoguesEditor
{
    /// <summary>
    /// Значок программы — золотая монета. Рисуется кодом, а не лежит файлом:
    /// сборка остаётся одним переносимым exe без встроенных ресурсов.
    /// </summary>
    internal static class AppIcon
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        private static Icon _icon;

        public static Icon Get()
        {
            if (_icon != null) return _icon;

            try { _icon = Build(64); }
            catch { _icon = SystemIcons.Application; }

            return _icon;
        }

        private static Icon Build(int size)
        {
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    float pad = size * 0.06f;
                    float d = size - pad * 2;

                    // тёмный обод, золотое тело, светлый внутренний круг
                    using (SolidBrush rim = new SolidBrush(Color.FromArgb(255, 150, 96, 20)))
                        g.FillEllipse(rim, pad, pad, d, d);

                    float body = d * 0.86f;
                    float bodyOff = pad + (d - body) / 2f;
                    RectangleF bodyRect = new RectangleF(bodyOff, bodyOff, body, body);
                    using (LinearGradientBrush gold = new LinearGradientBrush(bodyRect,
                               Color.FromArgb(255, 255, 214, 92), Color.FromArgb(255, 222, 150, 30), 45f))
                        g.FillEllipse(gold, bodyRect);

                    float inner = d * 0.58f;
                    float innerOff = pad + (d - inner) / 2f;
                    using (Pen ring = new Pen(Color.FromArgb(200, 170, 110, 20), size * 0.045f))
                        g.DrawEllipse(ring, innerOff, innerOff, inner, inner);

                    // блик — чтобы в 16 пикселях читалось как монета, а не как жёлтый круг
                    float s = size * 0.14f;
                    float cx = pad + d * 0.30f, cy = pad + d * 0.30f;
                    using (SolidBrush shine = new SolidBrush(Color.FromArgb(230, 255, 250, 225)))
                    {
                        PointF[] star = new PointF[]
                        {
                            new PointF(cx, cy - s), new PointF(cx + s * 0.25f, cy - s * 0.25f),
                            new PointF(cx + s, cy), new PointF(cx + s * 0.25f, cy + s * 0.25f),
                            new PointF(cx, cy + s), new PointF(cx - s * 0.25f, cy + s * 0.25f),
                            new PointF(cx - s, cy), new PointF(cx - s * 0.25f, cy - s * 0.25f),
                        };
                        g.FillPolygon(shine, star);
                    }
                }

                IntPtr handle = bmp.GetHicon();
                try
                {
                    // копия нужна, чтобы можно было освободить системный обработчик
                    using (Icon temp = Icon.FromHandle(handle))
                    {
                        return new Icon(temp, temp.Size);
                    }
                }
                finally
                {
                    try { DestroyIcon(handle); }
                    catch { }
                }
            }
        }
    }
}
