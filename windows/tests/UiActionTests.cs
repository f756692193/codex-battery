using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using CodexQuotaManager;

class UiActionTests {
    static int checks;
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, PrivateInstance).GetValue(target); }
    static void Invoke(object target, string name) { target.GetType().GetMethod(name, PrivateInstance).Invoke(target, null); }
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
    static LinkLabel Link(HoverDetailsForm details, string text) { return Field<Panel>(details, "footer").Controls.OfType<LinkLabel>().Single(l => l.Text == text); }
    static void Click(LinkLabel link) { typeof(LinkLabel).GetMethod("OnLinkClicked", PrivateInstance).Invoke(link, new object[] { new LinkLabelLinkClickedEventArgs(link.Links[0]) }); }
    static void ShowHover(FloatingForm floating) { floating.Show(); typeof(FloatingForm).GetMethod("EvaluateHover", PrivateInstance).Invoke(floating, new object[] { true }); }
    static void SettingsCheck(MainForm main, FloatingForm floating, Action open, Form expectedOwner, bool taskbar, string screenshot) {
        bool seen = false, nativeVisible = false, ownerCorrect = false, taskbarCorrect = false, mainStayedHidden = false;
        bool wasHidden = !main.Visible;
        using (var timer = new Timer { Interval = 100 }) {
            int ticks = 0;
            timer.Tick += delegate {
                var dialog = Application.OpenForms.OfType<SettingsForm>().FirstOrDefault();
                if (dialog == null || !dialog.Visible) { if (++ticks > 30) { timer.Stop(); Application.ExitThread(); } return; }
                timer.Stop(); seen = true; nativeVisible = IsWindowVisible(dialog.Handle);
                ownerCorrect = dialog.Owner == expectedOwner && (dialog.Owner == null || dialog.Owner.Visible);
                taskbarCorrect = dialog.ShowInTaskbar == taskbar; mainStayedHidden = !wasHidden || !main.Visible;
                using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(screenshot); }
                dialog.DialogResult = DialogResult.Cancel; dialog.Close();
            };
            timer.Start(); open();
        }
        Check(seen && nativeVisible, "settings window is shown without opening main first");
        Check(ownerCorrect, "settings owner is visible or absent");
        Check(taskbarCorrect && mainStayedHidden, "settings taskbar entry and hidden main state are correct");
    }
    [STAThread] public static int Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        try {
            using (var main = new MainForm(false, true)) {
                var handle = main.Handle; main.Hide();
                var floating = Field<FloatingForm>(main, "floating"); var details = Field<HoverDetailsForm>(floating, "details");
                var snapshot = new Snapshot { Account = "demo@example.invalid", Plan = "pro", ResetCards = 2, ResetCardExpiryKnown = true, ResetCardExpiresAt = DateTimeOffset.UtcNow.AddDays(2), FetchedAt = DateTimeOffset.UtcNow };
                var window = new QuotaWindow { BucketId = "codex", BucketName = "Codex", Slot = "primary", Minutes = 10080, Used = 38, Reset = DateTimeOffset.UtcNow.AddDays(3) }; snapshot.Windows.Add(window);
                floating.UpdateQuota(window, new HoverQuotaState { Snapshot = snapshot, Fresh = true }); ShowHover(floating);
                Check(Field<HoverQuotaView>(details, "content").AccessibleName.Contains("最早到期："), "hover includes the earliest reset-card expiry below the count");
                floating.UpdateQuota(null, new HoverQuotaState { Snapshot = snapshot, Fresh = false });
                Check(Field<HoverQuotaView>(details, "content").AccessibleName.Contains("到期时间待确认") && !Field<HoverQuotaView>(details, "content").AccessibleName.Contains("最早到期："), "stale account data cannot expose an old expiry as current");
                floating.UpdateQuota(window, new HoverQuotaState { Snapshot = snapshot, Fresh = true });
                Click(Link(details, "刷新"));
                Check(details.Visible, "refresh click keeps hover panel open");
                floating.UpdateQuota(window, new HoverQuotaState { Snapshot = snapshot, Fresh = true, Busy = true });
                Check(details.Visible, "panel remains visible while quota is loading");
                var refresh = Field<Panel>(details, "footer").Controls.OfType<LinkLabel>().First(l => l.Text.StartsWith("刷新"));
                Check(refresh.Text.Contains("中") && !refresh.Enabled, "refresh shows busy state and blocks duplicate clicks");
                window.Used = 45; floating.UpdateQuota(window, new HoverQuotaState { Snapshot = snapshot, Fresh = true });
                Check(details.Visible && Field<HoverQuotaView>(details, "content").AccessibleName.Contains("55%"), "quota result updates in the same visible panel");
                Check(refresh.Text == "刷新" && refresh.Enabled, "refresh becomes available after update");
                SettingsCheck(main, floating, delegate { Click(Link(details, "设置")); }, floating, true, Path.Combine(output, "settings-from-floating.png"));
                floating.Hide();
                SettingsCheck(main, floating, delegate { Invoke(main, "OpenSettings"); }, null, true, Path.Combine(output, "settings-from-tray.png"));
                main.Show();
                SettingsCheck(main, floating, delegate { Invoke(main, "OpenSettings"); }, main, false, Path.Combine(output, "settings-from-main.png"));
                main.ExitManager();
            }
            File.WriteAllText(Path.Combine(output, "ui-actions.json"), Json.Write(new { success = true, checks = checks }), new UTF8Encoding(false));
            Console.WriteLine("All " + checks + " UI action checks passed."); return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.ToString()); File.WriteAllText(Path.Combine(output, "ui-actions.json"), Json.Write(new { success = false, checks = checks, error = ex.GetBaseException().Message }), new UTF8Encoding(false)); return 1;
        }
    }
}
