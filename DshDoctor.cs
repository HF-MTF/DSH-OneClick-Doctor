using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace DshDoctor
{
    /// <summary>运行期环境：全部靠探测得出，不写死路径（通用工具）。可用 --root / --home / --node / --dsh 覆盖。</summary>
    static class Cfg
    {
        public static int Port = 3080;
        public static string Root = "";
        public static string Home = "";
        public static string NodeExe = "";
        public static string NodeDir = "";
        public static string NpmCmd = "";
        public static string DshBin = "";
        public static string SrcLib = "";
        public static string Note = "";          // 探测过程说明，写进报告

        public static string PkgRoot
        {
            get { return string.IsNullOrEmpty(Root) ? "" : Path.Combine(Root, @"dsh\node_modules\@deepseek-ai"); }
        }
        public static string ProfileDir
        {
            get { return string.IsNullOrEmpty(Home) ? "" : Path.Combine(Home, @"profiles\web"); }
        }
        public static string LogFile
        {
            get { return string.IsNullOrEmpty(Home) ? "" : Path.Combine(Home, @"logs\dsh-web.out.log"); }
        }
    }

    /// <summary>自动探测 DSH 安装位置、数据目录、Node 与主程序。</summary>
    static class Detector
    {
        static bool LooksLikeRoot(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                return Directory.Exists(Path.Combine(dir, "dsh"))
                    || Directory.Exists(Path.Combine(dir, "node"))
                    || Directory.Exists(Path.Combine(dir, "home"));
            }
            catch { return false; }
        }

        static bool HasProfile(string home)
        {
            try { return Directory.Exists(Path.Combine(Path.Combine(home, "profiles"), "web")); }
            catch { return false; }
        }

        static string Arg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static string DetectRoot(string forced, List<string> log)
        {
            if (!string.IsNullOrEmpty(forced))
            {
                if (Directory.Exists(forced)) { log.Add("命令行指定"); return forced; }
                log.Add("命令行指定的安装目录不存在：" + forced);
            }
            string self = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            if (LooksLikeRoot(self)) { log.Add("程序所在目录"); return self; }
            try
            {
                string up = Path.GetDirectoryName(self);
                if (LooksLikeRoot(up)) { log.Add("程序上级目录"); return up; }
            }
            catch { }
            foreach (string k in new string[] { "DSH_ROOT", "DSH_INSTALL", "DSH_DIR" })
            {
                string v = Environment.GetEnvironmentVariable(k);
                if (LooksLikeRoot(v)) { log.Add("环境变量 " + k); return v; }
            }
            string homeEnv = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrEmpty(homeEnv))
            {
                try
                {
                    string p = Path.GetDirectoryName(homeEnv.TrimEnd('\\'));
                    if (LooksLikeRoot(p)) { log.Add("由 DSH_HOME 反推"); return p; }
                }
                catch { }
            }
            List<string> cands = new List<string>();
            foreach (string drv in new string[] { "C", "D", "E", "F", "G" })
            {
                cands.Add(drv + ":\\DeepSeekHarness");
                cands.Add(drv + ":\\DSH");
                cands.Add(drv + ":\\Program Files\\DeepSeekHarness");
            }
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string upf = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            cands.Add(Path.Combine(la, "DeepSeekHarness"));
            cands.Add(Path.Combine(la, "Programs", "DeepSeekHarness"));
            cands.Add(Path.Combine(upf, "DeepSeekHarness"));
            foreach (string c in cands)
                if (LooksLikeRoot(c)) { log.Add("常见安装位置"); return c; }
            // 扫盘根目录，找名字里带 dsh / harness 且像安装目录的
            foreach (string drv in new string[] { "C", "D", "E", "F", "G" })
            {
                try
                {
                    foreach (string d in Directory.GetDirectories(drv + ":\\"))
                    {
                        string nm = Path.GetFileName(d).ToLowerInvariant();
                        if ((nm.Contains("dsh") || nm.Contains("harness")) && LooksLikeRoot(d))
                        { log.Add("扫描磁盘发现"); return d; }
                    }
                }
                catch { }
            }
            log.Add("未探测到");
            return null;
        }

        static string DetectHome(string root, List<string> log)
        {
            string h = Environment.GetEnvironmentVariable("DSH_HOME");
            if (!string.IsNullOrEmpty(h) && Directory.Exists(h) && HasProfile(h)) { log.Add("数据目录来自环境变量 DSH_HOME"); return h; }
            if (!string.IsNullOrEmpty(root))
            {
                string d = Path.Combine(root, "home");
                if (Directory.Exists(d)) { log.Add("安装目录内的 home"); return d; }
            }
            string upf = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string dot = Path.Combine(upf, ".dsh");
            if (Directory.Exists(dot) && HasProfile(dot)) { log.Add("用户目录下的 .dsh"); return dot; }
            if (!string.IsNullOrEmpty(h) && Directory.Exists(h)) { log.Add("环境变量 DSH_HOME（未找到 profile）"); return h; }
            if (!string.IsNullOrEmpty(root)) { log.Add("按默认约定 root\\home"); return Path.Combine(root, "home"); }
            log.Add("数据目录未探测到");
            return null;
        }

        static string DetectNode(string root, string forced, List<string> log)
        {
            if (!string.IsNullOrEmpty(forced) && File.Exists(forced)) { log.Add("命令行指定"); return forced; }
            if (!string.IsNullOrEmpty(root))
            {
                string n = Path.Combine(Path.Combine(root, "node"), "node.exe");
                if (File.Exists(n)) { log.Add("安装目录内 node\\node.exe"); return n; }
            }
            string path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
                foreach (string p in path.Split(';'))
                {
                    try
                    {
                        string f = Path.Combine(p.Trim(), "node.exe");
                        if (File.Exists(f)) { log.Add("PATH 中的 node"); return f; }
                    }
                    catch { }
                }
            foreach (string c in new string[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"nodejs\node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"nodejs\node.exe") })
                if (File.Exists(c)) { log.Add("默认安装位置 nodejs"); return c; }
            log.Add("未找到 node.exe");
            return null;
        }

        static string DetectDshBin(string root, string forced, List<string> log)
        {
            if (!string.IsNullOrEmpty(forced) && File.Exists(forced)) { log.Add("命令行指定"); return forced; }
            if (!string.IsNullOrEmpty(root))
            {
                string b = Path.Combine(root, @"dsh\node_modules\@deepseek-ai\dsh\lib\bin.js");
                if (File.Exists(b)) { log.Add("安装目录内 dsh\\node_modules"); return b; }
            }
            string appd = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string b2 = Path.Combine(appd, @"npm\node_modules\@deepseek-ai\dsh\lib\bin.js");
            if (File.Exists(b2)) { log.Add("npm 全局安装"); return b2; }
            string la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            try
            {
                string npx = Path.Combine(la, @"npm-cache\_npx");
                if (Directory.Exists(npx))
                    foreach (string d in Directory.GetDirectories(npx))
                    {
                        string b3 = Path.Combine(d, @"node_modules\@deepseek-ai\dsh\lib\bin.js");
                        if (File.Exists(b3)) { log.Add("npx 缓存"); return b3; }
                    }
            }
            catch { }
            log.Add("未找到 bin.js");
            return null;
        }

        /// <summary>界面上手动指定目录后重新探测。</summary>
        public static void DetectWithRoot(string root)
        {
            Detect(new string[] { "--root", root });
        }

        public static void Detect(string[] args)
        {
            List<string> log = new List<string>();
            Cfg.Root = DetectRoot(Arg(args, "--root"), log);
            Cfg.Home = DetectHome(Cfg.Root, log);
            Cfg.NodeExe = DetectNode(Cfg.Root, Arg(args, "--node"), log);
            Cfg.NodeDir = string.IsNullOrEmpty(Cfg.NodeExe) ? "" : Path.GetDirectoryName(Cfg.NodeExe);
            Cfg.NpmCmd = string.IsNullOrEmpty(Cfg.NodeDir) ? "" : Path.Combine(Cfg.NodeDir, "npm.cmd");
            Cfg.DshBin = DetectDshBin(Cfg.Root, Arg(args, "--dsh"), log);
            Cfg.SrcLib = string.IsNullOrEmpty(Cfg.Root) ? "" : Path.Combine(Cfg.Root, @"launcher-src\lib");
            string h = Arg(args, "--home");
            if (!string.IsNullOrEmpty(h) && Directory.Exists(h)) { Cfg.Home = h; log.Add("数据目录由命令行指定"); }
            Cfg.Note = string.Join("；", log.ToArray());
            if (string.IsNullOrEmpty(Cfg.Root)) Cfg.Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        }
    }

    class Item
    {
        public string Group;
        public string Name;
        public string Detail;
        public int Level;              // 0=通过 1=信息 2=警告 3=失败
        public string FixLabel;        // 非空表示可自动修复
        public Func<bool> Fix;
    }

    static class T
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        public static string Run(string exe, string args, int ms, string home)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                psi.StandardErrorEncoding = Encoding.UTF8;
                if (!string.IsNullOrEmpty(home)) psi.EnvironmentVariables["DSH_HOME"] = home;
                StringBuilder sb = new StringBuilder();
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(ms)) { try { p.Kill(); } catch { } return sb.ToString().Trim() + "\n(超时)"; }
                    return sb.ToString().Trim();
                }
            }
            catch (Exception ex) { return "(无法执行: " + ex.Message + ")"; }
        }

        public static string Reg(string path, string name)
        {
            try
            {
                object v = Registry.GetValue(path, name, null);
                return v == null ? null : v.ToString();
            }
            catch { return null; }
        }

        public static long DirSize(string dir)
        {
            try
            {
                long n = 0;
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { n += new FileInfo(f).Length; } catch { }
                }
                return n;
            }
            catch { return 0; }
        }

        public static string MB(long b) { return (b / 1048576.0).ToString("0.0") + " MB"; }

        public static bool IsWritable(string dir)
        {
            try
            {
                string f = Path.Combine(dir, ".dshdiag.tmp");
                File.WriteAllText(f, "x");
                File.Delete(f);
                return true;
            }
            catch { return false; }
        }

        public static int PidOnPort(int port)
        {
            try
            {
                IPGlobalProperties ip = IPGlobalProperties.GetIPGlobalProperties();
                foreach (IPEndPoint ep in ip.GetActiveTcpListeners())
                    if (ep.Port == port) return FindOwner(port);
            }
            catch { }
            return 0;
        }

        public static int FindOwner(int port)
        {
            string o = Run("netstat", "-ano", 8000, null);
            foreach (string line in o.Split('\n'))
            {
                string s = line.Trim();
                if (s.IndexOf("LISTENING", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string[] parts = s.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;
                if (!parts[1].EndsWith(":" + port)) continue;
                int pid;
                if (int.TryParse(parts[parts.Length - 1], out pid)) return pid;
            }
            return 0;
        }

        public static List<int> OurNodePids()
        {
            List<int> list = new List<int>();
            try
            {
                foreach (Process p in Process.GetProcessesByName("node"))
                {
                    try
                    {
                        string path = p.MainModule.FileName;
                        if (path != null && path.StartsWith(Cfg.Root, StringComparison.OrdinalIgnoreCase)) list.Add(p.Id);
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        public static string ProcPath(int pid)
        {
            try { return Process.GetProcessById(pid).MainModule.FileName; }
            catch { return null; }
        }
    }

    static class Probe
    {
        public static List<Item> All(Action<string> log)
        {
            List<Item> r = new List<Item>();
            Env(r, log);
            Install(r, log);
            Config(r, log);
            Runtime(r, log);
            return r;
        }

        static Item Add(List<Item> r, string g, string n, int lv, string d)
        {
            Item it = new Item();
            it.Group = g; it.Name = n; it.Level = lv; it.Detail = d;
            r.Add(it);
            return it;
        }

        // ---------- 运行环境 ----------
        static void Env(List<Item> r, Action<string> log)
        {
            log("检查运行环境…");
            string g = "运行环境";

            // 先报告探测到的位置（通用工具：路径全靠探测）
            if (!string.IsNullOrEmpty(Cfg.Root) && Directory.Exists(Cfg.Root))
            {
                string src = Cfg.Note;
                int cut = src.IndexOf('；');
                if (cut > 0) src = src.Substring(0, cut);
                Add(r, g, "安装位置", 0, Cfg.Root + "（" + src + "）   数据目录 " + (string.IsNullOrEmpty(Cfg.Home) ? "(未找到)" : Cfg.Home));
            }
            else
                Add(r, g, "安装位置", 3, "没有探测到 DSH 安装目录 —— 用 --root <路径> 指定，或在界面上点路径手动选择");

            if (!string.IsNullOrEmpty(Cfg.NodeExe) && File.Exists(Cfg.NodeExe))
            {
                string src2 = "";
                foreach (string seg in Cfg.Note.Split('；'))
                    if (seg.StartsWith("Node")) { src2 = seg; break; }
                Add(r, g, "Node 位置", 0, Cfg.NodeExe + (src2.Length > 0 ? "（" + src2 + "）" : ""));
            }
            else Add(r, g, "Node 位置", 3, "没有找到 node.exe（用 --node <路径> 指定）");

            // 操作系统
            string build = T.Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
            string disp = T.Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
            string prod = T.Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
            int b = 0;
            int.TryParse(build, out b);
            if (b >= 22000) Add(r, g, "操作系统", 0, (prod == null ? "Windows" : prod) + "  " + disp + " (build " + build + ")");
            else if (b >= 17763) Add(r, g, "操作系统", 1, (prod == null ? "Windows" : prod) + " build " + build + "：可用，但建议 Win11 以获得圆角/动画");
            else Add(r, g, "操作系统", 3, "Windows build " + build + "：版本过低，DSH 需要 Windows 10 1809 及以上");

            // 架构
            string arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
            if (arch == "AMD64") Add(r, g, "CPU 架构", 0, "x64（原生模块与预编译包齐全）");
            else if (arch == "ARM64") Add(r, g, "CPU 架构", 2, "ARM64：部分插件/原生模块可能没有预编译包，安装或运行可能失败");
            else Add(r, g, "CPU 架构", 2, arch + "：非主流架构，兼容性未知");

            // .NET Framework
            string rel = T.Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full", "Release");
            int relN = 0;
            int.TryParse(rel, out relN);
            if (relN >= 528040) Add(r, g, ".NET Framework", 0, "4.8 或更高（Release " + rel + "）");
            else if (relN >= 461808) Add(r, g, ".NET Framework", 2, "版本偏低（Release " + rel + "）：启动器/诊断工具需要 4.8");
            else Add(r, g, ".NET Framework", 3, "未检测到 .NET Framework 4.8（Release " + (rel == null ? "无" : rel) + "）");

            // Node
            if (!File.Exists(Cfg.NodeExe)) Add(r, g, "Node 运行时", 3, "找不到 " + Cfg.NodeExe);
            else
            {
                string v = T.Run(Cfg.NodeExe, "--version", 8000, null);
                Match m = Regex.Match(v, @"v(\d+)\.(\d+)");
                int major = 0;
                if (m.Success) int.TryParse(m.Groups[1].Value, out major);
                if (major >= 22) Add(r, g, "Node 运行时", 0, v + " （满足 DSH 的 TypeScript 剥离特性）");
                else if (major >= 20) Add(r, g, "Node 运行时", 2, v + "：版本偏低，PTC 运行时用到的 stripTypeScriptTypes 需要 Node 22+");
                else Add(r, g, "Node 运行时", 3, v + "：版本过低，DSH 需要 Node 20 以上（推荐 22+）");
            }

            // WebView2
            string wv = T.Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", "pv");
            if (wv == null) wv = T.Reg(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", "pv");
            if (wv != null) Add(r, g, "WebView2 运行时", 0, wv + "（启动器的独立页面窗口需要）");
            else Add(r, g, "WebView2 运行时", 2, "未安装：启动器的独立页面窗口会回退到系统浏览器");

            // PowerShell
            string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            string pwsh = null;
            string pf = Environment.GetEnvironmentVariable("ProgramFiles");
            if (pf != null && File.Exists(Path.Combine(pf, @"PowerShell\7\pwsh.exe"))) pwsh = Path.Combine(pf, @"PowerShell\7\pwsh.exe");
            if (File.Exists(ps) || pwsh != null)
                Add(r, g, "PowerShell", 0, (pwsh != null ? "PowerShell 7 + " : "") + "Windows PowerShell（DSH 的终端与 pwsh 工具需要）");
            else Add(r, g, "PowerShell", 2, "没找到 PowerShell，DSH 的 pwsh / bash 工具将不可用");

            // 内存
            ulong total = 0;
            try
            {
                MEMORYSTATUSEX ms = new MEMORYSTATUSEX();
                ms.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
                if (T.GlobalMemoryStatusEx(ref ms)) total = ms.ullTotalPhys;
            }
            catch { }
            double gb = total / 1073741824.0;
            if (gb >= 8) Add(r, g, "物理内存", 0, gb.ToString("0.0") + " GB");
            else if (gb >= 4) Add(r, g, "物理内存", 1, gb.ToString("0.0") + " GB：够用，长时间跑大项目可能吃紧");
            else Add(r, g, "物理内存", 2, gb.ToString("0.0") + " GB：偏小");

            // 磁盘
            try
            {
                DriveInfo d = new DriveInfo(Path.GetPathRoot(Cfg.Root));
                double free = d.AvailableFreeSpace / 1073741824.0;
                string fs = d.DriveFormat;
                if (free >= 5) Add(r, g, "磁盘空间", 0, d.Name + " 剩余 " + free.ToString("0.0") + " GB");
                else if (free >= 1) Add(r, g, "磁盘空间", 2, d.Name + " 剩余 " + free.ToString("0.0") + " GB：DSH 与插件更新可能需要更多空间");
                else Add(r, g, "磁盘空间", 3, d.Name + " 剩余不足 1 GB");

                if (fs == "NTFS") Add(r, g, "文件系统", 0, fs + "（支持硬链接/符号链接，npm 安装依赖必需）");
                else Add(r, g, "文件系统", 3, fs + "：非 NTFS，npm/pnpm 安装 node_modules 时可能失败");
            }
            catch (Exception ex) { Add(r, g, "磁盘空间", 2, "无法读取磁盘信息：" + ex.Message); }

            // 长路径
            string lp = T.Reg(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled");
            if (lp == "1") Add(r, g, "长路径支持", 0, "已启用（node_modules 深层路径不会踩 260 字符限制）");
            else
            {
                Item it = Add(r, g, "长路径支持", 2, "未启用：node_modules 深层路径可能触发 260 字符限制导致安装/加载失败");
                it.FixLabel = "尝试启用（需管理员）";
                it.Fix = delegate
                {
                    try
                    {
                        Registry.SetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1, RegistryValueKind.DWord);
                        return true;
                    }
                    catch { return false; }
                };
            }

            // 代码页
            string acp = T.Reg(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Nls\CodePage", "ACP");
            if (acp == "65001") Add(r, g, "系统编码", 0, "UTF-8 (65001)");
            else Add(r, g, "系统编码", 1, "ANSI 代码页 " + acp + "：中文路径一般没问题，个别插件日志可能乱码");

            // 代理
            string pen = T.Reg(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable");
            string psrv = T.Reg(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyServer");
            if (pen == "1") Add(r, g, "系统代理", 1, "已开启（" + psrv + "）：访问 API 走代理正常；本地回环若被拦，启动器的页面窗口已强制直连");
            else Add(r, g, "系统代理", 0, "未开启");
        }

        // ---------- 安装完整性 ----------
        static void Install(List<Item> r, Action<string> log)
        {
            log("检查 DSH 安装完整性…");
            string g = "安装完整性";

            if (File.Exists(Cfg.DshBin)) Add(r, g, "DSH 主程序", 0, Cfg.DshBin);
            else Add(r, g, "DSH 主程序", 3, "找不到 " + Cfg.DshBin + "（需要重新安装 DSH）");

            string[] need = new string[] { "dsh", "dsh-app-boot", "dsh-base", "cordis" };
            List<string> missing = new List<string>();
            foreach (string n in need)
                if (!Directory.Exists(Path.Combine(Cfg.PkgRoot, n))) missing.Add(n);
            if (missing.Count == 0) Add(r, g, "依赖完整性", 0, "核心包齐全（dsh / app-boot / base / cordis）");
            else
            {
                Item it = Add(r, g, "依赖完整性", 3, "缺少核心包：" + string.Join("、", missing.ToArray()));
                it.FixLabel = "按 package.json 重新补齐依赖（几分钟）";
                it.Fix = delegate { return NpmInstall(log); };
            }

            if (File.Exists(Cfg.DshBin))
            {
                string v = T.Run(Cfg.NodeExe, "\"" + Cfg.DshBin + "\" --version", 60000, Cfg.Home);
                if (Regex.IsMatch(v, @"^\d+\.\d+", RegexOptions.Multiline))
                    Add(r, g, "启动自检", 0, "dsh --version 正常返回：" + v.Split('\n')[0].Trim());
                else
                {
                    Item it = Add(r, g, "启动自检", 3, "dsh --version 输出异常：" + v.Replace("\n", " ").Substring(0, Math.Min(200, v.Length)));
                    it.FixLabel = "按 package.json 重新补齐依赖（几分钟）";
                    it.Fix = delegate { return NpmInstall(log); };
                }
            }

            if (T.IsWritable(Cfg.Root)) Add(r, g, "安装目录可写", 1, Cfg.Root + " 可写");
            else Add(r, g, "安装目录可写", 2, Cfg.Root + " 不可写：更新 DSH 或写日志会失败（可能需要管理员权限）");

            if (Directory.Exists(Cfg.Home))
            {
                if (T.IsWritable(Cfg.Home)) Add(r, g, "DSH_HOME 可写", 0, Cfg.Home);
                else Add(r, g, "DSH_HOME 可写", 3, Cfg.Home + " 不可写：会话、配置、日志都无法保存");
            }
            else Add(r, g, "DSH_HOME 可写", 3, "数据目录不存在：" + Cfg.Home);

            string env = Environment.GetEnvironmentVariable("DSH_HOME");
            if (string.IsNullOrEmpty(env)) Add(r, g, "DSH_HOME 环境变量", 1, "未设置（启动器会自己传入，命令行直跑 dsh 时会用默认 ~/.dsh）");
            else if (string.Equals(env.TrimEnd('\\'), Cfg.Home.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                Add(r, g, "DSH_HOME 环境变量", 0, env);
            else
            {
                Item it = Add(r, g, "DSH_HOME 环境变量", 2, "当前值 " + env + " 与启动器使用的 " + Cfg.Home + " 不一致，可能出现会话/配置读不到");
                it.FixLabel = "改为 " + Cfg.Home;
                it.Fix = delegate
                {
                    try { Environment.SetEnvironmentVariable("DSH_HOME", Cfg.Home, EnvironmentVariableTarget.User); return true; }
                    catch { return false; }
                };
            }

            // npm 脚本策略（npm 12 会拦 install script）
            string npmCmd = Path.Combine(Cfg.NodeDir, "npm.cmd");
            string npmRaw = T.Run("cmd.exe", "/c \"\"" + npmCmd + "\" --version\"", 30000, null);
            Match mm = Regex.Match(npmRaw, @"\d+\.\d+\.\d+");
            string npmv = mm.Success ? mm.Value : "未知";
            int nmajor = 0;
            if (mm.Success) int.TryParse(mm.Value.Split('.')[0], out nmajor);
            if (nmajor >= 12) Add(r, g, "npm 脚本策略", 1, "npm " + npmv + "：默认拦截未授权的 install script（WebView2 / 原生模块一般自带预编译包，通常无影响）");
            else Add(r, g, "npm 脚本策略", 0, "npm " + npmv);
        }

        // ---------- 配置与插件 ----------
        static void Config(List<Item> r, Action<string> log)
        {
            log("检查配置与插件…");
            string g = "配置与插件";
            string pkg = Path.Combine(Cfg.ProfileDir, "package.json");
            string patch = Path.Combine(Cfg.ProfileDir, "cordis.patch.yml");
            string compat = Path.Combine(Cfg.ProfileDir, "compatibility.json");

            if (!File.Exists(pkg)) { Add(r, g, "profile 清单", 3, "找不到 " + pkg); return; }
            string ptxt = File.ReadAllText(pkg);
            if (ptxt.TrimStart().StartsWith("{") && ptxt.TrimEnd().EndsWith("}"))
                Add(r, g, "profile 清单", 0, "package.json 结构正常");
            else
            {
                Item it = Add(r, g, "profile 清单", 3, "package.json 结构异常（不是合法 JSON 对象）");
                it.FixLabel = "从备份恢复";
                it.Fix = delegate { return RestoreFromBak(pkg); };
            }

            if (File.Exists(patch) && new FileInfo(patch).Length > 2) Add(r, g, "用户 patch", 0, "cordis.patch.yml 存在（" + new FileInfo(patch).Length + " 字节）");
            else
            {
                Item it = Add(r, g, "用户 patch", 3, "cordis.patch.yml 缺失或为空，插件配置会全部丢失");
                it.FixLabel = "从备份恢复";
                it.Fix = delegate { return RestoreFromBak(patch); };
            }

            if (File.Exists(compat))
            {
                string c = File.ReadAllText(compat).Trim();
                if (c.StartsWith("{") && c.EndsWith("}")) Add(r, g, "兼容性豁免", 0, "compatibility.json 正常");
                else
                {
                    Item it = Add(r, g, "兼容性豁免", 2, "compatibility.json 不是合法 JSON，豁免会全部失效");
                    it.FixLabel = "删除损坏文件";
                    it.Fix = delegate { try { File.Delete(compat); return true; } catch { return false; } };
                }
            }
            else Add(r, g, "兼容性豁免", 1, "没有 compatibility.json（有插件因版本不兼容被跳过时可在此精确豁免）");

            // bundle 兼容性（跑一次 dump-config）
            log("  正在用 dsh --dump-config 检查插件兼容性（几秒）…");
            string outTxt = T.Run(Cfg.NodeExe, "\"" + Cfg.DshBin + "\" --profile web --dump-config", 120000, Cfg.Home);
            List<string> skipped = new List<string>();
            foreach (string line in outTxt.Split('\n'))
            {
                Match m2 = Regex.Match(line, "skipping profile bundle \"([^\"]+)\"");
                if (m2.Success && !skipped.Contains(m2.Groups[1].Value)) skipped.Add(m2.Groups[1].Value);
            }
            if (skipped.Count == 0 && Regex.IsMatch(outTxt, @"^\d", RegexOptions.Multiline) == false && outTxt.IndexOf("skipping", StringComparison.OrdinalIgnoreCase) < 0)
                Add(r, g, "插件兼容性", 0, "所有 bundle 都能加载");
            else if (skipped.Count == 0)
                Add(r, g, "插件兼容性", 0, "没有 bundle 被跳过");
            else
            {
                Item it = Add(r, g, "插件兼容性", 2, "有 " + skipped.Count + " 个插件与当前 DSH 版本声明不兼容、已被跳过：" + string.Join("、", skipped.ToArray()) + " —— 豁免只解除版本限制、不改动 profile；若豁免后仍然异常，请手动从 package.json 的 bundles 里移除它");
                it.FixLabel = "为它们写版本豁免（不改 profile，可撤销）";
                it.Fix = delegate { return WriteExemptions(skipped); };
            }

            if (outTxt.IndexOf("Error", StringComparison.OrdinalIgnoreCase) >= 0 && skipped.Count == 0)
                Add(r, g, "配置解析", 2, "dump-config 输出里出现 Error 字样，建议查看完整日志");
            else Add(r, g, "配置解析", 0, "profile 与 patch 能被正确解析");

            // 启动器组件
            string[] dlls = new string[] { "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll" };
            List<string> miss = new List<string>();
            foreach (string d in dlls) if (!File.Exists(Path.Combine(Cfg.Root, d))) miss.Add(d);
            if (miss.Count == 0) Add(r, g, "启动器组件", 0, "WebView2 三个组件齐全");
            else
            {
                Item it = Add(r, g, "启动器组件", 2, "缺少：" + string.Join("、", miss.ToArray()));
                if (Directory.Exists(Cfg.SrcLib))
                {
                    it.FixLabel = "从 lib 目录补齐";
                    it.Fix = delegate
                    {
                        try
                        {
                            foreach (string d in miss) File.Copy(Path.Combine(Cfg.SrcLib, d), Path.Combine(Cfg.Root, d), true);
                            return true;
                        }
                        catch { return false; }
                    };
                }
            }
        }

        static bool NpmInstall(Action<string> log)
        {
            try
            {
                log("  执行 npm install --prefix dsh（可能需要几分钟）…");
                string args = "/c \"\"" + Cfg.NpmCmd + "\" install --prefix \"" + Path.Combine(Cfg.Root, "dsh") + "\" --no-fund --no-audit\"";
                string outp = T.Run("cmd.exe", args, 900000, null);
                bool ok = Directory.Exists(Path.Combine(Cfg.PkgRoot, "dsh"));
                string tail = outp.Length > 200 ? outp.Substring(outp.Length - 200) : outp;
                log("  npm 结束（" + (ok ? "包已就位" : "仍缺包") + "）：" + tail.Replace("\r", " ").Replace("\n", " "));
                return ok;
            }
            catch { return false; }
        }

        static bool RestoreFromBak(string file)
        {
            try
            {
                string dir = Path.GetDirectoryName(file);
                string name = Path.GetFileName(file);
                string best = null;
                DateTime bestT = DateTime.MinValue;
                foreach (string f in Directory.GetFiles(dir, name + "*.bak*"))
                {
                    DateTime t = File.GetLastWriteTime(f);
                    if (t > bestT) { bestT = t; best = f; }
                }
                if (best == null) return false;
                File.Copy(best, file, true);
                return true;
            }
            catch { return false; }
        }

        /// <summary>为被跳过的插件写精确版本豁免（只动 compatibility.json，不改 profile）。</summary>
        static bool WriteExemptions(List<string> names)
        {
            try
            {
                string compat = Path.Combine(Cfg.ProfileDir, "compatibility.json");
                Dictionary<string, string> map = new Dictionary<string, string>();
                if (File.Exists(compat))
                {
                    string old = File.ReadAllText(compat);
                    foreach (Match m in Regex.Matches(old, "\"([^\"]+)\"\\s*:\\s*\\[([^\\]]*)\\]"))
                        map[m.Groups[1].Value] = m.Groups[2].Value.Trim();
                }
                string dshVer = ReadPkgVersion(Path.Combine(Cfg.PkgRoot, "dsh"));
                if (string.IsNullOrEmpty(dshVer)) return false;
                int added = 0;
                foreach (string n in names)
                {
                    string ver = ReadPkgVersion(Path.Combine(Path.Combine(Cfg.ProfileDir, "node_modules"), n));
                    if (string.IsNullOrEmpty(ver)) continue;
                    string key = n + "@" + ver;
                    if (!map.ContainsKey(key)) { map[key] = "\"" + dshVer + "\""; added++; }
                }
                if (added == 0) return false;
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                bool first = true;
                foreach (KeyValuePair<string, string> kv in map)
                {
                    if (!first) sb.AppendLine(",");
                    sb.Append("  \"" + kv.Key + "\": [" + kv.Value + "]");
                    first = false;
                }
                sb.AppendLine();
                sb.AppendLine("}");
                File.WriteAllText(compat, sb.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        static string ReadPkgVersion(string dir)
        {
            try
            {
                string f = Path.Combine(dir, "package.json");
                if (!File.Exists(f)) return null;
                Match m = Regex.Match(File.ReadAllText(f), "\"version\"\\s*:\\s*\"([^\"]+)\"");
                return m.Success ? m.Groups[1].Value : null;
            }
            catch { return null; }
        }

        // ---------- 运行状态 ----------
        static void Runtime(List<Item> r, Action<string> log)
        {
            log("检查运行状态…");
            string g = "运行状态";

            int pid = T.PidOnPort(Cfg.Port);
            if (pid <= 0) Add(r, g, "服务端口 " + Cfg.Port, 1, "当前没有服务在监听（DSH 未运行，属正常）");
            else
            {
                string path = T.ProcPath(pid);
                if (path != null && path.StartsWith(Cfg.Root, StringComparison.OrdinalIgnoreCase))
                    Add(r, g, "服务端口 " + Cfg.Port, 0, "PID " + pid + " 正在监听（" + path + "）");
                else
                    Add(r, g, "服务端口 " + Cfg.Port, 3, "端口被别的程序占用：PID " + pid + "（" + (path == null ? "未知程序" : path) + "），DSH 会启动失败");
            }

            List<int> nodes = T.OurNodePids();
            if (nodes.Count == 0) Add(r, g, "node 进程", 1, "没有本安装目录下的 node 进程");
            else Add(r, g, "node 进程", 1, nodes.Count + " 个（含 DSH 服务与它拉起的 MCP 子进程，4~8 个属正常）");

            int dshPorts = 0;
            string ns = T.Run("netstat", "-ano", 8000, null);
            foreach (string line in ns.Split('\n'))
            {
                Match pm = Regex.Match(line, @":(\d{4})\s.*LISTENING");
                if (!pm.Success) continue;
                int pp = 0;
                int.TryParse(pm.Groups[1].Value, out pp);
                if (pp >= 3080 && pp <= 3090) dshPorts++;
            }
            if (dshPorts > 1) Add(r, g, "多实例检测", 2, "3080~3090 之间有 " + dshPorts + " 个端口在监听，可能同时跑着多个 DSH 实例");
            else if (dshPorts == 1) Add(r, g, "多实例检测", 0, "只有 1 个 DSH 服务在监听");
            else Add(r, g, "多实例检测", 1, "3080~3090 没有服务在监听（DSH 未运行）");

            string rt = Path.Combine(Cfg.Root, "runtime.json");
            if (File.Exists(rt))
            {
                string txt = File.ReadAllText(rt);
                Match m = Regex.Match(txt, @"""pid""\s*:\s*(\d+)");
                int rpid = 0;
                if (m.Success) int.TryParse(m.Groups[1].Value, out rpid);
                bool alive = false;
                try { Process.GetProcessById(rpid); alive = true; } catch { }
                if (rpid > 0 && alive) Add(r, g, "运行时记录", 1, "runtime.json 指向 PID " + rpid + "（存活）");
                else
                {
                    Item it = Add(r, g, "运行时记录", 2, "runtime.json 指向的 PID " + rpid + " 已经不存在（残留记录）");
                    it.FixLabel = "清理残留记录";
                    it.Fix = delegate { try { File.Delete(rt); return true; } catch { return false; } };
                }
            }
            else Add(r, g, "运行时记录", 1, "没有 runtime.json（正常，启动器不使用它）");

            // 日志分析
            if (!File.Exists(Cfg.LogFile)) Add(r, g, "启动日志", 1, "还没有日志文件：" + Cfg.LogFile);
            else
            {
                long sz = new FileInfo(Cfg.LogFile).Length;
                // DSH 正在写这个文件，必须共享读取
                List<string> tmpLines = new List<string>();
                using (FileStream lfs = new FileStream(Cfg.LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader lsr = new StreamReader(lfs, Encoding.UTF8))
                {
                    string ln;
                    while ((ln = lsr.ReadLine()) != null) tmpLines.Add(ln);
                }
                string[] lines = tmpLines.ToArray();
                int from = Math.Max(0, lines.Length - 300);
                StringBuilder tail = new StringBuilder();
                for (int i = from; i < lines.Length; i++) tail.AppendLine(lines[i]);
                string t = tail.ToString();

                List<string> hits = new List<string>();
                if (Regex.IsMatch(t, "EADDRINUSE")) hits.Add("端口被占用");
                if (Regex.IsMatch(t, "EPIPE|ERR_STREAM_DESTROYED|write EPIPE")) hits.Add("输出管道断开");
                if (Regex.IsMatch(t, "Cannot find module|MODULE_NOT_FOUND")) hits.Add("依赖缺失");
                if (Regex.IsMatch(t, "YAMLException|SyntaxError|Unexpected token")) hits.Add("配置语法错误");
                if (Regex.IsMatch(t, "401|Unauthorized")) hits.Add("鉴权失败（token/凭据）");
                if (Regex.IsMatch(t, "skipping profile bundle")) hits.Add("插件被跳过");
                if (Regex.IsMatch(t, "heap out of memory|JavaScript heap")) hits.Add("内存不足");

                if (hits.Count == 0) Add(r, g, "启动日志", 0, "日志 " + T.MB(sz) + "，最近 300 行没有发现已知错误特征");
                else Add(r, g, "启动日志", 2, "日志 " + T.MB(sz) + "，命中特征：" + string.Join("、", hits.ToArray()));

                if (sz > 20 * 1024 * 1024)
                {
                    Item it = Add(r, g, "日志体积", 2, "启动日志已 " + T.MB(sz) + "，过大（每次启动会覆盖写入，历史积累）");
                    it.FixLabel = "清空日志";
                    it.Fix = delegate
                    {
                        try
                        {
                            using (FileStream fs2 = new FileStream(Cfg.LogFile, FileMode.Truncate, FileAccess.Write, FileShare.ReadWrite)) { }
                            return true;
                        }
                        catch { return false; }
                    };
                }
            }

            // 缓存体积
            string wd = Path.Combine(Cfg.Home, "window-data");
            if (Directory.Exists(wd))
            {
                long s = T.DirSize(wd);
                if (s > 200L * 1024 * 1024)
                {
                    Item it = Add(r, g, "窗口缓存", 2, "页面窗口缓存已 " + T.MB(s) + "，可以清理");
                    it.FixLabel = "清理缓存";
                    it.Fix = delegate { try { Directory.Delete(wd, true); return true; } catch { return false; } };
                }
                else Add(r, g, "窗口缓存", 1, T.MB(s) + "（正常）");
            }

            string ncache = Path.Combine(Cfg.Root, "npm-cache");
            if (Directory.Exists(ncache))
            {
                long s = T.DirSize(ncache);
                if (s > 300L * 1024 * 1024)
                {
                    Item it = Add(r, g, "npm 缓存", 2, "npm 缓存已 " + T.MB(s) + "，可以清理（下次更新会重新下载）");
                    it.FixLabel = "清理缓存";
                    it.Fix = delegate
                    {
                        try
                        {
                            foreach (string d in Directory.GetDirectories(ncache)) Directory.Delete(d, true);
                            return true;
                        }
                        catch { return false; }
                    };
                }
                else Add(r, g, "npm 缓存", 1, T.MB(s));
            }
        }
    }

    class DiagResult
    {
        public List<Item> Items = new List<Item>();
        public int Ok, Info, Warn, Fail;
        public int Problems { get { return Warn + Fail; } }
    }

    static class Doctor
    {
        public static DiagResult Scan(Action<string> log)
        {
            DiagResult d = new DiagResult();
            d.Items = Probe.All(log);
            foreach (Item it in d.Items)
            {
                if (it.Level == 0) d.Ok++;
                else if (it.Level == 1) d.Info++;
                else if (it.Level == 2) d.Warn++;
                else d.Fail++;
            }
            return d;
        }

        /// <summary>执行所有可自动修复项，返回成功数量。</summary>
        public static int FixAll(DiagResult d, Action<string> log)
        {
            int n = 0;
            foreach (Item it in d.Items)
            {
                if (it.Fix == null) continue;
                log("修复：" + it.Name + " —— " + it.FixLabel);
                bool ok;
                try { ok = it.Fix(); } catch { ok = false; }
                log("      " + (ok ? "已修复" : "未能自动修复"));
                if (ok) n++;
            }
            return n;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    static class Pal
    {
        public static readonly Color Bg = Color.FromArgb(11, 16, 28);
        public static readonly Color Card = Color.FromArgb(20, 28, 44);
        public static readonly Color CardHi = Color.FromArgb(28, 39, 60);
        public static readonly Color Edge = Color.FromArgb(42, 56, 80);
        public static readonly Color Text = Color.FromArgb(230, 238, 248);
        public static readonly Color Dim = Color.FromArgb(138, 156, 184);
        public static readonly Color Green = Color.FromArgb(74, 222, 128);
        public static readonly Color Yellow = Color.FromArgb(250, 204, 21);
        public static readonly Color Red = Color.FromArgb(248, 113, 113);
        public static readonly Color Blue = Color.FromArgb(96, 165, 250);
        public static readonly Color Gray = Color.FromArgb(120, 136, 160);

        public static System.Drawing.Drawing2D.GraphicsPath Round(Rectangle r, int rad)
        {
            System.Drawing.Drawing2D.GraphicsPath p = new System.Drawing.Drawing2D.GraphicsPath();
            int d = rad * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Of(int level)
        {
            if (level == 0) return Green;
            if (level == 1) return Gray;
            if (level == 2) return Yellow;
            return Red;
        }

        public static string Label(int level)
        {
            if (level == 0) return "通过";
            if (level == 1) return "信息";
            if (level == 2) return "警告";
            return "错误";
        }
    }

    class FlatBtn : Control
    {
        public bool Primary;
        private bool _hover, _down;

        public FlatBtn(string text, bool primary)
        {
            Text = text;
            Primary = primary;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9.5f);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg, edge, fg;
            if (Primary)
            {
                bg = _down ? Color.FromArgb(37, 99, 235) : (_hover ? Color.FromArgb(96, 165, 250) : Color.FromArgb(59, 130, 246));
                edge = Color.FromArgb(110, 160, 250);
                fg = Color.White;
            }
            else
            {
                bg = _down ? Color.FromArgb(22, 31, 48) : (_hover ? Color.FromArgb(35, 48, 70) : Color.FromArgb(26, 36, 54));
                edge = _hover ? Color.FromArgb(72, 94, 132) : Pal.Edge;
                fg = Pal.Text;
            }
            using (System.Drawing.Drawing2D.GraphicsPath p = Pal.Round(r, 9))
            using (SolidBrush b = new SolidBrush(bg)) g.FillPath(b, p);
            using (System.Drawing.Drawing2D.GraphicsPath p = Pal.Round(r, 9))
            using (Pen pen = new Pen(edge)) g.DrawPath(pen, p);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>自绘检查结果列表：分组标题 + 彩色状态行，支持滚轮与悬停。</summary>
    class CheckList : Panel
    {
        private class Row
        {
            public bool IsGroup;
            public string GroupText;
            public Item It;
            public int Y, H;
        }

        private List<Row> _rows = new List<Row>();
        private int _scroll, _hover = -1, _sel = -1;
        private Font _fGroup, _fTag;

        public event EventHandler SelectionChanged;

        public CheckList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Pal.Card;
            Font = new Font("Microsoft YaHei UI", 10f);
            BackColor = Color.FromArgb(13, 19, 32);
            _fGroup = new Font(Font, FontStyle.Bold);
            _fTag = new Font(Font.FontFamily, 8.5f);
        }

        public Item Selected
        {
            get { return (_sel >= 0 && _sel < _rows.Count) ? _rows[_sel].It : null; }
        }

        public void SetItems(List<Item> items)
        {
            _rows.Clear();
            _scroll = 0; _hover = -1; _sel = -1;
            string last = null;
            int y = 6;
            foreach (Item it in items)
            {
                if (it.Group != last)
                {
                    Row g = new Row();
                    g.IsGroup = true; g.GroupText = it.Group; g.Y = y; g.H = 38;
                    _rows.Add(g);
                    y += 38;
                    last = it.Group;
                }
                Row r = new Row();
                r.It = it; r.Y = y; r.H = 42;
                _rows.Add(r);
                y += 34;
            }
            Invalidate();
        }

        private int TotalHeight()
        {
            int h = 6;
            foreach (Row r in _rows) h += r.H;
            return h + 34;   // 底部留白，滚到底时最后一行不会被切
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int max = Math.Max(0, TotalHeight() - Height);
            _scroll -= e.Delta / 3;
            if (_scroll < 0) _scroll = 0;
            if (_scroll > max) _scroll = max;
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = HitTest(e.Y);
            if (idx != _hover) { _hover = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = -1; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int idx = HitTest(e.Y);
            if (idx >= 0 && !_rows[idx].IsGroup)
            {
                _sel = idx;
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        private int HitTest(int y)
        {
            int cy = y + _scroll;
            for (int i = 0; i < _rows.Count; i++)
                if (cy >= _rows[i].Y && cy < _rows[i].Y + _rows[i].H) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            for (int i = 0; i < _rows.Count; i++)
            {
                Row r = _rows[i];
                int y = r.Y - _scroll;
                if (y + r.H < 0 || y > Height) continue;

                if (r.IsGroup)
                {
                    TextRenderer.DrawText(g, r.GroupText, _fGroup,
                        new Rectangle(18, y + 6, Width - 36, r.H - 8), Pal.Blue,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    continue;
                }

                // 每个检查项 = 一张小卡片
                Rectangle bg = new Rectangle(12, y + 3, Width - 24, r.H - 6);
                Color fill = (i == _sel) ? Color.FromArgb(40, 56, 84)
                    : (i == _hover ? Color.FromArgb(31, 44, 68) : Color.FromArgb(23, 32, 50));
                using (System.Drawing.Drawing2D.GraphicsPath p = Pal.Round(bg, 9))
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                if (i == _sel)
                    using (System.Drawing.Drawing2D.GraphicsPath p = Pal.Round(bg, 9))
                    using (Pen pen = new Pen(Color.FromArgb(120, 90, 140, 220))) g.DrawPath(pen, p);

                Color c = Pal.Of(r.It.Level);
                int cy = y + r.H / 2;
                // 状态点：外圈光晕 + 实心
                using (SolidBrush b = new SolidBrush(Color.FromArgb(55, c.R, c.G, c.B)))
                    g.FillEllipse(b, bg.X + 14, cy - 7, 14, 14);
                using (SolidBrush b = new SolidBrush(c))
                    g.FillEllipse(b, bg.X + 18, cy - 3, 6, 6);

                // 状态胶囊
                string tag = Pal.Label(r.It.Level);
                Rectangle tagRect = new Rectangle(bg.X + 38, cy - 11, 46, 22);
                using (System.Drawing.Drawing2D.GraphicsPath p = Pal.Round(tagRect, 7))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(48, c.R, c.G, c.B))) g.FillPath(b, p);
                TextRenderer.DrawText(g, tag, _fTag, tagRect, c,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

                TextRenderer.DrawText(g, r.It.Name, Font, new Rectangle(bg.X + 96, y, 186, r.H), Pal.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

                TextRenderer.DrawText(g, r.It.Detail, Font, new Rectangle(bg.X + 290, y, bg.Width - 306, r.H),
                    Color.FromArgb(158, 175, 200),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }

            // 右侧滚动条（更明显）
            int total = TotalHeight();
            if (total > Height)
            {
                int barH = Math.Max(36, Height * Height / total);
                int barY = (Height - barH) * _scroll / Math.Max(1, total - Height);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(46, 62, 90)))
                    g.FillRectangle(b, Width - 8, 0, 8, Height);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(110, 140, 180)))
                    g.FillRectangle(b, Width - 8, barY, 6, barH);
            }

            // 底部渐隐：提示下面还有内容
            if (_scroll < Math.Max(0, total - Height))
            {
                Rectangle fade = new Rectangle(0, Height - 34, Width, 34);
                using (System.Drawing.Drawing2D.LinearGradientBrush lb = new System.Drawing.Drawing2D.LinearGradientBrush(
                    fade, Color.FromArgb(0, BackColor), BackColor, 90f))
                    g.FillRectangle(lb, fade);
            }
        }
    }

    class MainForm : Form
    {
        private const int TITLE_H = 38;
        private const int BORDER = 8;

        private List<Item> _items = new List<Item>();
        private DiagResult _last;
        private bool _busy;

        private CheckList _list;
        private TextBox _log;
        private FlatBtn _bDiag, _bFix, _bReport, _bOpen, _bRoot;
        private int _hoverBtn = -1;
        private Font _fTitle, _fBig, _fSmall, _fNum, _fTagSmall, _fCount;
        private Image _ico;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private bool _autoFix;

        public MainForm(bool autoFix)
        {
            _autoFix = autoFix;
            Text = "DSH 一键诊断";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1040, 830);
            MinimumSize = new Size(900, 680);
            BackColor = Pal.Bg;
            ForeColor = Pal.Text;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); _ico = Icon.ToBitmap(); } catch { }
            _fTitle = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            _fBig = new Font("Microsoft YaHei UI", 16f, FontStyle.Bold);
            _fSmall = new Font("Microsoft YaHei UI", 9f);
            _fTagSmall = new Font("Microsoft YaHei UI", 10f);
            _fCount = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
            _fNum = new Font("Microsoft YaHei UI", 20f, FontStyle.Bold);

            _list = new CheckList();
            Controls.Add(_list);

            _log = new TextBox();
            _log.Multiline = true;
            _log.ReadOnly = true;
            _log.ScrollBars = ScrollBars.Vertical;
            _log.BackColor = Color.FromArgb(8, 12, 22);
            _log.ForeColor = Color.FromArgb(140, 200, 170);
            _log.BorderStyle = BorderStyle.None;
            _log.TabStop = false;          // 只读日志不该抢焦点（否则会一直闪输入光标）
            _log.ShortcutsEnabled = true;
            _log.Font = new Font("Consolas", 9f);
            Controls.Add(_log);

            _bDiag = new FlatBtn("开始诊断", false);
            _bDiag.Click += delegate { RunDiag(); };
            _bFix = new FlatBtn("一键修复", true);
            _bFix.Click += delegate { RunFix(); };
            _bRoot = new FlatBtn("更改目录", false);
            _bRoot.Click += delegate
            {
                FolderBrowserDialog d = new FolderBrowserDialog();
                d.Description = "选择 DSH 安装目录（包含 dsh / node / home 的那一层）";
                try { d.SelectedPath = Cfg.Root; } catch { }
                if (d.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(d.SelectedPath))
                {
                    Detector.DetectWithRoot(d.SelectedPath);
                    Say("已切换到安装目录：" + Cfg.Root);
                    Say("探测说明：" + Cfg.Note);
                    RunDiag();
                }
            };
            _bReport = new FlatBtn("导出报告", false);
            _bReport.Click += delegate { ExportReport(); };
            _bOpen = new FlatBtn("打开 DSH 目录", false);
            _bOpen.Click += delegate { try { Process.Start(Cfg.Root); } catch { } };
            Controls.Add(_bDiag); Controls.Add(_bFix); Controls.Add(_bRoot);
            Controls.Add(_bReport); Controls.Add(_bOpen);

            LayoutAll();
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCCALCSIZE = 0x83;
            const int WM_NCHITTEST = 0x84;
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                // 客户区 = 整个窗口矩形 → 系统标题栏不再绘制（外观全交给自绘）
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result != 1) return;
                int sx = unchecked((short)(long)m.LParam);
                int sy = unchecked((short)(((long)m.LParam) >> 16));
                Point p = PointToClient(new Point(sx, sy));
                if (WindowState == FormWindowState.Normal)
                {
                    int grab = 6;
                    bool left = p.X <= grab, right = p.X >= ClientSize.Width - grab;
                    bool top = p.Y <= grab, bottom = p.Y >= ClientSize.Height - grab;
                    if (top && left) { m.Result = (IntPtr)13; return; }
                    if (top && right) { m.Result = (IntPtr)14; return; }
                    if (bottom && left) { m.Result = (IntPtr)16; return; }
                    if (bottom && right) { m.Result = (IntPtr)17; return; }
                    if (left) { m.Result = (IntPtr)10; return; }
                    if (right) { m.Result = (IntPtr)11; return; }
                    if (top) { m.Result = (IntPtr)12; return; }
                    if (bottom) { m.Result = (IntPtr)15; return; }
                }
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int allow = 0; DwmSetWindowAttribute(Handle, 3, ref allow, sizeof(int)); } catch { }
            try { int pref = 2; DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int)); } catch { }
        }

        private void LayoutAll()
        {
            // 构造函数里设置 ClientSize 时 OnResize 就会触发，此时子控件还没建好
            if (_list == null || _log == null || _bDiag == null || _bFix == null ||
                _bRoot == null || _bReport == null || _bOpen == null) return;

            int w = ClientSize.Width, h = ClientSize.Height;
            int x = BORDER + 12, inner = w - (BORDER + 12) * 2;
            int listTop = TITLE_H + 100;
            int btnH = 34;

            int btnTop = h - BORDER - 16 - btnH;      // 按钮行贴着窗口底部（下方留 16px）
            int logH = 132;
            int logTop = btnTop - 34 - logH;          // 日志底与按钮之间留 34px
            int listH = Math.Max(120, logTop - 30 - listTop);

            _list.SetBounds(x, listTop, inner, listH);
            _log.SetBounds(x + 20, logTop, inner - 44, logH);

            int by = btnTop;
            _bDiag.SetBounds(x, by, 108, btnH);
            _bFix.SetBounds(x + 118, by, 108, btnH);
            _bReport.SetBounds(x + 236, by, 108, btnH);
            _bOpen.SetBounds(x + 354, by, 120, btnH);
            _bRoot.SetBounds(x + 484, by, 108, btnH);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutAll();
            Invalidate();
        }

        private Rectangle BtnRect(int i)
        {
            return new Rectangle(ClientSize.Width - 46 * (i + 1), 0, 46, TITLE_H);
        }

        private int HitButton(Point p)
        {
            for (int i = 0; i < 3; i++) if (BtnRect(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            int w = ClientSize.Width, h = ClientSize.Height;

            using (SolidBrush b = new SolidBrush(Pal.Bg)) g.FillRectangle(b, 0, 0, w, h);

            // 标题栏
            Rectangle tr = new Rectangle(0, 0, w, TITLE_H);
            using (System.Drawing.Drawing2D.LinearGradientBrush br = new System.Drawing.Drawing2D.LinearGradientBrush(
                tr, Color.FromArgb(20, 30, 50), Color.FromArgb(13, 19, 32), 90f))
                g.FillRectangle(br, tr);
            int iy = (TITLE_H - 18) / 2;
            if (_ico != null) g.DrawImage(_ico, 14, iy, 18, 18);
            TextRenderer.DrawText(g, "DSH 一键诊断", _fTitle,
                new Rectangle(40, 0, 300, TITLE_H), Color.FromArgb(222, 232, 246),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            for (int i = 0; i < 3; i++) DrawCaptionButton(g, i);

            // 状态卡
            Rectangle card = new Rectangle(BORDER + 12, TITLE_H + 12, w - (BORDER + 12) * 2, 68);
            using (System.Drawing.Drawing2D.GraphicsPath p = Pal.Round(card, 12))
            using (SolidBrush b = new SolidBrush(Pal.Card)) g.FillPath(b, p);
            using (System.Drawing.Drawing2D.GraphicsPath p = Pal.Round(card, 12))
            using (Pen pen = new Pen(Pal.Edge)) g.DrawPath(pen, p);

            string big = "尚未诊断";
            Color bigC = Pal.Dim;
            if (_busy) { big = "正在检查…"; bigC = Pal.Blue; }
            else if (_last != null)
            {
                if (_last.Fail > 0) { big = "发现 " + _last.Fail + " 个错误"; bigC = Pal.Red; }
                else if (_last.Warn > 0) { big = "发现 " + _last.Warn + " 个警告"; bigC = Pal.Yellow; }
                else { big = "全部通过"; bigC = Pal.Green; }
            }
            TextRenderer.DrawText(g, big, _fBig,
                new Rectangle(card.X + 22, card.Y, 470, card.Height), bigC,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            int[] nums = new int[] { _last == null ? 0 : _last.Ok, _last == null ? 0 : _last.Warn, _last == null ? 0 : _last.Fail };
            string[] caps = new string[] { "通过", "警告", "错误" };
            Color[] cols = new Color[] { Pal.Green, Pal.Yellow, Pal.Red };
            int bw = 108, bh = 48, gap = 12;
            int totalW = bw * 3 + gap * 2;
            int bx0 = card.Right - totalW - 18;
            int by0 = card.Y + (card.Height - bh) / 2;
            for (int i = 0; i < 3; i++)
            {
                Rectangle box = new Rectangle(bx0 + i * (bw + gap), by0, bw, bh);
                Color acc = nums[i] == 0 ? Color.FromArgb(116, 132, 158) : cols[i];
                using (System.Drawing.Drawing2D.GraphicsPath p = Pal.Round(box, 10))
                using (SolidBrush b = new SolidBrush(Color.FromArgb(24, 34, 52))) g.FillPath(b, p);
                using (System.Drawing.Drawing2D.GraphicsPath p = Pal.Round(box, 10))
                using (Pen pen = new Pen(Pal.Edge)) g.DrawPath(pen, p);

                // 横排：数字 + 标签，先量再画，保证不重叠
                string num = nums[i].ToString();
                string cap = caps[i];
                Size ns = TextRenderer.MeasureText(g, num, _fCount, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                Size cs2 = TextRenderer.MeasureText(g, cap, _fTagSmall, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                int contentW = ns.Width + 7 + cs2.Width;
                int sx = box.X + (box.Width - contentW) / 2;
                int midY = box.Y + box.Height / 2;
                TextRenderer.DrawText(g, num, _fCount,
                    new Rectangle(sx, midY - ns.Height / 2, ns.Width + 2, ns.Height), acc,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, cap, _fTagSmall,
                    new Rectangle(sx + ns.Width + 7, midY - cs2.Height / 2, cs2.Width + 2, cs2.Height),
                    Color.FromArgb(160, 178, 204),
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }

            // 按钮行右侧的说明
            if (_bOpen != null)
            {
                int hintW = 360;
                TextRenderer.DrawText(g, _busy ? "正在扫描…" : "「一键修复」= 诊断 → 修复 → 自动复检",
                    _fSmall, new Rectangle(ClientSize.Width - BORDER - 14 - hintW, _bOpen.Top + 9, hintW, 20),
                    Color.FromArgb(126, 144, 170),
                    TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            }

            // 外框
            using (System.Drawing.Drawing2D.GraphicsPath p = Pal.Round(new Rectangle(0, 0, w - 1, h - 1), 13))
            using (System.Drawing.Drawing2D.LinearGradientBrush lb = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, w, h), Color.FromArgb(120, 160, 214), Color.FromArgb(40, 58, 88), 42f))
            using (Pen pen = new Pen(lb, 1))
                g.DrawPath(pen, p);
        }

        private void DrawCaptionButton(Graphics g, int i)
        {
            Rectangle r = BtnRect(i);
            if (_hoverBtn == i)
            {
                Color c = (i == 0) ? Color.FromArgb(220, 38, 38) : Color.FromArgb(35, 48, 68);
                using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, r);
            }
            Color stroke = (_hoverBtn == i && i == 0) ? Color.White : Color.FromArgb(186, 202, 224);
            using (Pen pen = new Pen(stroke, 1))
            {
                int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, sz = 5;
                if (i == 0)
                {
                    g.DrawLine(pen, cx - sz, cy - sz, cx + sz, cy + sz);
                    g.DrawLine(pen, cx - sz, cy + sz, cx + sz, cy - sz);
                }
                else if (i == 1) g.DrawRectangle(pen, cx - sz, cy - sz, sz * 2, sz * 2);
                else g.DrawLine(pen, cx - sz, cy, cx + sz, cy);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int h = HitButton(e.Location);
            if (h != _hoverBtn) { _hoverBtn = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverBtn = -1; Invalidate(); base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int b = HitButton(e.Location);
            if (b == 0) { Close(); return; }
            if (b == 2) { WindowState = FormWindowState.Minimized; return; }
            if (b == 1)
            {
                WindowState = (WindowState == FormWindowState.Maximized) ? FormWindowState.Normal : FormWindowState.Maximized;
                return;
            }
            if (e.Y < TITLE_H) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)0x2, IntPtr.Zero); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Y < TITLE_H && HitButton(e.Location) < 0)
                WindowState = (WindowState == FormWindowState.Maximized) ? FormWindowState.Normal : FormWindowState.Maximized;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
            t.Interval = 400;
            t.Tick += delegate { t.Stop(); t.Dispose(); if (_autoFix) RunFix(); else RunDiag(); };
            t.Start();
        }

        private void Say(string s)
        {
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        private void RunDiag()
        {
            if (_busy) return;
            _busy = true;
            Invalidate();
            _log.Clear();
            Say("开始诊断…");
            Application.DoEvents();
            _last = Doctor.Scan(delegate(string s) { Say(s); Application.DoEvents(); });
            _items = _last.Items;
            _list.SetItems(_items);
            _busy = false;
            Invalidate();
            Say(_last.Fail > 0 ? "诊断完成：发现 " + _last.Fail + " 个错误、" + _last.Warn + " 个警告"
                : (_last.Warn > 0 ? "诊断完成：没有错误，" + _last.Warn + " 个警告（多数不影响使用）" : "诊断完成：全部通过"));
        }

        private void RunFix()
        {
            if (_busy) return;
            _busy = true;
            Invalidate();
            _log.Clear();
            Say("诊断 → 修复 → 复检 …");
            Application.DoEvents();
            DiagResult before = Doctor.Scan(delegate(string s) { Say(s); Application.DoEvents(); });
            int n = Doctor.FixAll(before, delegate(string s) { Say(s); Application.DoEvents(); });
            Say("复检中…");
            Application.DoEvents();
            DiagResult after = Doctor.Scan(delegate(string s) { Application.DoEvents(); });
            _last = after;
            _items = after.Items;
            _list.SetItems(_items);
            _busy = false;
            Invalidate();
            Say(n == 0
                ? "没有可自动修复的项目；剩余问题 " + after.Problems + " 个需要人工处理"
                : "已自动修复 " + n + " 项：问题 " + before.Problems + " → " + after.Problems + "（剩余错误 " + after.Fail + "，警告 " + after.Warn + "）");
        }

        private void ShowDetail()
        {
            Item it = _list.Selected;
            if (it == null) { Say("先在列表里点一行。"); return; }
            MessageBox.Show(it.Name + Environment.NewLine + Environment.NewLine + it.Detail +
                (string.IsNullOrEmpty(it.FixLabel) ? "" : Environment.NewLine + Environment.NewLine + "可自动修复：" + it.FixLabel),
                "诊断详情", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ExportReport()
        {
            if (_items.Count == 0) { Say("请先做一次诊断。"); return; }
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    "DSH诊断报告-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("DSH 一键诊断报告   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("计算机：" + Environment.MachineName + "   用户：" + Environment.UserName);
                sb.AppendLine("安装目录：" + Cfg.Root);
                sb.AppendLine("结果：错误 " + (_last == null ? 0 : _last.Fail) + " 个，警告 " + (_last == null ? 0 : _last.Warn) + " 个");
                sb.AppendLine("========================================");
                string lastG = "";
                foreach (Item it in _items)
                {
                    if (it.Group != lastG) { sb.AppendLine(); sb.AppendLine("【" + it.Group + "】"); lastG = it.Group; }
                    sb.AppendLine("[" + Pal.Label(it.Level) + "] " + it.Name + " : " + it.Detail);
                }
                sb.AppendLine();
                sb.AppendLine("========================================");
                sb.AppendLine("过程日志：");
                sb.AppendLine(_log.Text);
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Say("报告已导出：" + path);
                try { Process.Start("notepad.exe", "\"" + path + "\""); } catch { }
            }
            catch (Exception ex) { Say("导出失败：" + ex.Message); }
        }
    }

    static class Program
    {
        static int AutoRun(bool fix)
        {
            List<string> lines = new List<string>();
            Action<string> log = delegate(string s2) { lines.Add(s2); };
            DiagResult before = Doctor.Scan(log);
            int fixedCount = 0;
            DiagResult after = before;
            if (fix)
            {
                fixedCount = Doctor.FixAll(before, log);
                log("复检中…");
                after = Doctor.Scan(log);
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DSH 一键诊断报告   " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("计算机：" + Environment.MachineName + "   用户：" + Environment.UserName);
            sb.AppendLine("安装目录：" + Cfg.Root);
            if (fix) sb.AppendLine("结果：自动修复 " + fixedCount + " 项，问题 " + before.Problems + " → " + after.Problems + "（错误 " + after.Fail + "，警告 " + after.Warn + "）");
            else sb.AppendLine("结果：错误 " + after.Fail + " 个，警告 " + after.Warn + " 个");
            sb.AppendLine("========================================");
            string last = "";
            foreach (Item it in after.Items)
            {
                if (it.Group != last) { sb.AppendLine(); sb.AppendLine("【" + it.Group + "】"); last = it.Group; }
                sb.AppendLine("[" + Pal.Label(it.Level) + "] " + it.Name + " : " + it.Detail);
            }
            sb.AppendLine();
            sb.AppendLine("========================================");
            sb.AppendLine("过程日志：");
            foreach (string l in lines) sb.AppendLine(l);
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DSH诊断报告.txt"), sb.ToString(), new UTF8Encoding(false)); }
            catch { }
            return after.Fail > 0 ? 2 : (after.Warn > 0 ? 1 : 0);
        }

        [STAThread]
        static void Main(string[] args)
        {
            Detector.Detect(args);
            if (args.Length > 0 && args[0] == "--auto")
            {
                bool fix = false;
                for (int i = 1; i < args.Length; i++) if (args[i] == "--fix") fix = true;
                Environment.ExitCode = AutoRun(fix);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool autoFix = false;
            for (int i = 0; i < args.Length; i++) if (args[i] == "--autofix") autoFix = true;
            Application.Run(new MainForm(autoFix));
        }
    }
}
