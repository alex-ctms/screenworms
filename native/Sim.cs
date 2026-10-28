// The worm colony: one worm per logical core, plus network comets and disk rings.
// Fixed 120 Hz timestep; all randomness comes from a seeded generator.
using System;
using System.Collections.Generic;

namespace ScreenWorm
{
    struct RectD
    {
        public double X, Y, W, H;
        public RectD(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
        public bool Contains(double x, double y) { return x >= X && x < X + W && y >= Y && y < Y + H; }
        public static RectD Union(IList<RectD> rs)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var r in rs) { x0 = Math.Min(x0, r.X); y0 = Math.Min(y0, r.Y); x1 = Math.Max(x1, r.X + r.W); y1 = Math.Max(y1, r.Y + r.H); }
            return new RectD(x0, y0, x1 - x0, y1 - y0);
        }
    }

    class Mulberry
    {
        uint a;
        public Mulberry(uint seed) { a = seed; }
        public double Next()
        {
            unchecked
            {
                a += 0x6D2B79F5u;
                uint t = a;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    class Simplex3
    {
        static readonly int[][] Grad = {
            new[] { 1, 1, 0 }, new[] { -1, 1, 0 }, new[] { 1, -1, 0 }, new[] { -1, -1, 0 },
            new[] { 1, 0, 1 }, new[] { -1, 0, 1 }, new[] { 1, 0, -1 }, new[] { -1, 0, -1 },
            new[] { 0, 1, 1 }, new[] { 0, -1, 1 }, new[] { 0, 1, -1 }, new[] { 0, -1, -1 } };
        const double F3 = 1.0 / 3.0, G3 = 1.0 / 6.0;
        readonly byte[] perm = new byte[512], permMod12 = new byte[512];

        public Simplex3(uint seed)
        {
            var rnd = new Mulberry(seed); var p = new byte[256];
            for (int i = 0; i < 256; i++) p[i] = (byte)i;
            for (int i = 255; i > 0; i--) { int j = (int)Math.Floor(rnd.Next() * (i + 1)); byte t = p[i]; p[i] = p[j]; p[j] = t; }
            for (int i = 0; i < 512; i++) { perm[i] = p[i & 255]; permMod12[i] = (byte)(perm[i] % 12); }
        }

        public double Noise(double xin, double yin, double zin)
        {
            double n0 = 0, n1 = 0, n2 = 0, n3 = 0;
            double s = (xin + yin + zin) * F3;
            int i = (int)Math.Floor(xin + s), j = (int)Math.Floor(yin + s), k = (int)Math.Floor(zin + s);
            double t = (i + j + k) * G3;
            double x0 = xin - (i - t), y0 = yin - (j - t), z0 = zin - (k - t);
            int i1, j1, k1, i2, j2, k2;
            if (x0 >= y0)
            {
                if (y0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
                else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
                else { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
            }
            else
            {
                if (y0 < z0) { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
                else if (x0 < z0) { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
                else { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
            }
            double x1 = x0 - i1 + G3, y1 = y0 - j1 + G3, z1 = z0 - k1 + G3;
            double x2 = x0 - i2 + 2 * G3, y2 = y0 - j2 + 2 * G3, z2 = z0 - k2 + 2 * G3;
            double x3 = x0 - 1 + 3 * G3, y3 = y0 - 1 + 3 * G3, z3 = z0 - 1 + 3 * G3;
            int ii = i & 255, jj = j & 255, kk = k & 255;
            double t0 = 0.6 - x0 * x0 - y0 * y0 - z0 * z0;
            if (t0 > 0) { int[] g = Grad[permMod12[ii + perm[jj + perm[kk]]]]; t0 *= t0; n0 = t0 * t0 * (g[0] * x0 + g[1] * y0 + g[2] * z0); }
            double t1 = 0.6 - x1 * x1 - y1 * y1 - z1 * z1;
            if (t1 > 0) { int[] g = Grad[permMod12[ii + i1 + perm[jj + j1 + perm[kk + k1]]]]; t1 *= t1; n1 = t1 * t1 * (g[0] * x1 + g[1] * y1 + g[2] * z1); }
            double t2 = 0.6 - x2 * x2 - y2 * y2 - z2 * z2;
            if (t2 > 0) { int[] g = Grad[permMod12[ii + i2 + perm[jj + j2 + perm[kk + k2]]]]; t2 *= t2; n2 = t2 * t2 * (g[0] * x2 + g[1] * y2 + g[2] * z2); }
            double t3 = 0.6 - x3 * x3 - y3 * y3 - z3 * z3;
            if (t3 > 0) { int[] g = Grad[permMod12[ii + 1 + perm[jj + 1 + perm[kk + 1]]]]; t3 *= t3; n3 = t3 * t3 * (g[0] * x3 + g[1] * y3 + g[2] * z3); }
            return 32 * (n0 + n1 + n2 + n3);
        }
    }

    // One second of system statistics.
    class StatSample
    {
        public double[] Cpu = new double[0]; public double[] Perf;
        public double CpuTotal, MemUsed, MemAvail, MemTotal, Rx, Tx, Read, Write, Gpu = -1, Threads, Procs, PerfTotal = 1, Uptime;
    }

    class Worm
    {
        public int Index, Group, Rank, Eff, Sibling = -1;
        public float R, G, B;
        public double X, Y, Ang, Speed = 60, Wander, Util, UtilT, Perf = 1, Len = 100, Width = 6, WidthT = 6, Phase, LastPx, LastPy;
        public readonly float[] Trail = new float[Sim.TrailCap * 2]; public int Head, Count;
    }
    class Comet { public int Kind; public double Size, Age, Phase, X, Y, Vx, Vy; }
    class Ring { public int Kind; public double X, Y, Age, Life, MaxR, Size; }
    class Eased
    {
        public double Cpu, Mem, Rx, Tx, Read, Write, Gpu, Threads, Procs, Perf = 1;
        public void Approach(Eased t, double k)
        {
            Cpu += (t.Cpu - Cpu) * k; Mem += (t.Mem - Mem) * k; Rx += (t.Rx - Rx) * k; Tx += (t.Tx - Tx) * k; Read += (t.Read - Read) * k;
            Write += (t.Write - Write) * k; Gpu += (t.Gpu - Gpu) * k; Threads += (t.Threads - Threads) * k; Procs += (t.Procs - Procs) * k; Perf += (t.Perf - Perf) * k;
        }
    }

    class Sim
    {
        public const int StepHz = 120; public const double Dt = 1.0 / StepHz; public const int TrailCap = 1024;
        const double Spacing = 3.0; const int MaxComets = 320, MaxRings = 160; const double Tau = Math.PI * 2;
        static readonly double EaseUtil = Ease(0.45), EaseSpeed = Ease(0.6), EaseLen = Ease(0.9), EaseWidth = Ease(0.7), EaseStat = Ease(0.8);
        static double Ease(double tau) { return 1 - Math.Exp(-Dt / tau); }

        public readonly List<Worm> Worms = new List<Worm>();
        public readonly List<Comet> Comets = new List<Comet>();
        public readonly List<Ring> Rings = new List<Ring>();
        public RectD World; public List<RectD> Screens; public double Time; public long Step; public readonly int Cores;
        public readonly Eased Stats = new Eased(), StatsT = new Eased(); public StatSample LastSample = new StatSample(); public bool HasGpu;
        readonly Mulberry rnd; readonly Simplex3 noise; double lenScale = 1, widthScale = 1;
        readonly double[] cometAcc = new double[2], ringAcc = new double[2];

        public Sim(uint seed, int cores, List<List<int>> siblings, int[] efficiency, RectD world, List<RectD> screens)
        {
            rnd = new Mulberry(seed); noise = new Simplex3(seed ^ 0xA5A5A5A5u); Cores = Math.Max(1, cores);
            SetWorld(world, screens);
            int groups = siblings != null && siblings.Count > 0 ? siblings.Count : Cores;
            for (int i = 0; i < Cores; i++)
            {
                int group = i, rank = 0;
                if (siblings != null) for (int g = 0; g < siblings.Count; g++) { int r = siblings[g].IndexOf(i); if (r >= 0) { group = g; rank = r; break; } }
                double hue = ((double)group / groups + 0.62) % 1.0;
                float[] rgb = rank == 0 ? Hsv(hue, 0.92, 1.0) : Hsv(hue + 0.015, 0.62, 1.0);
                var sc = Screens[i % Screens.Count];
                var w = new Worm { Index = i, Group = group, Rank = rank, Eff = efficiency != null && i < efficiency.Length ? efficiency[i] : 0, R = rgb[0], G = rgb[1], B = rgb[2] };
                w.X = sc.X + sc.W * (0.15 + 0.7 * rnd.Next()); w.Y = sc.Y + sc.H * (0.15 + 0.7 * rnd.Next());
                w.Ang = rnd.Next() * Tau; w.Phase = rnd.Next() * Tau; w.LastPx = w.X; w.LastPy = w.Y;
                Worms.Add(w);
            }
            if (siblings != null) foreach (var w in Worms) foreach (var o in Worms) if (o != w && o.Group == w.Group) { w.Sibling = o.Index; break; }
        }

        public void SetWorld(RectD world, List<RectD> screens)
        {
            World = world;
            Screens = screens != null && screens.Count > 0 ? new List<RectD>(screens) : new List<RectD> { world };
            double diag = Math.Sqrt(world.W * world.W + world.H * world.H);
            lenScale = Clamp(diag / 3200, 0.4, 1.0);
            widthScale = Clamp(diag / 4200, 0.75, 1.0);
        }

        public static float[] Hsv(double h, double s, double v)
        {
            h = ((h % 1) + 1) % 1;
            int i = (int)Math.Floor(h * 6); double f = h * 6 - i;
            double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
            switch (i % 6)
            {
                case 0: return new[] { (float)v, (float)t, (float)p };
                case 1: return new[] { (float)q, (float)v, (float)p };
                case 2: return new[] { (float)p, (float)v, (float)t };
                case 3: return new[] { (float)p, (float)q, (float)v };
                case 4: return new[] { (float)t, (float)p, (float)v };
                default: return new[] { (float)v, (float)p, (float)q };
            }
        }

        static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        static double AngDiff(double a, double b) { double d = a - b; while (d > Math.PI) d -= Tau; while (d < -Math.PI) d += Tau; return d; }

        public void ApplySample(StatSample s)
        {
            LastSample = s;
            foreach (var w in Worms)
            {
                w.UtilT = w.Index < s.Cpu.Length ? Clamp(s.Cpu[w.Index], 0, 1) : 0;
                w.Perf = s.Perf != null && w.Index < s.Perf.Length ? s.Perf[w.Index] : 1;
            }
            var t = StatsT;
            t.Cpu = Clamp(s.CpuTotal, 0, 1); t.Mem = Clamp(s.MemUsed, 0, 1); t.Rx = s.Rx; t.Tx = s.Tx; t.Read = s.Read; t.Write = s.Write;
            t.Gpu = s.Gpu >= 0 ? Clamp(s.Gpu, 0, 1) : 0; HasGpu = s.Gpu >= 0; t.Threads = s.Threads; t.Procs = s.Procs; t.Perf = s.PerfTotal;
        }

        public void StepOnce()
        {
            double t = Time;
            Stats.Approach(StatsT, EaseStat);
            foreach (var w in Worms) StepWorm(w, t);
            StepComets(); StepRings();
            Step++; Time = Step * Dt;
        }

        bool InAnyScreen(double x, double y) { foreach (var s in Screens) if (s.Contains(x, y)) return true; return false; }

        void StepWorm(Worm w, double t)
        {
            var world = World;
            w.Util += (w.UtilT - w.Util) * EaseUtil;
            double u = w.Util;
            w.Speed += (70 + 800 * Math.Pow(u, 0.8) - w.Speed) * EaseSpeed;
            w.Len += ((160 + 1500 * Math.Pow(u, 0.85)) * lenScale - w.Len) * EaseLen;
            w.WidthT = (7 + 14 * u) * widthScale * (w.Eff > 0 ? 0.8 : 1);
            w.Width += (w.WidthT - w.Width) * EaseWidth;

            double av = noise.Noise(w.X * 0.0022, w.Y * 0.0022, t * 0.12 + w.Index * 3.7) * 1.5;
            w.Wander = w.Wander * 0.985 + (rnd.Next() - 0.5) * 0.28;
            av += w.Wander * 1.1;
            double freq = 4 + 11 * u;
            av += Math.Sin(t * freq + w.Phase) * (0.12 + 0.5 * u) * freq * u;

            const double m = 170;
            double tx = 0, ty = 0;
            double dl = w.X - world.X, dr = world.X + world.W - w.X, dtp = w.Y - world.Y, db = world.Y + world.H - w.Y;
            if (dl < m) tx += (m - dl) / m; if (dr < m) tx -= (m - dr) / m;
            if (dtp < m) ty += (m - dtp) / m; if (db < m) ty -= (m - db) / m;
            if (tx != 0 || ty != 0) av += AngDiff(Math.Atan2(ty, tx), w.Ang) * 5 * Math.Min(1, Math.Sqrt(tx * tx + ty * ty));
            if (Screens.Count > 1 && !InAnyScreen(w.X, w.Y))
            {
                double bcx = 0, bcy = 0, bd = double.MaxValue;
                foreach (var s in Screens)
                {
                    double cx = s.X + s.W / 2, cy = s.Y + s.H / 2, d = Math.Sqrt((cx - w.X) * (cx - w.X) + (cy - w.Y) * (cy - w.Y));
                    if (d < bd) { bd = d; bcx = cx; bcy = cy; }
                }
                av += AngDiff(Math.Atan2(bcy - w.Y, bcx - w.X), w.Ang) * 1.2;
            }
            foreach (var o in Worms)
            {
                if (o == w) continue;
                double dx = o.X - w.X, dy = o.Y - w.Y, d2 = dx * dx + dy * dy;
                if (d2 < 90 * 90) { double d = Math.Sqrt(d2); if (d < 1) d = 1; av += AngDiff(Math.Atan2(-dy, -dx), w.Ang) * (1 - d / 90) * 2.5; }
                else if (o.Index == w.Sibling && d2 < 700 * 700 && d2 > 160 * 160) av += AngDiff(Math.Atan2(dy, dx), w.Ang) * 0.45;
            }
            double maxAv = w.Speed / Math.Max(55, 3.5 * w.Width);
            av = Clamp(av, -maxAv, maxAv);
            w.Ang += av * Dt;
            if (w.Ang > Math.PI) w.Ang -= Tau; else if (w.Ang < -Math.PI) w.Ang += Tau;
            w.X += Math.Cos(w.Ang) * w.Speed * Dt;
            w.Y += Math.Sin(w.Ang) * w.Speed * Dt;
            if (w.X < world.X - 40) { w.X = world.X - 40; w.Ang = 0; }
            if (w.X > world.X + world.W + 40) { w.X = world.X + world.W + 40; w.Ang = Math.PI; }
            if (w.Y < world.Y - 40) { w.Y = world.Y - 40; w.Ang = Math.PI / 2; }
            if (w.Y > world.Y + world.H + 40) { w.Y = world.Y + world.H + 40; w.Ang = -Math.PI / 2; }

            double ddx = w.X - w.LastPx, ddy = w.Y - w.LastPy;
            if (ddx * ddx + ddy * ddy >= Spacing * Spacing)
            {
                int idx = w.Head * 2;
                w.Trail[idx] = (float)w.X; w.Trail[idx + 1] = (float)w.Y;
                w.Head = (w.Head + 1) % TrailCap;
                if (w.Count < TrailCap) w.Count++;
                w.LastPx = w.X; w.LastPy = w.Y;
            }
        }

        void StepComets()
        {
            double[] rates = { Stats.Rx, Stats.Tx };
            for (int k = 0; k < 2; k++)
            {
                double bps = rates[k];
                double rate = bps > 0 ? Math.Pow(Math.Log10(1 + bps / 200), 1.6) : 0;
                cometAcc[k] += rate * Dt;
                while (cometAcc[k] >= 1) { cometAcc[k] -= 1; SpawnComet(k, bps); }
            }
            var W = World;
            for (int i = Comets.Count - 1; i >= 0; i--)
            {
                var c = Comets[i];
                c.X += c.Vx * Dt; c.Y += c.Vy * Dt; c.Age += Dt;
                c.Vy += Math.Sin(c.Age * 3 + c.Phase) * 40 * Dt;
                if (c.X < W.X - 400 || c.X > W.X + W.W + 400 || c.Y < W.Y - 400 || c.Y > W.Y + W.H + 400 || c.Age > 12) Comets.RemoveAt(i);
            }
        }

        void SpawnComet(int kind, double bps)
        {
            if (Comets.Count >= MaxComets) Comets.RemoveAt(0);
            var W = World;
            var sc = Screens[(int)Math.Floor(rnd.Next() * Screens.Count)];
            double y = sc.Y + sc.H * (0.05 + 0.9 * rnd.Next());
            double size = Clamp(Math.Log10(1 + bps) / 8.5, 0.12, 1);
            double speed = 1100 + 1500 * rnd.Next();
            int dir = kind == 0 ? 1 : -1;
            double ang = (rnd.Next() - 0.5) * 0.25;
            Comets.Add(new Comet { Kind = kind, Size = size, Phase = rnd.Next() * Tau, X = kind == 0 ? W.X - 60 : W.X + W.W + 60, Y = y, Vx = Math.Cos(ang) * speed * dir, Vy = Math.Sin(ang) * speed });
        }

        void StepRings()
        {
            double[] rates = { Stats.Read, Stats.Write };
            for (int k = 0; k < 2; k++)
            {
                double bps = rates[k];
                double rate = bps > 0 ? Math.Pow(Math.Log10(1 + bps / 4000), 1.5) * 0.9 : 0;
                ringAcc[k] += rate * Dt;
                while (ringAcc[k] >= 1)
                {
                    ringAcc[k] -= 1;
                    if (Rings.Count >= MaxRings) Rings.RemoveAt(0);
                    var sc = Screens[(int)Math.Floor(rnd.Next() * Screens.Count)];
                    double size = Clamp(Math.Log10(1 + bps) / 9, 0.15, 1);
                    Rings.Add(new Ring { Kind = k, X = sc.X + sc.W * (0.1 + 0.8 * rnd.Next()), Y = sc.Y + sc.H * (0.1 + 0.8 * rnd.Next()), Life = 1.4 + 1.2 * size, MaxR = 80 + 200 * size, Size = size });
                }
            }
            for (int i = Rings.Count - 1; i >= 0; i--) { var r = Rings[i]; r.Age += Dt; if (r.Age >= r.Life) Rings.RemoveAt(i); }
        }

        // Centre line for rendering: 6 floats per vertex, 2 vertices per point; walks the trail back len px.
        public int Body(Worm w, float[] o, int offset)
        {
            int p = offset, n = 0;
            double px = w.X, py = w.Y, dist = 0;
            int cap = Math.Min(w.Count, TrailCap - 2);
            float nx = (float)-Math.Sin(w.Ang), ny = (float)Math.Cos(w.Ang);
            double lastX = px, lastY = py;
            o[p++] = (float)px; o[p++] = (float)py; o[p++] = nx; o[p++] = ny; o[p++] = -1; o[p++] = 0;
            o[p++] = (float)px; o[p++] = (float)py; o[p++] = nx; o[p++] = ny; o[p++] = 1; o[p++] = 0;
            n++;
            for (int j = 0; j < cap && dist < w.Len; j++)
            {
                int idx = ((w.Head - 1 - j + TrailCap) % TrailCap) * 2;
                double x = w.Trail[idx], y = w.Trail[idx + 1];
                double segX = x - lastX, segY = y - lastY, seg = Math.Sqrt(segX * segX + segY * segY);
                if (seg < 0.5) continue;
                dist += seg;
                double tX, tY;
                if (j + 1 < cap) { int nidx = ((w.Head - 2 - j + TrailCap) % TrailCap) * 2; tX = w.Trail[nidx] - lastX; tY = w.Trail[nidx + 1] - lastY; }
                else { tX = segX; tY = segY; }
                double tl = Math.Sqrt(tX * tX + tY * tY); if (tl < 1e-6) tl = 1;
                nx = (float)(-tY / tl); ny = (float)(tX / tl);
                float d = (float)dist;
                o[p++] = (float)x; o[p++] = (float)y; o[p++] = nx; o[p++] = ny; o[p++] = -1; o[p++] = d;
                o[p++] = (float)x; o[p++] = (float)y; o[p++] = nx; o[p++] = ny; o[p++] = 1; o[p++] = d;
                n++;
                lastX = x; lastY = y;
            }
            return n;
        }

        // Synthetic workload for previews and screenshots.
        public static StatSample DemoSample(int k, int cores)
        {
            var s = new StatSample { Cpu = new double[cores] };
            double tot = 0;
            for (int i = 0; i < cores; i++)
            {
                double slow = 0.5 + 0.5 * Math.Sin(k * 0.05 + i * 0.9);
                double burst = ((k + i * 7) % 90) < 14 ? 0.75 : 0;
                double jitter = 0.5 + 0.5 * Math.Sin(k * 1.3 + i * 2.1);
                s.Cpu[i] = Math.Min(1, 0.03 + 0.35 * slow * jitter + burst * (0.7 + 0.3 * jitter)); tot += s.Cpu[i];
            }
            s.CpuTotal = tot / cores;
            Func<double, double, double> wave = (p, ph) => Math.Max(0, Math.Sin(k * p + ph));
            s.MemTotal = 64e9; s.MemUsed = 0.42 + 0.2 * Math.Sin(k * 0.02); s.MemAvail = s.MemTotal * (1 - s.MemUsed);
            s.Rx = 2e3 + 4e6 * wave(0.07, 1); s.Tx = 1e3 + 6e5 * wave(0.05, 3);
            s.Read = 3e5 * wave(0.09, 2); s.Write = 2e6 * wave(0.04, 5);
            s.Gpu = 0.1 + 0.5 * wave(0.03, 0); s.Procs = 360; s.Threads = 9000 + 2000 * Math.Sin(k * 0.01); s.Uptime = 12345 + k;
            return s;
        }
    }
}
