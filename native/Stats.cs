// System statistics straight from the kernel: per-core times, memory, I/O, processes, plus PDH
// (with English counter names, so no localisation trouble) for CPU performance and GPU engines.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ScreenWorm
{
    class Sampler
    {
        public int Logical; public string CpuModel = "", OsName = ""; public ulong MemTotal; public volatile double[] LastCpu;
        public readonly List<List<int>> Siblings = new List<List<int>>(); public int[] Efficiency;
        readonly int groups; readonly int[] groupCounts; readonly long[] prevIdle, prevBusy; long prevIoR, prevIoW, prevRx, prevTx, prevMs; bool haveBase;
        Pdh pdh;

        public Sampler()
        {
            try { groups = Native.GetActiveProcessorGroupCount(); } catch (Exception) { groups = 1; }
            if (groups < 1) groups = 1;
            groupCounts = new int[groups];
            for (int g = 0; g < groups; g++) { try { groupCounts[g] = (int)Native.GetActiveProcessorCount((ushort)g); } catch (Exception) { groupCounts[g] = Environment.ProcessorCount; } }
            Logical = groups == 1 ? groupCounts[0] : (groups - 1) * 64 + groupCounts[groups - 1];
            if (Logical < 1) Logical = Environment.ProcessorCount;
            prevIdle = new long[Logical]; prevBusy = new long[Logical]; LastCpu = new double[Logical]; Efficiency = new int[Logical];
            ReadTopology();
            try { using (var k = Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0")) if (k != null) CpuModel = ((k.GetValue("ProcessorNameString") as string) ?? "").Trim(); } catch (Exception) { }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion"))
                    if (k != null)
                    {
                        string name = (k.GetValue("ProductName") as string) ?? "Windows", build = (k.GetValue("CurrentBuild") as string) ?? "";
                        int b; if (int.TryParse(build, out b) && b >= 22000) name = name.Replace("Windows 10", "Windows 11");
                        OsName = name + (build != "" ? " build " + build : "");
                    }
            }
            catch (Exception) { OsName = Environment.OSVersion.ToString(); }
            var ms = new Native.MEMORYSTATUSEX(); ms.Length = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (Native.GlobalMemoryStatusEx(ref ms)) MemTotal = ms.TotalPhys;
            try { pdh = new Pdh(); } catch (Exception ex) { Log.Write("pdh unavailable: " + ex.Message); pdh = null; }
            Collect();
        }

        void ReadTopology()
        {
            try
            {
                uint len = 0;
                Native.GetLogicalProcessorInformationEx(0, IntPtr.Zero, ref len);
                if (len == 0) return;
                IntPtr buf = Marshal.AllocHGlobal((int)len);
                try
                {
                    if (!Native.GetLogicalProcessorInformationEx(0, buf, ref len)) return;
                    int off = 0;
                    while (off < len)
                    {
                        int size = Marshal.ReadInt32(buf, off + 4);
                        byte eff = Marshal.ReadByte(buf, off + 9);
                        int groupCount = Marshal.ReadInt16(buf, off + 30);
                        var core = new List<int>();
                        for (int g = 0; g < groupCount; g++)
                        {
                            int gm = off + 32 + g * 16;
                            ulong mask = (ulong)Marshal.ReadInt64(buf, gm); int group = Marshal.ReadInt16(buf, gm + 8);
                            for (int b = 0; b < 64; b++) if ((mask & (1UL << b)) != 0) { int idx = group * 64 + b; if (idx < Logical) { core.Add(idx); Efficiency[idx] = eff; } }
                        }
                        if (core.Count > 0) Siblings.Add(core);
                        if (size <= 0) break;
                        off += size;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch (Exception) { Siblings.Clear(); }
            if (Siblings.Count == 0) for (int i = 0; i < Logical; i++) Siblings.Add(new List<int> { i });
            Siblings.Sort(delegate(List<int> a, List<int> b) { return a[0].CompareTo(b[0]); });
        }

        public string TopologyJson()
        {
            var sb = new StringBuilder("{\"physical\":").Append(Siblings.Count).Append(",\"siblings\":[");
            for (int i = 0; i < Siblings.Count; i++) { if (i > 0) sb.Append(','); sb.Append('[').Append(string.Join(",", Siblings[i].ConvertAll(x => x.ToString(CultureInfo.InvariantCulture)).ToArray())).Append(']'); }
            sb.Append("],\"efficiency\":[");
            for (int i = 0; i < Logical; i++) { if (i > 0) sb.Append(','); sb.Append(Efficiency[i]); }
            return sb.Append("]}").ToString();
        }

        public string InfoJson()
        {
            return "{\"type\":\"info\",\"cores\":" + Logical + ",\"cpuModel\":" + Json.S(CpuModel) + ",\"host\":" + Json.S(Environment.MachineName) + ",\"os\":" + Json.S(OsName) + ",\"memTotal\":" + MemTotal + ",\"topology\":" + TopologyJson() + "}";
        }

        void ReadCpu(long[] idle, long[] busy)
        {
            const int sz = 48;
            for (int g = 0; g < groups; g++)
            {
                int n = groupCounts[g];
                IntPtr buf = Marshal.AllocHGlobal(sz * n);
                try
                {
                    int ret, st;
                    if (groups == 1) st = Native.NtQuerySystemInformation(8, buf, sz * n, out ret);
                    else { ushort gg = (ushort)g; st = Native.NtQuerySystemInformationEx(8, ref gg, 2, buf, sz * n, out ret); }
                    if (st != 0) continue;
                    int cnt = Math.Min(n, ret / sz);
                    for (int i = 0; i < cnt; i++)
                    {
                        int idx = g * 64 + i; if (idx >= Logical) break;
                        long id = Marshal.ReadInt64(buf, i * sz), kr = Marshal.ReadInt64(buf, i * sz + 8), us = Marshal.ReadInt64(buf, i * sz + 16);
                        idle[idx] = id; busy[idx] = kr + us - id;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }

        void ReadIo(out long rd, out long wr)
        {
            IntPtr buf = Marshal.AllocHGlobal(1024);
            try { int ret; if (Native.NtQuerySystemInformation(2, buf, 1024, out ret) == 0) { rd = Marshal.ReadInt64(buf, 8); wr = Marshal.ReadInt64(buf, 16); } else { rd = 0; wr = 0; } }
            finally { Marshal.FreeHGlobal(buf); }
        }

        void ReadProcs(out int procs, out int threads)
        {
            procs = 0; threads = 0;
            int size = 512 * 1024;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    int ret; int st = Native.NtQuerySystemInformation(5, buf, size, out ret);
                    if (st == unchecked((int)0xC0000004)) { size = Math.Max(size * 2, ret + 65536); continue; }
                    if (st != 0) return;
                    int off = 0;
                    while (true)
                    {
                        int next = Marshal.ReadInt32(buf, off); int nt = Marshal.ReadInt32(buf, off + 4);
                        procs++; threads += nt;
                        if (next == 0) break; off += next;
                    }
                    return;
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }

        static void ReadNet(out long rx, out long tx)
        {
            rx = 0; tx = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var st = ni.GetIPv4Statistics(); rx += st.BytesReceived; tx += st.BytesSent;
                }
            }
            catch (Exception) { }
        }

        public StatSample Collect()
        {
            long now = Json.NowMs();
            var idle = new long[Logical]; var busy = new long[Logical];
            ReadCpu(idle, busy);
            var c = new StatSample { Cpu = new double[Logical], MemTotal = MemTotal };
            double dt = haveBase ? Math.Max(0.001, (now - prevMs) / 1000.0) : 1;
            double sum = 0;
            for (int i = 0; i < Logical; i++)
            {
                long di = idle[i] - prevIdle[i], db = busy[i] - prevBusy[i];
                double u = haveBase && (di + db) > 0 ? (double)db / (di + db) : 0;
                c.Cpu[i] = Math.Max(0, Math.Min(1, u)); sum += c.Cpu[i];
                prevIdle[i] = idle[i]; prevBusy[i] = busy[i];
            }
            c.CpuTotal = Logical > 0 ? sum / Logical : 0;
            LastCpu = c.Cpu;
            var ms = new Native.MEMORYSTATUSEX(); ms.Length = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX));
            if (Native.GlobalMemoryStatusEx(ref ms)) { c.MemAvail = ms.AvailPhys; c.MemUsed = MemTotal > 0 ? 1.0 - (double)ms.AvailPhys / MemTotal : 0; }
            long rd, wr; ReadIo(out rd, out wr);
            long rx, tx; ReadNet(out rx, out tx);
            if (haveBase) { c.Read = Math.Max(0, (rd - prevIoR) / dt); c.Write = Math.Max(0, (wr - prevIoW) / dt); c.Rx = Math.Max(0, (rx - prevRx) / dt); c.Tx = Math.Max(0, (tx - prevTx) / dt); }
            prevIoR = rd; prevIoW = wr; prevRx = rx; prevTx = tx;
            int p, t; ReadProcs(out p, out t); c.Procs = p; c.Threads = t;
            c.Uptime = Native.GetTickCount64() / 1000.0;
            if (pdh != null)
            {
                try { pdh.Collect(Logical, c); }
                catch (Exception ex) { Log.Write("pdh failed: " + ex.Message); pdh = null; }
            }
            prevMs = now; haveBase = true;
            return c;
        }

        public string SampleJson(int seq, long t, bool withType)
        {
            var c = Collect();
            var sb = new StringBuilder(withType ? "{\"type\":\"sample\"," : "{");
            sb.Append("\"seq\":").Append(seq).Append(",\"t\":").Append(t).Append(",\"cpu\":[");
            for (int i = 0; i < c.Cpu.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Json.N(c.Cpu[i])); }
            sb.Append("],\"cpuTotal\":").Append(Json.N(c.CpuTotal));
            if (c.Perf != null) { sb.Append(",\"perf\":["); for (int i = 0; i < c.Perf.Length; i++) { if (i > 0) sb.Append(','); sb.Append(Json.N(c.Perf[i])); } sb.Append("],\"perfTotal\":").Append(Json.N(c.PerfTotal)); }
            if (c.Gpu >= 0) sb.Append(",\"gpu\":").Append(Json.N(c.Gpu));
            sb.Append(",\"mem\":{\"used\":").Append(Json.N(c.MemUsed)).Append(",\"total\":").Append(MemTotal).Append(",\"avail\":").Append(Json.N(c.MemAvail)).Append('}');
            sb.Append(",\"net\":{\"rx\":").Append(Json.N(c.Rx)).Append(",\"tx\":").Append(Json.N(c.Tx)).Append('}');
            sb.Append(",\"disk\":{\"read\":").Append(Json.N(c.Read)).Append(",\"write\":").Append(Json.N(c.Write)).Append('}');
            sb.Append(",\"sys\":{\"procs\":").Append(c.Procs).Append(",\"threads\":").Append(c.Threads).Append(",\"uptime\":").Append(Json.N(c.Uptime)).Append("}}");
            return sb.ToString();
        }

        class Pdh
        {
            readonly IntPtr query; readonly IntPtr perfCounter, gpuCounter;
            public Pdh()
            {
                if (Native.PdhOpenQueryW(null, IntPtr.Zero, out query) != 0) throw new Exception("PdhOpenQuery failed");
                if (Native.PdhAddEnglishCounterW(query, "\\Processor Information(*)\\% Processor Performance", IntPtr.Zero, out perfCounter) != 0) perfCounter = IntPtr.Zero;
                if (Native.PdhAddEnglishCounterW(query, "\\GPU Engine(*)\\Utilization Percentage", IntPtr.Zero, out gpuCounter) != 0) gpuCounter = IntPtr.Zero;
                Native.PdhCollectQueryData(query);
            }
            public void Collect(int logical, StatSample c)
            {
                if (Native.PdhCollectQueryData(query) != 0) return;
                if (perfCounter != IntPtr.Zero)
                {
                    var perf = new double[logical]; bool any = false; double sum = 0; int cnt = 0;
                    foreach (var kv in ReadArray(perfCounter))
                    {
                        if (kv.Key.IndexOf("_Total", StringComparison.Ordinal) >= 0) continue;
                        string[] parts = kv.Key.Split(','); int g, i;
                        if (parts.Length == 2 && int.TryParse(parts[0], out g) && int.TryParse(parts[1], out i)) { int idx = g * 64 + i; if (idx < logical) { perf[idx] = kv.Value / 100.0; any = true; sum += perf[idx]; cnt++; } }
                    }
                    if (any) { c.Perf = perf; c.PerfTotal = cnt > 0 ? sum / cnt : 1; }
                }
                if (gpuCounter != IntPtr.Zero)
                {
                    var byType = new Dictionary<string, double>();
                    foreach (var kv in ReadArray(gpuCounter))
                    {
                        int k = kv.Key.IndexOf("engtype_", StringComparison.Ordinal); string type = k >= 0 ? kv.Key.Substring(k + 8) : "?";
                        double v; byType.TryGetValue(type, out v); byType[type] = v + kv.Value;
                    }
                    double max = 0; foreach (var v in byType.Values) max = Math.Max(max, v);
                    c.Gpu = Math.Min(1, max / 100.0);
                }
            }
            static List<KeyValuePair<string, double>> ReadArray(IntPtr counter)
            {
                var list = new List<KeyValuePair<string, double>>();
                uint size = 0, count;
                uint st = Native.PdhGetFormattedCounterArrayW(counter, 0x200, ref size, out count, IntPtr.Zero);
                if (st != 0x800007D2 || size == 0) return list;
                IntPtr buf = Marshal.AllocHGlobal((int)size);
                try
                {
                    st = Native.PdhGetFormattedCounterArrayW(counter, 0x200, ref size, out count, buf);
                    if (st != 0) return list;
                    for (int i = 0; i < count; i++)
                    {
                        int b = i * 24;
                        IntPtr namePtr = Marshal.ReadIntPtr(buf, b);
                        uint status = (uint)Marshal.ReadInt32(buf, b + 8);
                        double val = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(buf, b + 16));
                        if (status != 0) continue;
                        list.Add(new KeyValuePair<string, double>(Marshal.PtrToStringUni(namePtr), val));
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
                return list;
            }
        }
    }

    static class StatsCli
    {
        public static int Run()
        {
            var s = new Sampler();
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            stdout.WriteLine(s.InfoJson());
            long t0 = Json.NowMs(); int seq = 0;
            while (true)
            {
                long due = t0 + (long)(seq + 1) * 1000;
                long wait = due - Json.NowMs(); if (wait > 0) Thread.Sleep((int)wait);
                stdout.WriteLine(s.SampleJson(seq++, due, true));
            }
        }
    }

    class MonitorInfo { public int X, Y, W, H, Dpi = 96; public bool Primary; public string Name; }

    static class Monitors
    {
        public static List<MonitorInfo> Detect()
        {
            var list = new List<MonitorInfo>();
            foreach (var s in Screen.AllScreens)
            {
                var m = new MonitorInfo { X = s.Bounds.X, Y = s.Bounds.Y, W = s.Bounds.Width, H = s.Bounds.Height, Primary = s.Primary, Name = s.DeviceName };
                try
                {
                    IntPtr hm = Native.MonitorFromPoint(new Native.POINT { X = s.Bounds.X + 2, Y = s.Bounds.Y + 2 }, 2);
                    uint dx, dy; if (Native.GetDpiForMonitor(hm, 0, out dx, out dy) == 0 && dx > 0) m.Dpi = (int)dx;
                }
                catch (Exception) { }
                list.Add(m);
            }
            list.Sort(delegate(MonitorInfo a, MonitorInfo b) { if (a.Primary != b.Primary) return a.Primary ? -1 : 1; int c = a.X.CompareTo(b.X); return c != 0 ? c : a.Y.CompareTo(b.Y); });
            return list;
        }

        public static string ToJson(List<MonitorInfo> screens)
        {
            var sb = new StringBuilder("{\"screens\":[");
            for (int i = 0; i < screens.Count; i++)
            {
                var s = screens[i]; if (i > 0) sb.Append(',');
                sb.Append("{\"x\":").Append(s.X).Append(",\"y\":").Append(s.Y).Append(",\"w\":").Append(s.W).Append(",\"h\":").Append(s.H).Append(",\"dpi\":").Append(s.Dpi)
                  .Append(",\"primary\":").Append(s.Primary ? "true" : "false").Append(",\"name\":").Append(Json.S(s.Name)).Append('}');
            }
            return sb.Append("]}").ToString();
        }
    }

    static class Json
    {
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static long NowMs() { return (long)(DateTime.UtcNow - Epoch).TotalMilliseconds; }
        public static string N(double v) { if (double.IsNaN(v) || double.IsInfinity(v)) v = 0; return v.ToString("0.#####", CultureInfo.InvariantCulture); }
        public static string S(string s)
        {
            if (s == null) return "\"\"";
            var sb = new StringBuilder("\"");
            foreach (char c in s) { if (c == '"' || c == '\\') sb.Append('\\').Append(c); else if (c < 32) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); }
            return sb.Append('"').ToString();
        }
    }

    static class Log
    {
        static readonly object gate = new object();
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenWorm"); } }
        public static void Write(string msg)
        {
            try
            {
                lock (gate)
                {
                    Directory.CreateDirectory(Dir);
                    string f = Path.Combine(Dir, "screenworm.log");
                    if (File.Exists(f) && new FileInfo(f).Length > 1 << 20) File.Delete(f);
                    File.AppendAllText(f, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss ") + msg + "\r\n");
                }
            }
            catch (Exception) { }
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] static extern IntPtr SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        public static void EnableDpiAwareness() { try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (Exception) { try { SetProcessDPIAware(); } catch (Exception) { } } }

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [StructLayout(LayoutKind.Sequential)] public struct MEMORYSTATUSEX { public uint Length, MemoryLoad; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual; }

        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);
        public static uint LastInputTick() { var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) }; return GetLastInputInfo(ref li) ? li.dwTime : 0; }
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmon, int type, out uint dx, out uint dy);
        [DllImport("user32.dll")] public static extern IntPtr SetParent(IntPtr child, IntPtr parent);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, IntPtr v, uint flags);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        [DllImport("kernel32.dll")] public static extern ushort GetActiveProcessorGroupCount();
        [DllImport("kernel32.dll")] public static extern uint GetActiveProcessorCount(ushort group);
        [DllImport("kernel32.dll")] public static extern bool GetLogicalProcessorInformationEx(int rel, IntPtr buf, ref uint len);
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("ntdll.dll")] public static extern int NtQuerySystemInformation(int cls, IntPtr buf, int len, out int ret);
        [DllImport("ntdll.dll")] public static extern int NtQuerySystemInformationEx(int cls, ref ushort input, int inputLen, IntPtr buf, int len, out int ret);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhOpenQueryW(string src, IntPtr userData, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);
        [DllImport("pdh.dll")] public static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] public static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufSize, out uint count, IntPtr buffer);
    }
}
