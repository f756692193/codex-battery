using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using CodexQuotaManager;

class CoreTests {
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
    static object Window(double used, int mins, long reset) { return new { usedPercent = used, windowDurationMins = mins, resetsAt = reset }; }
    static object Map(object obj) { return Json.Read(Json.Write(obj)); }
    static object Credit(string id, object expires) { return new { id = id, status = "available", expiresAt = expires }; }
    static Snapshot Parse(object obj, DateTimeOffset now) { return Snapshot.Parse(Map(obj), "test@example.invalid", "pro", now); }
    static int Fake() {
        string folder = Environment.GetEnvironmentVariable("QUOTA_TEST_OUTPUT");
        if (!string.IsNullOrEmpty(folder)) {
            var child = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, "child") { UseShellExecute = false, CreateNoWindow = true });
            File.WriteAllText(Path.Combine(folder, "fake-child.pid"), child.Id.ToString());
        }
        int reads = 0; string scenario = Environment.GetEnvironmentVariable("QUOTA_TEST_SCENARIO");
        // Framework Process.StandardInput follows the parent console encoding and
        // can emit a UTF-8 BOM on CI. Read the RPC stream as UTF-8, independently
        // of the fake process's console code page, and consume its optional BOM.
        using (var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false, true), true))
        using (var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true }) {
            string line;
            while ((line = input.ReadLine()) != null) {
                var m = Json.Read(line); var id = Json.Get(m, "id"); if (id == null) continue;
                string method = Json.Str(m, "method"); object result = new { };
                if (method == "account/read") { reads++; result = new { account = scenario == "no-login" ? null : new { type = "chatgpt", email = scenario == "switch" && reads > 1 ? "other@example.invalid" : "test@example.invalid", planType = "pro" } }; }
                if (method == "account/rateLimits/read") result = new { rateLimits = new { primary = Window(25, 300, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()) } };
                if (method == "account/rateLimits/read" && scenario == "hang") Thread.Sleep(60000);
                output.WriteLine(Json.Write(new { id = id, result = result }));
            }
        }
        return 0;
    }
    static bool Alive(int id) { try { using (var p = Process.GetProcessById(id)) return !p.HasExited; } catch { return false; } }
    public static int Main(string[] args) {
        if (args.Length > 0 && args[0] == "app-server") return Fake();
        if (args.Length > 0 && args[0] == "child") { Thread.Sleep(120000); return 0; }
        try {
            var now = DateTimeOffset.UtcNow; long future = now.AddHours(2).ToUnixTimeSeconds(), past = now.AddSeconds(-1).ToUnixTimeSeconds();
            var s = Parse(new { rateLimits = new { primary = Window(25, 10080, future), secondary = Window(80, 300, future) } }, now);
            Check(s.Windows.Count == 2 && s.Windows[0].Minutes == 300, "sort real duration, not primary slot");
            Check(s.Select("auto", now).Remaining == 20, "lowest remaining in Codex bucket");
            Check(s.Windows[1].DurationLabel == "7 天周期", "seven-day primary label");
            Check(s.Select("codex:primary", now).Remaining == 75, "explicit period selection");
            Check(s.Select("missing", now) == null, "missing explicit selection stays unknown");
            s = Parse(new { rateLimits = new { primary = Window(100, 300, past) } }, now);
            Check(s.Select("auto", now) == null, "expired quota never becomes full");
            Check(s.Windows[0].Countdown(now).Contains("等待"), "expired countdown waits for server");
            s = Parse(new { rateLimits = new { primary = Window(12, 300, future) }, rateLimitsByLimitId = new Dictionary<string, object> {
                { "codex", new { primary = Window(10, 10080, future) } }, { "model_other", new { primary = Window(99, 300, future) } } } }, now);
            Check(s.Windows.Count == 2, "multi-bucket view doesn't duplicate legacy");
            Check(s.Select("auto", now).Remaining == 90, "unrelated bucket cannot reduce Codex icon");
            Check(s.Select("model_other:primary", now).Remaining == 1, "other bucket can be explicitly selected");
            s = Parse(new { rateLimits = new { primary = new { usedPercent = 0, windowDurationMins = 300 } } }, now);
            Check(s.Windows.Count == 0, "missing reset is unknown");
            s = Parse(new { rateLimits = new { primary = new { windowDurationMins = 300, resetsAt = future } } }, now);
            Check(s.Windows.Count == 0, "missing usage is unknown");
            s = Parse(new { rateLimits = new { primary = Window(120, 300, future), secondary = Window(-10, 10080, future) } }, now);
            Check(s.Windows[0].Remaining == 0 && s.Windows[1].Remaining == 100, "clamp valid numeric percentages");
            Check(Clock.InZone(now, false).Offset == TimeSpan.FromHours(8), "Beijing timezone independent of machine");
            Check(!Clock.Mask("user@example.invalid").Contains("user@"), "mask account label");
            s = Parse(new { rateLimits = new { primary = Window(33.8, 90, future) } }, now);
            Check(Math.Abs(s.Windows[0].Remaining - 66.2) < .0001 && s.Windows[0].DurationLabel == "90 分钟周期", "fractional usage and non-hour duration");
            s = Parse(new { rateLimits = new { primary = Window(25, 300, future) }, rateLimitsByLimitId = new Dictionary<string, object>() }, now);
            Check(s.Windows.Count == 1, "empty multi-bucket falls back to legacy");
            s = Parse(new { rateLimitResetCredits = new { availableCount = 2, credits = new[] { new { id = "detail-1" } } } }, now);
            Check(s.ResetCards == 2, "reset card count uses authoritative total, not detail rows");
            s = Parse(new { rateLimitResetCredits = new { availableCount = 0 } }, now);
            Check(s.ResetCards == 0, "explicit zero reset cards remains zero");
            Check(Parse(new { }, now).ResetCards == null, "missing reset card count stays unknown");
            Check(Parse(new { rateLimitResetCredits = new { availableCount = -1 } }, now).ResetCards == null, "negative reset card count stays unknown");
            Check(Parse(new { rateLimitResetCredits = new { availableCount = 1.5 } }, now).ResetCards == null, "fractional reset card count stays unknown");
            s = Parse(new { rateLimitResetCredits = new { availableCount = 2, credits = new[] { Credit("later", future + 86400), Credit("first", future) } } }, now);
            Check(s.ResetCardExpiryKnown && s.ResetCardExpiresAt.Value.ToUnixTimeSeconds() == future, "earliest reset-card expiry is selected independent of row order");
            Check(s.ResetCardExpiryText(now, false).Contains(Clock.InZone(DateTimeOffset.FromUnixTimeSeconds(future), false).ToString("MM-dd HH:mm")), "reset-card expiry uses Beijing time by default");
            Check(s.ResetCardExpiryText(now, true).Contains(DateTimeOffset.FromUnixTimeSeconds(future).ToLocalTime().ToString("MM-dd HH:mm")), "reset-card expiry follows local-time setting");
            s = Parse(new { rateLimitResetCredits = new { availableCount = 2, credits = new[] { Credit("no-expiry", null), Credit("dated", future) } } }, now);
            Check(s.ResetCardExpiryKnown && s.ResetCardExpiresAt.Value.ToUnixTimeSeconds() == future, "explicit non-expiring card does not hide a dated card");
            s = Parse(new { rateLimitResetCredits = new { availableCount = 1, credits = new[] { Credit("no-expiry", null) } } }, now);
            Check(s.ResetCardExpiryKnown && !s.ResetCardExpiresAt.HasValue && s.ResetCardExpiryText(now, false).Contains("无到期限制"), "explicit null expiry is distinct from missing details");
            s = Parse(new { rateLimitResetCredits = new { availableCount = 2, credits = new[] { Credit("partial", future) } } }, now);
            Check(!s.ResetCardExpiryKnown && !s.ResetCardExpiresAt.HasValue && s.ResetCardExpiryText(now, false).Contains("待确认") && s.ResetCards == 2, "truncated details cannot claim an account-wide earliest expiry");
            s = Parse(new { rateLimitResetCredits = new { availableCount = 2 } }, now);
            Check(!s.ResetCardExpiryKnown && s.ResetCardExpiryText(now, false).Contains("待确认"), "count-only response keeps expiry unknown");
            s = Parse(new { rateLimitResetCredits = new { availableCount = 2, credits = new[] { Credit("duplicate", future), Credit("duplicate", future) } } }, now);
            Check(!s.ResetCardExpiryKnown, "duplicate card IDs cannot satisfy detail completeness");
            s = Parse(new { rateLimitResetCredits = new { availableCount = 1, credits = new[] { new { id = "missing-expiry", status = "available" } } } }, now);
            Check(!s.ResetCardExpiryKnown, "missing expiresAt is not treated as a non-expiring card");
            foreach (var invalidExpiry in new object[] { 1.5, 1e99, "1800000000", true }) {
                s = Parse(new { rateLimitResetCredits = new { availableCount = 1, credits = new[] { Credit("invalid", invalidExpiry) } } }, now);
                Check(!s.ResetCardExpiryKnown && !s.ResetCardExpiresAt.HasValue, "invalid reset-card expiry stays unknown: " + invalidExpiry);
            }
            s = Parse(new { rateLimitResetCredits = new { availableCount = 2, credits = new[] { Credit("expired", past), Credit("future", future) } } }, now);
            Check(s.ResetCardExpiryText(now, false).Contains("已到期") && s.ResetCards == 2 && s.ResetCardExpiresAt.Value.ToUnixTimeSeconds() == past, "expiry waits for server confirmation without decrementing count or skipping the old card");
            Check(Parse(new { rateLimitResetCredits = new { availableCount = 0 } }, now).ResetCardExpiryText(now, false).Contains("暂无可用"), "zero cards do not imply an unknown expiry");
            var countdownNow = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds());
            s = Parse(new { rateLimitResetCredits = new { availableCount = 1, credits = new[] { Credit("countdown", countdownNow.AddDays(2).AddHours(11).ToUnixTimeSeconds()) } } }, countdownNow);
            Check(s.ResetCardExpiryText(countdownNow, false).Contains("剩2天11小时"), "reset-card expiry includes a compact countdown");
            var folder = Path.GetFullPath(args.Length > 0 ? args[0] : "."); Directory.CreateDirectory(folder);
            Environment.SetEnvironmentVariable("QUOTA_TEST_OUTPUT", folder);
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            var client = new QuotaClient(); var got = client.Fetch(exe, CancellationToken.None).GetAwaiter().GetResult();
            Check(got.Windows.Count == 1 && got.Windows[0].Remaining == 75, "RPC initialize/account/limits round trip");
            int child = int.Parse(File.ReadAllText(Path.Combine(folder, "fake-child.pid")));
            Check(!Alive(client.LastProcessId) && !Alive(child), "job closes helper and descendants");
            var originalInputEncoding = Console.InputEncoding;
            try {
                // Reproduce the UTF-8 preamble emitted by the Framework writer
                // under the console configuration used by hosted Windows CI.
                Console.InputEncoding = Encoding.UTF8;
                got = client.Fetch(exe, CancellationToken.None).GetAwaiter().GetResult();
                Check(got.Account == "test@example.invalid" && got.Windows.Count == 1 && got.Windows[0].Remaining == 75, "RPC round trip accepts UTF-8 console input with a BOM");
                child = int.Parse(File.ReadAllText(Path.Combine(folder, "fake-child.pid")));
                Check(!Alive(client.LastProcessId) && !Alive(child), "UTF-8-console query cleans helper and descendants");
            } finally { Console.InputEncoding = originalInputEncoding; }
            Environment.SetEnvironmentVariable("QUOTA_TEST_SCENARIO", "switch"); bool switched = false;
            try { client.Fetch(exe, CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidOperationException) { switched = true; }
            Check(switched, "account change rejects mixed snapshot");
            Check(!Alive(client.LastProcessId), "failed query also cleans helper");
            Environment.SetEnvironmentVariable("QUOTA_TEST_SCENARIO", "no-login"); bool missing = false;
            try { client.Fetch(exe, CancellationToken.None).GetAwaiter().GetResult(); } catch (InvalidOperationException) { missing = true; }
            Check(missing, "no login doesn't become zero quota");
            Check(!Alive(client.LastProcessId), "no-login helper cleaned");
            Environment.SetEnvironmentVariable("QUOTA_TEST_SCENARIO", "hang"); bool canceled = false;
            using (var cancel = new CancellationTokenSource()) {
                cancel.CancelAfter(300);
                try { client.Fetch(exe, cancel.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { canceled = true; }
            }
            Check(canceled, "in-flight query can be canceled");
            child = int.Parse(File.ReadAllText(Path.Combine(folder, "fake-child.pid")));
            Check(!Alive(client.LastProcessId) && !Alive(child), "cancel cleans helper and descendants");
            Environment.SetEnvironmentVariable("QUOTA_TEST_SCENARIO", null); Environment.SetEnvironmentVariable("QUOTA_TEST_OUTPUT", null);
            Console.WriteLine("All " + checks + " checks passed."); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
