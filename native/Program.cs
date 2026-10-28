// Entry point, windows and lifetime: screensaver (/s), configure (/c), preview (/p), plus
// --run (full-screen demo), --window (resizable), --screenshot (offscreen PNG), --stats, --screens, --install.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenWorm
{
    enum RunMode { Saver, Demo, Window, Preview, Screenshot }

    class Args
    {
        public readonly string Mode = "/c"; readonly Dictionary<string, string> opts = new Dictionary<string, string>(); public readonly List<string> Rest = new List<string>();
        static readonly HashSet<string> Flags = new HashSet<string> { "demo", "crt", "live", "help" };
        public Args(string[] a)
        {
            int start = 0;
            if (a.Length > 0 && (a[0].StartsWith("/") || a[0].StartsWith("--") || a[0].StartsWith("-")))
            {
                string m = a[0].ToLowerInvariant();
                if (m.StartsWith("/")) { Mode = m.Substring(0, Math.Min(2, m.Length)); if (m.Length > 3 && m[2] == ':') Rest.Add(m.Substring(3)); }
                else Mode = m;
                start = 1;
            }
            for (int i = start; i < a.Length; i++)
            {
                if (a[i].StartsWith("--"))
                {
                    string k = a[i].Substring(2).ToLowerInvariant(); string v = "1";
                    if (!Flags.Contains(k) && i + 1 < a.Length && !a[i + 1].StartsWith("--")) v = a[++i];
                    opts[k] = v;
                }
                else Rest.Add(a[i]);
            }
        }
        public bool Has(string k) { return opts.ContainsKey(k); }
        public string Get(string k, string d) { string v; return opts.TryGetValue(k, out v) ? v : d; }
        public int Int(string k, int d) { int v; return int.TryParse(Get(k, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : d; }
        public double Dbl(string k, double d) { double v; return double.TryParse(Get(k, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : d; }
    }

    static class Program
    {
        public const string Usage = @"ScreenWorm - one glowing worm per CPU core, fed by live system stats.
  screenworm /s                 run as screensaver (exits on input)
  screenworm /c                 settings dialog
  screenworm --run [--demo]     full screen on every monitor, Esc/Q or click to quit
  screenworm --window           resizable window (keys: h legend, c crt, b bloom, q quit)
  screenworm --screenshot out.png [--size 1920x1080] [--seconds 12] [--demo|--live] [--cores N] [--hud 0|1|2]
  screenworm --install          make it the active screensaver (--uninstall to undo)
  screenworm --stats            stream JSON samples to stdout
  screenworm --screens          print monitor layout
  options: --q 0.5..1 (render scale)  --particles N  --hud 0|1|2  --crt  --seconds N (auto quit)";

        [STAThread]
        static int Main(string[] args)
        {
            Native.EnableDpiAwareness();
            var a = new Args(args);
            try
            {
                switch (a.Mode)
                {
                    case "/s": return new App(RunMode.Saver, a).Run();
                    case "/p": return new App(RunMode.Preview, a).Run();
                    case "/c": Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); Application.Run(new ConfigForm()); return 0;
                    case "--run": return new App(RunMode.Demo, a).Run();
                    case "--window": return new App(RunMode.Window, a).Run();
                    case "--screenshot": return new App(RunMode.Screenshot, a).Run();
                    case "--stats": return StatsCli.Run();
                    case "--screens": Console.WriteLine(Monitors.ToJson(Monitors.Detect())); return 0;
                    case "--install": return Installer.Install(true);
                    case "--uninstall": return Installer.Install(false);
                    case "--help": case "-h": case "/?": Console.WriteLine(Usage); return 0;
                }
            }
            catch (Exception ex)
            {
                Log.Write("fatal: " + ex);
                Console.Error.WriteLine(ex);
                if (a.Mode != "/s" && a.Mode != "/p") MessageBox.Show(ex.Message, "ScreenWorm", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }
            Console.WriteLine(Usage);
            return 1;
        }
    }

    // A window that OpenGL owns: no GDI painting, own DC, input forwarded to the app.
    class GlForm : Form
    {
        public IntPtr Hdc; public RectD View; public ViewRes Res; public HudTexture Hud; public bool HudOn; public double HudScale = 1;
        public volatile int CW, CH; public volatile bool Dirty = true;
        readonly App app; readonly RunMode mode; bool haveMouse; int mx, my;

        public GlForm(App app, RunMode mode)
        {
            this.app = app; this.mode = mode;
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            FormBorderStyle = mode == RunMode.Window ? FormBorderStyle.Sizable : FormBorderStyle.None;
            ShowInTaskbar = mode == RunMode.Window; StartPosition = FormStartPosition.Manual; BackColor = Color.Black; Text = "ScreenWorm";
            TopMost = mode == RunMode.Saver;
        }
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ClassStyle |= 0x20; return cp; } } // CS_OWNDC
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Hdc = Wgl.GetDC(Handle); }
        protected override void OnClientSizeChanged(EventArgs e) { base.OnClientSizeChanged(e); CW = ClientSize.Width; CH = ClientSize.Height; Dirty = true; }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!haveMouse) { haveMouse = true; mx = e.X; my = e.Y; return; }
            if (Math.Abs(e.X - mx) + Math.Abs(e.Y - my) > 12) app.OnInput(true);
        }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); app.OnInput(false); }
        protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); app.OnKey(e.KeyCode); }
        protected override void OnFormClosed(FormClosedEventArgs e) { base.OnFormClosed(e); if (mode == RunMode.Window) app.Quit(); }
    }

    class App
    {
        readonly RunMode mode; readonly Args args; readonly Config cfg;
        readonly List<GlForm> forms = new List<GlForm>();
        volatile bool running, quitting; Exception renderError; Thread renderThread, samplerThread;
        Sampler sampler; volatile StatSample latest; readonly bool demo; Sim sim; Renderer renderer; readonly Settings settings = new Settings();
        volatile int hudMode; double quality; readonly bool pinnedQ; IntPtr parentHwnd; ApplicationContext ctx;
        readonly Stopwatch clock = Stopwatch.StartNew(); readonly double autoQuit; uint startTick, startInput;
        readonly uint seed = (uint)(Environment.TickCount * 2654435761u) | 1u;
        double fpsEma = 60, dtEma = 16.7;

        public App(RunMode mode, Args a)
        {
            this.mode = mode; args = a; cfg = Config.Load();
            demo = a.Has("demo") || (mode == RunMode.Screenshot && !a.Has("live"));
            hudMode = Math.Max(0, Math.Min(2, a.Int("hud", mode == RunMode.Preview ? 0 : cfg.Hud)));
            settings.Crt = a.Has("crt") || cfg.Crt ? 0.7 : 0;
            double q = a.Dbl("q", cfg.QualityValue); pinnedQ = a.Has("q") || cfg.QualityValue > 0; quality = q > 0 ? Math.Max(0.3, Math.Min(1, q)) : 1;
            if (mode == RunMode.Preview) { quality = 0.5; }
            autoQuit = a.Dbl("seconds", 0);
        }

        public int Run()
        {
            Log.Write("start " + mode + " args=" + string.Join(" ", Environment.GetCommandLineArgs()));
            Native.timeBeginPeriod(1);
            Mutex mutex = null;
            if (mode == RunMode.Saver) { bool created; mutex = new Mutex(true, "Local\\ScreenWorm.Saver", out created); if (!created) return 0; }
            sampler = new Sampler();
            if (mode == RunMode.Screenshot) return RunScreenshot();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            startTick = (uint)Environment.TickCount; startInput = Native.LastInputTick();
            CreateForms();
            running = true;
            samplerThread = new Thread(SampleLoop) { IsBackground = true, Name = "sampler" }; samplerThread.Start();
            renderThread = new Thread(RenderLoop) { IsBackground = true, Name = "render" }; renderThread.Start();
            ctx = new ApplicationContext();
            var timer = new System.Windows.Forms.Timer { Interval = 100 }; timer.Tick += UiTick; timer.Start();
            bool hideCursor = mode == RunMode.Saver || mode == RunMode.Demo;
            if (hideCursor) Cursor.Hide();
            Application.Run(ctx);
            timer.Stop();
            running = false;
            if (renderThread != null) renderThread.Join(5000);
            foreach (var f in forms) { try { f.Close(); f.Dispose(); } catch (Exception) { } }
            if (hideCursor) Cursor.Show();
            GC.KeepAlive(mutex);
            if (renderError != null)
            {
                Log.Write("render error: " + renderError);
                if (mode != RunMode.Saver && mode != RunMode.Preview) MessageBox.Show(renderError.Message, "ScreenWorm", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 3;
            }
            Log.Write("exit " + mode);
            return 0;
        }

        void CreateForms()
        {
            var mons = Monitors.Detect();
            if (mode == RunMode.Window)
            {
                var prim = mons[0]; int w = args.Int("w", 1600), h = args.Int("h", 900);
                var f = new GlForm(this, mode) { HudScale = prim.Dpi / 96.0 };
                f.Bounds = new Rectangle(prim.X + (prim.W - w) / 2, prim.Y + (prim.H - h) / 2, w, h);
                forms.Add(f); f.Show();
            }
            else if (mode == RunMode.Preview)
            {
                long hwnd; parentHwnd = args.Rest.Count > 0 && long.TryParse(args.Rest[0], out hwnd) ? new IntPtr(hwnd) : IntPtr.Zero;
                if (parentHwnd == IntPtr.Zero || !Native.IsWindow(parentHwnd)) throw new Exception("preview needs a parent window handle");
                Native.RECT r; Native.GetClientRect(parentHwnd, out r);
                var f = new GlForm(this, mode); f.TopLevel = false;
                f.Bounds = new Rectangle(0, 0, Math.Max(16, r.Right - r.Left), Math.Max(16, r.Bottom - r.Top));
                forms.Add(f);
                Native.SetParent(f.Handle, parentHwnd);
                f.Show();
            }
            else
            {
                for (int i = 0; i < mons.Count; i++)
                {
                    var m = mons[i];
                    var f = new GlForm(this, mode) { HudScale = m.Dpi / 96.0, View = new RectD(m.X, m.Y, m.W, m.H) };
                    f.Bounds = new Rectangle(m.X, m.Y, m.W, m.H);
                    forms.Add(f);
                }
                foreach (var f in forms) f.Show();
                forms[0].Activate();
            }
            foreach (var f in forms) { f.CW = f.ClientSize.Width; f.CH = f.ClientSize.Height; }
        }

        void SampleLoop()
        {
            int k = 0;
            while (running)
            {
                var sw = Stopwatch.StartNew();
                try { latest = demo ? Sim.DemoSample(k, sim != null ? sim.Cores : sampler.Logical) : sampler.Collect(); }
                catch (Exception ex) { Log.Write("sample: " + ex.Message); }
                k++;
                int wait = (int)Math.Max(50, 1000 - sw.ElapsedMilliseconds);
                for (int i = 0; i < wait / 50 && running; i++) Thread.Sleep(50);
            }
        }

        static List<List<int>> Pairs(int cores) { var l = new List<List<int>>(); for (int i = 0; i < cores; i += 2) l.Add(i + 1 < cores ? new List<int> { i, i + 1 } : new List<int> { i }); return l; }

        void BuildSim(RectD world, List<RectD> screens)
        {
            int cores = demo && args.Has("cores") ? Math.Max(1, args.Int("cores", sampler.Logical)) : sampler.Logical;
            var siblings = cores == sampler.Logical ? sampler.Siblings : Pairs(cores);
            sim = new Sim(seed, cores, siblings, cores == sampler.Logical ? sampler.Efficiency : null, world, screens);
            renderer.SetWorld(world, seed);
        }

        static void CheckVersion()
        {
            var v = new int[1]; GL.glGetIntegerv(GL.MAJOR_VERSION, v); int major = v[0]; GL.glGetIntegerv(GL.MINOR_VERSION, v); int minor = v[0];
            GL.glGetError();
            if (major < 3 || (major == 3 && minor < 3)) throw new Exception("OpenGL 3.3 is required; this driver reports " + GL.glGetString(GL.VERSION));
        }

        void RenderLoop()
        {
            IntPtr glCtx = IntPtr.Zero;
            try
            {
                int fmt = 0; foreach (var f in forms) fmt = Wgl.SetupPixelFormat(f.Hdc, fmt);
                glCtx = Wgl.wglCreateContext(forms[0].Hdc);
                if (glCtx == IntPtr.Zero) throw new Exception("wglCreateContext failed (" + Marshal.GetLastWin32Error() + ")");
                if (!Wgl.wglMakeCurrent(forms[0].Hdc, glCtx)) throw new Exception("wglMakeCurrent failed");
                GL.Load(); CheckVersion();
                if (GL.wglSwapIntervalEXT != null) GL.wglSwapIntervalEXT(0);
                int particles = mode == RunMode.Preview ? 16384 : args.Int("particles", cfg.Particles);
                renderer = new Renderer(particles, mode == RunMode.Preview ? 512 : 6000, sampler.Logical);
                renderer.SetQuality(quality);
                Log.Write("gl: " + renderer.Info);
                bool multi = mode == RunMode.Saver || mode == RunMode.Demo;
                var screens = new List<RectD>();
                if (multi) foreach (var f in forms) screens.Add(f.View);
                else { var w = new RectD(0, 0, forms[0].CW, forms[0].CH); forms[0].View = w; screens.Add(w); }
                BuildSim(RectD.Union(screens), screens);
                foreach (var f in forms) f.Res = new ViewRes();
                string label = multi ? forms.Count + " monitors, world " + sim.World.W + "x" + sim.World.H : forms[0].CW + "x" + forms[0].CH;
                var sw = Stopwatch.StartNew(); double last = sw.Elapsed.TotalSeconds, lastGovern = last, lastQChange = last, acc = 0, pAcc = 0; bool dwmOk = true;
                while (running)
                {
                    double now = sw.Elapsed.TotalSeconds, dt = Math.Min(0.25, now - last); last = now;
                    dtEma += (dt * 1000 - dtEma) * 0.05; fpsEma = 1000 / Math.Max(0.1, dtEma);
                    var s = latest; if (s != null) { latest = null; sim.ApplySample(s); }
                    acc += dt; int steps = 0;
                    while (acc >= Sim.Dt && steps < 12) { sim.StepOnce(); acc -= Sim.Dt; steps++; }
                    if (acc > Sim.Dt) acc = 0;
                    pAcc += dt; int pn = 0;
                    while (pAcc >= 1.0 / 60 && pn < 4) { renderer.StepParticles(sim, 1f / 60); pAcc -= 1.0 / 60; pn++; }
                    if (pAcc > 1.0 / 60) pAcc = 0;
                    for (int i = 0; i < forms.Count; i++)
                    {
                        var f = forms[i];
                        if (f.CW < 8 || f.CH < 8) continue;
                        Wgl.wglMakeCurrent(f.Hdc, glCtx);
                        if (f.Dirty || f.Res.Hdr == null || f.Res.W != f.CW || f.Res.H != f.CH || f.Res.Quality != quality)
                        {
                            f.Res.Build(f.CW, f.CH, quality, false); f.Dirty = false;
                            if (!multi) { var w = new RectD(0, 0, f.CW, f.CH); f.View = w; sim.SetWorld(w, null); renderer.SetWorld(w, seed); label = f.CW + "x" + f.CH; }
                        }
                        f.HudOn = mode != RunMode.Preview && (hudMode == 2 || (hudMode == 1 && i == 0));
                        if (f.HudOn && f.Hud == null) f.Hud = new HudTexture(f.CW, f.HudScale, demo ? "DEMO" : Environment.MachineName, sampler.CpuModel, sampler.OsName, sim, hudMode);
                        if (f.Hud != null) { f.Hud.Mode = f.HudOn ? hudMode : 0; f.Hud.Update(sim, fpsEma, renderer.ParticleCount, quality, label, renderer.Info); }
                        renderer.Render(sim, f.Res, f.View, settings, f.Hud);
                        Wgl.SwapBuffers(f.Hdc);
                    }
                    if (dwmOk) { if (Wgl.DwmFlush() != 0) dwmOk = false; }
                    else Thread.Sleep(Math.Max(1, (int)(16 - dt * 1000)));
                    if (!pinnedQ && now - lastGovern > 2)
                    {
                        lastGovern = now;
                        if (dtEma > 21 && quality > 0.5) { quality -= 0.125; renderer.SetQuality(quality); lastQChange = now; Log.Write("quality -> " + quality); }
                        else if (dtEma < 13 && quality < 1 && now - lastQChange > 12) { quality += 0.125; renderer.SetQuality(quality); lastQChange = now; Log.Write("quality -> " + quality); }
                    }
                    if (autoQuit > 0 && now > autoQuit) quitting = true;
                }
                Log.Write("render loop end: ~" + fpsEma.ToString("0", CultureInfo.InvariantCulture) + " fps, quality " + quality.ToString("0.00", CultureInfo.InvariantCulture) + ", " + renderer.ParticleCount + " particles, " + forms.Count + " window(s)");
                GL.glFinish();
                foreach (var f in forms) if (f.Res != null) f.Res.Dispose();
            }
            catch (Exception ex) { renderError = ex; quitting = true; }
            finally
            {
                try { if (glCtx != IntPtr.Zero) { Wgl.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); Wgl.wglDeleteContext(glCtx); } } catch (Exception) { }
            }
        }

        void UiTick(object sender, EventArgs e)
        {
            if (quitting || renderError != null) { foreach (var f in forms) f.Hide(); ctx.ExitThread(); return; }
            if (mode == RunMode.Saver && clock.Elapsed.TotalSeconds > 2.5)
            {
                uint li = Native.LastInputTick();
                if (li != startInput && unchecked((int)(li - startTick)) > 1500) Quit();
            }
            if (mode == RunMode.Preview && !Native.IsWindow(parentHwnd)) Quit();
        }

        public void OnInput(bool move) { if (mode == RunMode.Saver) Quit(); else if (mode == RunMode.Demo && !move) Quit(); }
        public void OnKey(Keys k)
        {
            if (mode == RunMode.Saver) { Quit(); return; }
            switch (k)
            {
                case Keys.Escape: case Keys.Q: Quit(); break;
                case Keys.H: hudMode = (hudMode + 1) % 3; break;
                case Keys.C: settings.Crt = settings.Crt > 0 ? 0 : 0.7; break;
                case Keys.B: settings.Bloom = settings.Bloom > 0 ? 0 : 1; break;
            }
        }
        public void Quit() { quitting = true; }

        // Renders N seconds into an offscreen target and saves a PNG; no window is ever shown.
        int RunScreenshot()
        {
            string outFile = args.Get("out", args.Rest.Count > 0 ? args.Rest[0] : "screenworm.png");
            int W = 1920, H = 1080; var parts = args.Get("size", "1920x1080").ToLowerInvariant().Split('x');
            if (parts.Length == 2) { int.TryParse(parts[0], out W); int.TryParse(parts[1], out H); }
            W = Math.Max(64, W); H = Math.Max(64, H);
            double seconds = Math.Max(1, args.Dbl("seconds", 12));
            var f = new GlForm(this, RunMode.Screenshot); f.ClientSize = new Size(Math.Min(W, 640), Math.Min(H, 360));
            IntPtr handle = f.Handle;
            Wgl.SetupPixelFormat(f.Hdc, 0);
            IntPtr glCtx = Wgl.wglCreateContext(f.Hdc);
            if (glCtx == IntPtr.Zero) throw new Exception("wglCreateContext failed");
            Wgl.wglMakeCurrent(f.Hdc, glCtx); GL.Load(); CheckVersion();
            int cores = demo ? Math.Max(1, args.Int("cores", sampler.Logical)) : sampler.Logical;
            renderer = new Renderer(args.Int("particles", cfg.Particles), 6000, cores);
            renderer.SetQuality(quality);
            var world = new RectD(0, 0, W, H);
            sim = new Sim(seed, cores, cores == sampler.Logical ? sampler.Siblings : Pairs(cores), cores == sampler.Logical ? sampler.Efficiency : null, world, new List<RectD> { world });
            renderer.SetWorld(world, seed);
            var vr = new ViewRes(); vr.Build(W, H, quality, true);
            HudTexture hud = hudMode > 0 ? new HudTexture(W, args.Dbl("dpr", 1), demo ? "DEMO" : Environment.MachineName, sampler.CpuModel, sampler.OsName, sim, hudMode) : null;
            int frames = (int)(seconds * 60);
            for (int i = 0; i < frames; i++)
            {
                if (i % 60 == 0)
                {
                    if (demo) sim.ApplySample(Sim.DemoSample(i / 60, cores));
                    else { if (i > 0) Thread.Sleep(1000); sim.ApplySample(sampler.Collect()); }
                }
                sim.StepOnce(); sim.StepOnce();
                renderer.StepParticles(sim, 1f / 60);
                if (i >= frames - 3)
                {
                    if (hud != null) hud.Update(sim, 60, renderer.ParticleCount, quality, W + "x" + H, renderer.Info);
                    renderer.Render(sim, vr, world, settings, hud);
                }
            }
            GL.glFinish();
            var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            GL.glBindFramebuffer(GL.FRAMEBUFFER, vr.Final.Fbo); GL.glPixelStorei(GL.PACK_ALIGNMENT, 4);
            GL.glReadPixels(0, 0, W, H, GL.BGRA, GL.UNSIGNED_BYTE, data.Scan0);
            bmp.UnlockBits(data);
            bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
            bmp.Save(outFile, ImageFormat.Png);
            Console.WriteLine("wrote " + outFile + " (" + W + "x" + H + ", " + cores + " cores, " + renderer.ParticleCount + " particles) " + renderer.Info);
            Wgl.wglMakeCurrent(IntPtr.Zero, IntPtr.Zero); Wgl.wglDeleteContext(glCtx);
            return 0;
        }
    }

    static class Installer
    {
        public static int Install(bool on)
        {
            string exe = Assembly.GetExecutingAssembly().Location, dir = Path.GetDirectoryName(exe);
            string scr = Path.Combine(dir, "screenworm.scr");
            if (!File.Exists(scr)) scr = Path.Combine(dir, "screenworm.exe");
            if (!File.Exists(scr)) scr = exe;
            using (var k = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop", true))
            {
                if (on) { k.SetValue("SCRNSAVE.EXE", scr); k.SetValue("ScreenSaveActive", "1"); }
                else k.DeleteValue("SCRNSAVE.EXE", false);
            }
            Native.SystemParametersInfo(0x11, on ? 1u : 0u, IntPtr.Zero, 2);
            string msg = on ? "ScreenWorm is now the active screensaver:\r\n" + scr + "\r\n\r\nTimeout and lock-on-resume are set in Windows' Screen Saver Settings." : "ScreenWorm is no longer the active screensaver.";
            Console.WriteLine(msg);
            return 0;
        }
    }

    class Config
    {
        public string Quality = "auto"; public int Hud = 1; public bool Crt = false; public int Particles = 262144;
        public double QualityValue { get { double v; return Quality != "auto" && double.TryParse(Quality, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0; } }
        static string FilePath { get { return Path.Combine(Log.Dir, "config.txt"); } }
        public static Config Load()
        {
            var c = new Config();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        int k = line.IndexOf('='); if (k <= 0) continue;
                        string key = line.Substring(0, k).Trim(), val = line.Substring(k + 1).Trim();
                        switch (key)
                        {
                            case "quality": c.Quality = val; break;
                            case "hud": int.TryParse(val, out c.Hud); break;
                            case "crt": c.Crt = val == "1" || val == "true"; break;
                            case "particles": int.TryParse(val, out c.Particles); break;
                        }
                    }
            }
            catch (Exception) { }
            return c;
        }
        public void Save()
        {
            Directory.CreateDirectory(Log.Dir);
            File.WriteAllText(FilePath, "quality=" + Quality + "\r\nhud=" + Hud + "\r\ncrt=" + (Crt ? 1 : 0) + "\r\nparticles=" + Particles + "\r\n");
        }
    }

    class ConfigForm : Form
    {
        readonly ComboBox quality = new ComboBox(), hud = new ComboBox(), particles = new ComboBox();
        readonly CheckBox crt = new CheckBox();
        public ConfigForm()
        {
            var cfg = Config.Load();
            Text = "ScreenWorm"; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Segoe UI", 9f); ClientSize = new Size(420, 262);
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 2, RowCount = 7 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            Setup(quality, new[] { "auto", "1", "0.75", "0.5" }, cfg.Quality);
            Setup(hud, new[] { "0", "1", "2" }, cfg.Hud.ToString(CultureInfo.InvariantCulture));
            Setup(particles, new[] { "65536", "131072", "262144", "524288", "1048576" }, cfg.Particles.ToString(CultureInfo.InvariantCulture));
            crt.Checked = cfg.Crt; crt.Text = "scanlines";
            Row(grid, 0, "Render scale (auto adapts to frame rate)", quality);
            Row(grid, 1, "Legend (0 off, 1 subtle, 2 full)", hud);
            Row(grid, 2, "Particles", particles);
            Row(grid, 3, "CRT look", crt);
            var info = new Label { Text = "One worm per logical core; length and speed follow its load.\nNetwork streaks across, disk rings out, memory fogs the nebula,\nGPU load swells the bloom.", AutoSize = true, ForeColor = Color.DimGray };
            grid.Controls.Add(info, 0, 4); grid.SetColumnSpan(info, 2);
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true };
            var ok = new Button { Text = "OK" }; var cancel = new Button { Text = "Cancel" }; var test = new Button { Text = "Preview" }; var install = new Button { Text = "Set as screensaver", AutoSize = true };
            ok.Click += delegate { Save(); Close(); };
            cancel.Click += delegate { Close(); };
            test.Click += delegate { Save(); Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--run") { UseShellExecute = false }); };
            install.Click += delegate { Save(); Installer.Install(true); MessageBox.Show(this, "ScreenWorm is now the active screensaver.\nSet the timeout in Windows' Screen Saver Settings.", "ScreenWorm"); };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel); buttons.Controls.Add(test); buttons.Controls.Add(install);
            grid.Controls.Add(buttons, 0, 6); grid.SetColumnSpan(buttons, 2);
            Controls.Add(grid); AcceptButton = ok; CancelButton = cancel;
        }
        static void Setup(ComboBox cb, string[] items, string value) { cb.DropDownStyle = ComboBoxStyle.DropDownList; cb.Items.AddRange(items); cb.SelectedItem = Array.IndexOf(items, value) >= 0 ? value : items[0]; cb.Width = 150; }
        static void Row(TableLayoutPanel g, int r, string label, Control c) { g.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 0, 6) }, 0, r); g.Controls.Add(c, 1, r); }
        void Save()
        {
            new Config { Quality = (string)quality.SelectedItem, Hud = int.Parse((string)hud.SelectedItem, CultureInfo.InvariantCulture), Crt = crt.Checked, Particles = int.Parse((string)particles.SelectedItem, CultureInfo.InvariantCulture) }.Save();
        }
    }
}
