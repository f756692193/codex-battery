using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexQuotaManager {
    public static class BuildInfo {
        public static string Version { get { return typeof(BuildInfo).Assembly.GetName().Version.ToString(3); } }
    }
    public static class Json {
        public static Dictionary<string, object> Obj(object x) { return x as Dictionary<string, object>; }
        public static object Get(object x, string k) { var d = Obj(x); object v; return d != null && d.TryGetValue(k, out v) ? v : null; }
        public static string Str(object x, string k) { var v = Get(x, k); return v == null ? "" : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public static double? Number(object x, string k) {
            var v = Get(x, k); double n;
            return v != null && double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && !double.IsNaN(n) && !double.IsInfinity(n) ? (double?)n : null;
        }
        public static string Write(object x) { return new JavaScriptSerializer().Serialize(x); }
        public static Dictionary<string, object> Read(string s) { return new JavaScriptSerializer { MaxJsonLength = 4000000 }.Deserialize<Dictionary<string, object>>(s); }
    }
    public class QuotaWindow {
        public string BucketId, BucketName, Slot;
        public double Used;
        public int Minutes;
        public DateTimeOffset Reset;
        public double Remaining { get { return Math.Max(0, Math.Min(100, 100 - Used)); } }
        public string Key { get { return BucketId + ":" + Slot; } }
        public string DurationLabel {
            get {
                if (Minutes % 1440 == 0) return (Minutes / 1440).ToString() + " 天周期";
                if (Minutes % 60 == 0) return (Minutes / 60).ToString() + " 小时周期";
                return Minutes.ToString() + " 分钟周期";
            }
        }
        public bool Expired(DateTimeOffset now) { return Reset <= now; }
        public string Countdown(DateTimeOffset now) {
            if (Expired(now)) return "到期 · 等待服务端确认";
            var t = Reset - now;
            if (t.TotalDays >= 1) return string.Format("{0}天{1}小时", (int)t.TotalDays, t.Hours);
            if (t.TotalHours >= 1) return string.Format("{0}小时{1}分", (int)t.TotalHours, t.Minutes);
            return string.Format("{0}分{1}秒", t.Minutes, t.Seconds);
        }
    }
    public class Snapshot {
        public string Account, Plan;
        public int? ResetCards;
        public bool ResetCardExpiryKnown;
        public DateTimeOffset? ResetCardExpiresAt;
        public DateTimeOffset FetchedAt;
        public List<QuotaWindow> Windows = new List<QuotaWindow>();
        public static Snapshot Parse(object limits, string account, string plan, DateTimeOffset now) {
            var s = new Snapshot { Account = account, Plan = plan, FetchedAt = now };
            var count = Json.Number(Json.Get(limits, "rateLimitResetCredits"), "availableCount");
            if (count.HasValue && count.Value >= 0 && count.Value <= int.MaxValue && count.Value == Math.Floor(count.Value)) s.ResetCards = (int)count.Value;
            s.ReadResetCardExpiry(Json.Get(limits, "rateLimitResetCredits"));
            var buckets = Json.Obj(Json.Get(limits, "rateLimitsByLimitId"));
            if (buckets != null && buckets.Count > 0) {
                foreach (var b in buckets) s.AddBucket(b.Value, b.Key);
            } else {
                var legacy = Json.Get(limits, "rateLimits");
                if (legacy != null) s.AddBucket(legacy, "codex");
            }
            s.Windows = s.Windows.OrderBy(w => w.BucketId == "codex" ? 0 : 1).ThenBy(w => w.BucketId).ThenBy(w => w.Minutes).ToList();
            return s;
        }
        void ReadResetCardExpiry(object credits) {
            if (!ResetCards.HasValue || ResetCards.Value <= 0) return;
            var rawRows = Json.Get(credits, "credits"); var rows = rawRows as IEnumerable;
            if (rows == null || rawRows is string || Json.Obj(rawRows) != null) return;
            var ids = new HashSet<string>(); DateTimeOffset? earliest = null;
            foreach (var row in rows) {
                if (Json.Str(row, "status") != "available") continue;
                string id = Json.Str(row, "id"); var data = Json.Obj(row);
                if (id.Length == 0 || !ids.Add(id) || data == null || !data.ContainsKey("expiresAt")) return;
                var rawExpiry = Json.Get(row, "expiresAt");
                // The local App Server schema defines an explicit null as no expiry.
                if (rawExpiry == null) continue;
                var expiry = Json.Number(row, "expiresAt");
                if (rawExpiry is string || !expiry.HasValue || expiry.Value != Math.Floor(expiry.Value) || expiry.Value < -62135596800d || expiry.Value > 253402300799d) return;
                var date = DateTimeOffset.FromUnixTimeSeconds((long)expiry.Value);
                if (!earliest.HasValue || date < earliest.Value) earliest = date;
            }
            // Detail rows can be capped. Do not call a partial list's minimum the account's earliest expiry.
            if (ids.Count != ResetCards.Value) return;
            ResetCardExpiryKnown = true; ResetCardExpiresAt = earliest;
        }
        public string ResetCardExpiryText(DateTimeOffset now, bool local) {
            if (ResetCards == 0) return "暂无可用重置卡";
            if (!ResetCards.HasValue || !ResetCardExpiryKnown) return "到期时间待确认";
            if (!ResetCardExpiresAt.HasValue) return "到期：无到期限制";
            var expiry = ResetCardExpiresAt.Value; var date = Clock.InZone(expiry, local);
            string format = date.Year == Clock.InZone(now, local).Year ? "MM-dd HH:mm" : "yyyy-MM-dd HH:mm";
            string prefix = "最早到期：" + date.ToString(format) + " · ";
            if (expiry <= now) return prefix + "已到期，待确认";
            var span = expiry - now;
            if (span.TotalDays >= 1) return prefix + string.Format("剩{0}天{1}小时", (int)span.TotalDays, span.Hours);
            if (span.TotalHours >= 1) return prefix + string.Format("剩{0}小时{1}分", (int)span.TotalHours, span.Minutes);
            return prefix + string.Format("剩{0}分{1}秒", span.Minutes, span.Seconds);
        }
        void AddBucket(object b, string fallback) {
            string id = Json.Str(b, "limitId"); if (id.Length == 0) id = fallback;
            string name = Json.Str(b, "limitName"); if (name.Length == 0) name = id == "codex" ? "Codex" : id;
            foreach (var slot in new[] { "primary", "secondary" }) {
                var w = Json.Get(b, slot); var used = Json.Number(w, "usedPercent"); var mins = Json.Number(w, "windowDurationMins"); var reset = Json.Number(w, "resetsAt");
                if (!used.HasValue || !mins.HasValue || !reset.HasValue || mins <= 0 || mins > int.MaxValue) continue;
                try {
                    Windows.Add(new QuotaWindow { BucketId = id, BucketName = name, Slot = slot, Used = used.Value,
                        Minutes = (int)mins.Value, Reset = DateTimeOffset.FromUnixTimeSeconds((long)reset.Value) });
                } catch (ArgumentOutOfRangeException) { }
            }
        }
        public QuotaWindow Select(string key, DateTimeOffset now) {
            var valid = Windows.Where(w => !w.Expired(now)).ToList();
            if (key != "auto") return valid.FirstOrDefault(w => w.Key == key);
            // Never compare unrelated model buckets. Prefer the general Codex bucket.
            string bucket = Windows.Any(w => w.BucketId == "codex") ? "codex" : Windows.Select(w => w.BucketId).FirstOrDefault();
            return valid.Where(w => w.BucketId == bucket).OrderBy(w => w.Remaining).ThenBy(w => w.Minutes).FirstOrDefault();
        }
    }
    public class Settings {
        public int RefreshSeconds = 60;
        public int LowThreshold = 20;
        public bool LowNotify = true;
        public bool Floating = true;
        public bool LocalTime = false;
        public bool NumberIcon = false;
        public bool TiboNews = true;
        public string TiboFeedUrl = "";
        public string SelectedWindow = "auto";
        public string CodexPath = "";
        public int FloatX = -1, FloatY = -1;
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaManager");
        public static readonly string SettingsPath = Path.Combine(DataDir, "settings.json");
        public static Settings Load() {
            try {
                var s = new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(SettingsPath, Encoding.UTF8));
                s.RefreshSeconds = Math.Max(30, Math.Min(3600, s.RefreshSeconds));
                s.LowThreshold = Math.Max(1, Math.Min(99, s.LowThreshold));
                if (string.IsNullOrEmpty(s.SelectedWindow)) s.SelectedWindow = "auto";
                return s;
            } catch { return new Settings(); }
        }
        public void Save() { WriteLocal("settings.json", Json.Write(this)); }
        public static void WriteLocal(string file, string contents) {
            Directory.CreateDirectory(DataDir);
            string dest = Path.Combine(DataDir, file), temp = dest + ".tmp";
            File.WriteAllText(temp, contents, new UTF8Encoding(false));
            if (File.Exists(dest)) File.Replace(temp, dest, null); else File.Move(temp, dest);
        }
    }
    public static class Locator {
        public static string Find(string manual) {
            if (!string.IsNullOrWhiteSpace(manual)) {
                if (!File.Exists(manual) || !manual.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("设置中的 Codex EXE 路径无效，请重新选择。");
                return Path.GetFullPath(manual);
            }
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string bin = Path.Combine(local, "OpenAI", "Codex", "bin");
            if (Directory.Exists(bin)) {
                foreach (var d in new DirectoryInfo(bin).GetDirectories().OrderByDescending(x => x.LastWriteTimeUtc)) {
                    string p = Path.Combine(d.FullName, "codex.exe"); if (File.Exists(p)) return p;
                }
            }
            string app = Path.Combine(local, "Programs", "Codex", "resources", "codex.exe"); if (File.Exists(app)) return app;
            string npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@openai", "codex", "node_modules", "@openai");
            if (Directory.Exists(npm)) {
                try { var found = Directory.GetFiles(npm, "codex.exe", SearchOption.AllDirectories).FirstOrDefault(); if (found != null) return found; } catch { }
            }
            foreach (string raw in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')) {
                try { string p = Path.Combine(raw.Trim('"'), "codex.exe"); if (File.Exists(p) && p.IndexOf("WindowsApps", StringComparison.OrdinalIgnoreCase) < 0) return p; } catch { }
            }
            throw new FileNotFoundException("未找到本机 Codex。请在设置中选择 codex.exe；无需填写密码或 API Key。");
        }
    }
    // All newly launched helpers belong to a job. Closing it kills only our process tree.
    public sealed class ProcessJob : IDisposable {
        IntPtr handle;
        [StructLayout(LayoutKind.Sequential)] struct Basic { public long PerProcess, PerJob; public uint Flags; public UIntPtr Min, Max; public uint Active; public UIntPtr Affinity; public uint Priority, Scheduling; }
        [StructLayout(LayoutKind.Sequential)] struct Io { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [StructLayout(LayoutKind.Sequential)] struct Extended { public Basic Basic; public Io Io; public UIntPtr ProcessMemory, JobMemory, PeakProcess, PeakJob; }
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int cls, ref Extended data, uint length);
        [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        public ProcessJob(Process p) {
            handle = CreateJobObject(IntPtr.Zero, null);
            var info = new Extended(); info.Basic.Flags = 0x2000;
            if (handle == IntPtr.Zero || !SetInformationJobObject(handle, 9, ref info, (uint)Marshal.SizeOf(info)) || !AssignProcessToJobObject(handle, p.Handle)) {
                try { p.Kill(); } catch { }
                Dispose(); throw new InvalidOperationException("无法为额度查询进程建立退出清理机制。");
            }
        }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    }
    public sealed class Rpc : IDisposable {
        readonly Process process;
        readonly ProcessJob job;
        readonly object gate = new object();
        readonly Dictionary<int, TaskCompletionSource<object>> pending = new Dictionary<int, TaskCompletionSource<object>>();
        int next; bool disposed;
        public int ProcessId { get { return process.Id; } }
        public Rpc(string exe) {
            process = new Process { StartInfo = new ProcessStartInfo(exe, "app-server --listen stdio://") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(exe)
            }};
            process.StartInfo.EnvironmentVariables.Remove("CODEX_INTERNAL_ORIGINATOR_OVERRIDE");
            process.Start();
            try { job = new ProcessJob(process); } catch { process.Dispose(); throw; }
            process.OutputDataReceived += OnLine;
            // Drain stderr without recording potentially sensitive server diagnostics.
            process.ErrorDataReceived += delegate { };
            process.BeginOutputReadLine(); process.BeginErrorReadLine();
        }
        void OnLine(object sender, DataReceivedEventArgs e) {
            if (e.Data == null) { FailPending(new IOException("Codex 查询进程已结束。")); return; }
            try {
                var message = Json.Read(e.Data); var idValue = Json.Get(message, "id"); if (idValue == null) return;
                int id; if (!int.TryParse(Convert.ToString(idValue), out id)) return;
                TaskCompletionSource<object> t;
                lock (gate) { if (!pending.TryGetValue(id, out t)) return; pending.Remove(id); }
                if (Json.Get(message, "error") != null) t.TrySetException(new InvalidOperationException("额度查询失败（RPC " + Json.Str(Json.Get(message, "error"), "code") + "）。请确认 Codex 已登录、网络可用。"));
                else t.TrySetResult(Json.Get(message, "result"));
            } catch { }
        }
        void FailPending(Exception error) {
            lock (gate) { foreach (var t in pending.Values) t.TrySetException(error); pending.Clear(); }
        }
        public async Task<object> Call(string method, object parameters, CancellationToken cancel) {
            var t = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously); int id;
            lock (gate) {
                if (disposed) throw new ObjectDisposedException("Rpc");
                id = ++next; pending.Add(id, t);
                process.StandardInput.WriteLine(Json.Write(new { id = id, method = method, @params = parameters }));
                process.StandardInput.Flush();
            }
            try {
                var delay = Task.Delay(TimeSpan.FromSeconds(30), cancel);
                if (await Task.WhenAny(t.Task, delay).ConfigureAwait(false) != t.Task) {
                    cancel.ThrowIfCancellationRequested(); throw new TimeoutException("额度查询超时，请检查网络后重试。");
                }
                return await t.Task.ConfigureAwait(false);
            } finally { lock (gate) { pending.Remove(id); } }
        }
        public void Initialized() { lock (gate) { process.StandardInput.WriteLine("{\"method\":\"initialized\",\"params\":{}}"); process.StandardInput.Flush(); } }
        public void Dispose() {
            lock (gate) { if (disposed) return; disposed = true; }
            job.Dispose();
            try { process.WaitForExit(1500); } catch { }
            FailPending(new OperationCanceledException()); process.Dispose();
        }
    }
    public class QuotaClient {
        readonly object gate = new object(); Rpc active;
        public int LastProcessId;
        public void Stop() { lock (gate) { if (active != null) { active.Dispose(); active = null; } } }
        public async Task<Snapshot> Fetch(string exe, CancellationToken cancel) {
            // A fresh helper each refresh follows the current on-disk login and avoids stale account sessions.
            using (var rpc = new Rpc(exe)) {
                lock (gate) { active = rpc; LastProcessId = rpc.ProcessId; }
                try {
                    await rpc.Call("initialize", new { clientInfo = new { name = "codex_quota_manager", title = "Codex Battery", version = BuildInfo.Version } }, cancel).ConfigureAwait(false);
                    rpc.Initialized();
                    var accountResult = await rpc.Call("account/read", new { refreshToken = false }, cancel).ConfigureAwait(false);
                    var account = Json.Get(accountResult, "account");
                    if (account == null) throw new InvalidOperationException("未读取到本机登录状态，请先在 Codex 中登录，再点击刷新。");
                    if (Json.Str(account, "type") != "chatgpt") throw new InvalidOperationException("当前不是 ChatGPT 账号登录，无法查询订阅额度。请在 Codex 中切换账号。");
                    var limits = await rpc.Call("account/rateLimits/read", null, cancel).ConfigureAwait(false);
                    var after = Json.Get(await rpc.Call("account/read", new { refreshToken = false }, cancel).ConfigureAwait(false), "account");
                    if (after == null || Json.Str(account, "email") != Json.Str(after, "email")) throw new InvalidOperationException("检测到账号发生变化，请重新刷新。");
                    return Snapshot.Parse(limits, Json.Str(account, "email"), Json.Str(after, "planType"), DateTimeOffset.UtcNow);
                } finally { lock (gate) { if (active == rpc) active = null; } }
            }
        }
    }
    public static class Clock {
        public static DateTimeOffset InZone(DateTimeOffset t, bool local) { return local ? t.ToLocalTime() : t.ToOffset(TimeSpan.FromHours(8)); }
        public static string Zone(bool local) { return local ? "本地时间" : "北京时间 UTC+8"; }
        public static string Mask(string email) { int i = email.IndexOf('@'); return i > 0 ? email.Substring(0, 1) + "***" + email.Substring(i) : "本机已登录账号"; }
    }
}
