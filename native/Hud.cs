// Legend overlay drawn with GDI+ into a texture: ties worm colours to cores and shows the numbers.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;

namespace ScreenWorm
{
    class HudTexture
    {
        public uint Tex; public int W, H; public int Mode;
        public bool Visible { get { return Mode > 0 && Tex != 0 && builtMode == Mode; } }
        readonly double sc; readonly string host, cpuModel, os; readonly int cores; readonly Color[] colors; readonly int maxW;
        Bitmap bmp; int builtMode = -1; readonly Stopwatch clock = Stopwatch.StartNew(); double lastUpdate = -10;
        readonly Font font, bold; readonly StringFormat fmt = (StringFormat)StringFormat.GenericTypographic.Clone();

        public HudTexture(int maxWidth, double scale, string host, string cpuModel, string os, Sim sim, int mode)
        {
            maxW = Math.Max(64, maxWidth); sc = Math.Max(0.5, scale); this.host = host; this.cpuModel = cpuModel; this.os = os; cores = sim.Cores; Mode = mode;
            colors = new Color[cores];
            for (int i = 0; i < cores; i++) { var w = sim.Worms[i]; colors[i] = Color.FromArgb(255, C(w.R), C(w.G), C(w.B)); }
            font = new Font("Consolas", (float)(11.5 * sc), FontStyle.Regular, GraphicsUnit.Pixel);
            bold = new Font("Consolas", (float)(11.5 * sc), FontStyle.Bold, GraphicsUnit.Pixel);
            fmt.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
            GL.glGenTextures(1, out Tex);
        }
        static int C(float v) { return Math.Max(0, Math.Min(255, (int)Math.Round(v * 255))); }
        int StripH { get { return (int)Math.Round(38 * sc); } }
        int PanelH { get { int rows = (cores + 7) / 8; return (int)Math.Round((64 + rows * 18) * sc); } }

        void Rebuild()
        {
            builtMode = Mode;
            W = Math.Min(maxW, (int)Math.Round(1900 * sc)); H = StripH + (Mode == 2 ? PanelH + (int)Math.Round(12 * sc) : 0);
            if (bmp != null) bmp.Dispose();
            bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            GL.glBindTexture(GL.TEXTURE_2D, Tex);
            GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MIN_FILTER, (int)GL.LINEAR); GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_MAG_FILTER, (int)GL.LINEAR);
            GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_S, (int)GL.CLAMP_TO_EDGE); GL.glTexParameteri(GL.TEXTURE_2D, GL.TEXTURE_WRAP_T, (int)GL.CLAMP_TO_EDGE);
            GL.glTexImage2D(GL.TEXTURE_2D, 0, (int)GL.RGBA8, W, H, 0, GL.BGRA, GL.UNSIGNED_BYTE, IntPtr.Zero);
            lastUpdate = -10;
        }

        public void Update(Sim sim, double fps, int particles, double quality, string label, string renderer)
        {
            if (Mode == 0) return;
            if (builtMode != Mode) Rebuild();
            double now = clock.Elapsed.TotalSeconds;
            if (now - lastUpdate < 0.25) return;
            lastUpdate = now;
            Draw(sim, fps, particles, quality, label, renderer);
            var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                GL.glBindTexture(GL.TEXTURE_2D, Tex); GL.glPixelStorei(GL.UNPACK_ALIGNMENT, 4);
                GL.glTexSubImage2D(GL.TEXTURE_2D, 0, 0, 0, W, H, GL.BGRA, GL.UNSIGNED_BYTE, data.Scan0);
            }
            finally { bmp.UnlockBits(data); }
        }

        static string Rate(double b)
        {
            if (b < 1024) return b.ToString("0", CultureInfo.InvariantCulture) + " B/s";
            if (b < 1048576) return (b / 1024).ToString("0.0", CultureInfo.InvariantCulture) + " kB/s";
            if (b < 1073741824) return (b / 1048576).ToString("0.0", CultureInfo.InvariantCulture) + " MB/s";
            return (b / 1073741824).ToString("0.00", CultureInfo.InvariantCulture) + " GB/s";
        }
        static string Up(double s)
        {
            long t = (long)Math.Max(0, s); long d = t / 86400, h = t % 86400 / 3600, m = t % 3600 / 60;
            return d > 0 ? d + "d " + h + "h " + m + "m" : h > 0 ? h + "h " + m + "m" : m + "m";
        }
        static string Pct(double v) { return (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%"; }

        float Text(Graphics g, string t, Font f, Brush b, float x, float y)
        {
            g.DrawString(t, f, b, x, y, fmt);
            return x + g.MeasureString(t, f, int.MaxValue, fmt).Width;
        }

        void Draw(Sim sim, double fps, int particles, double quality, string label, string renderer)
        {
            var st = sim.Stats; var smp = sim.LastSample; float s = (float)sc;
            using (var g = Graphics.FromImage(bmp))
            using (var text = new SolidBrush(Color.FromArgb(185, 200, 216, 255)))
            using (var bright = new SolidBrush(Color.FromArgb(235, 232, 240, 255)))
            using (var accent = new SolidBrush(Color.FromArgb(235, 127, 215, 255)))
            using (var dim = new SolidBrush(Color.FromArgb(120, 200, 216, 255)))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                int y0 = H - StripH;
                using (var br = new LinearGradientBrush(new Rectangle(0, y0, W, StripH), Color.FromArgb(0, 2, 4, 12), Color.FromArgb(160, 2, 4, 12), LinearGradientMode.Vertical))
                    g.FillRectangle(br, 0, y0, W, StripH);
                float ty = y0 + (StripH - font.Height) / 2f, gap = 18 * s, x = 16 * s;
                x = Text(g, "SCREEN", bold, bright, x, ty); x = Text(g, "WORM", bold, accent, x, ty) + gap;
                x = Text(g, host, font, bright, x, ty) + gap;
                if (smp.Uptime > 0) x = Text(g, "up " + Up(smp.Uptime), font, dim, x, ty) + gap;
                float barW = 3 * s, bgap = 2 * s, barH = 14 * s, by = y0 + (StripH + barH) / 2f;
                using (var slot = new SolidBrush(Color.FromArgb(16, 255, 255, 255)))
                    for (int i = 0; i < cores; i++)
                    {
                        float bx = x + i * (barW + bgap);
                        g.FillRectangle(slot, bx, by - barH, barW, barH);
                        double u = sim.Worms[i].Util; float h = Math.Max(1f, (float)(u * barH));
                        using (var b = new SolidBrush(Color.FromArgb((int)(110 + 145 * u), colors[i]))) g.FillRectangle(b, bx, by - h, barW, h);
                    }
                x += cores * (barW + bgap) + gap;
                x = Text(g, "cpu " + Pct(st.Cpu), font, text, x, ty) + gap;
                x = Text(g, "mem " + Pct(st.Mem), font, text, x, ty) + gap;
                x = Text(g, "net ↓" + Rate(st.Rx) + " ↑" + Rate(st.Tx), font, text, x, ty) + gap;
                x = Text(g, "disk r " + Rate(st.Read) + " w " + Rate(st.Write), font, text, x, ty) + gap;
                if (sim.HasGpu) x = Text(g, "gpu " + Pct(st.Gpu), font, text, x, ty) + gap;
                if (st.Procs > 0) Text(g, ((int)st.Procs) + " procs · " + ((int)st.Threads) + " threads", font, dim, x, ty);

                if (Mode == 2)
                {
                    float px = 16 * s, py = 6 * s, pw = Math.Min(W - 32 * s, 8 * 118 * s + 24 * s), ph = PanelH;
                    using (var fill = new SolidBrush(Color.FromArgb(165, 4, 8, 20))) g.FillRectangle(fill, px, py, pw, ph);
                    using (var pen = new Pen(Color.FromArgb(50, 120, 160, 255))) g.DrawRectangle(pen, px, py, pw, ph);
                    float cy = py + 10 * s;
                    Text(g, cpuModel + " · " + os, font, bright, px + 12 * s, cy); cy += 22 * s;
                    float colW = (pw - 24 * s) / 8;
                    for (int i = 0; i < cores; i++)
                    {
                        float cx = px + 12 * s + (i % 8) * colW, ry = cy + (i / 8) * 18 * s;
                        using (var b = new SolidBrush(colors[i])) g.FillEllipse(b, cx, ry + 3 * s, 8 * s, 8 * s);
                        Text(g, "C" + i.ToString("00", CultureInfo.InvariantCulture) + " " + Pct(sim.Worms[i].Util).PadLeft(4), font, text, cx + 13 * s, ry);
                    }
                    cy += ((cores + 7) / 8) * 18 * s + 8 * s;
                    Text(g, fps.ToString("0", CultureInfo.InvariantCulture) + " fps · quality " + quality.ToString("0.00", CultureInfo.InvariantCulture) + " · " + particles.ToString("N0", CultureInfo.InvariantCulture) + " particles · " + label + " · " + renderer, font, dim, px + 12 * s, cy);
                }
            }
        }
    }
}
