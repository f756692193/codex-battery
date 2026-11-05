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
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(IntPtr handle);
    static T Field<T>(object target, string name) { return (T)target.GetType().GetField(name, PrivateInstance).GetValue(target); }
    static void Invoke(object target, string name) { target.GetType().GetMethod(name, PrivateInstance).Invoke(target, null); }
    static void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
    static LinkLabel Link(HoverDetailsForm details, string text) { return Field<Panel>(details, "footer").Controls.OfType<LinkLabel>().Single(l => l.Text == text); }
    static void Click(LinkLabel link) { typeof(LinkLabel).GetMethod("OnLinkClicked", PrivateInstance).Invoke(link, new object[] { new LinkLabelLinkClickedEventArgs(link.Links[0]) }); }
    static void ShowHover(FloatingForm floating) { floating.Show(); typeof(FloatingForm).GetMethod("EvaluateHover", PrivateInstance).Invoke(floating, new object[] { true }); }
    static void StartupCheck(bool background, bool floatingEnabled) {
        using (var main = new MainForm(background, true)) {
            Field<Settings>(main, "settings").Floating = floatingEnabled;
            main.Show(); Application.DoEvents();
            Check(main.Visible == !background, "startup main visibility follows background flag, not floating setting: " + background + "/" + floatingEnabled);
            Check(Field<FloatingForm>(main, "floating").Visible == floatingEnabled, "startup preserves independent floating setting: " + background + "/" + floatingEnabled);
            main.ExitManager();
        }
    }
    static void SettingsCheck(MainForm main, FloatingForm floating, Action open, Form expectedOwner, bool taskbar, string screenshot) {
        bool wasHidden = !main.Visible;
        var settings = Field<Settings>(main, "settings"); int interval = settings.RefreshSeconds;
        open(); Application.DoEvents();
        var dialog = Application.OpenForms.OfType<SettingsForm>().Single();
        Check(dialog.Visible && IsWindowVisible(dialog.Handle), "settings window is shown without opening main first");
        Check(dialog.Owner == expectedOwner && (dialog.Owner == null || dialog.Owner.Visible), "settings owner is visible or absent");
        Check(dialog.ShowInTaskbar == taskbar && (!wasHidden || !main.Visible), "settings taskbar entry and hidden main state are correct");
        Check(!dialog.Modal && IsWindowEnabled(floating.Handle) && IsWindowEnabled(main.Handle), "modeless settings leave battery and main window enabled");
        Invoke(main, "OpenSettings"); Application.DoEvents();
        Check(Application.OpenForms.OfType<SettingsForm>().Count() == 1 && Field<SettingsForm>(main, "settingsWindow") == dialog, "opening settings twice reuses the existing window");
        floating.Show(); var battery = Field<Control>(floating, "label"); Point before = floating.Location;
        foreach (var step in new[] { new { Method = "OnMouseDown", X = 8, Y = 8 }, new { Method = "OnMouseMove", X = 20, Y = 16 }, new { Method = "OnMouseUp", X = 20, Y = 16 } }) {
            typeof(Control).GetMethod(step.Method, PrivateInstance).Invoke(battery, new object[] { new MouseEventArgs(MouseButtons.Left, 1, step.X, step.Y, 0) });
        }
        Check(floating.Left == before.X + 12 && floating.Top == before.Y + 8 && dialog.Visible, "battery drag updates position while settings remain open");
        Check(settings.FloatX == floating.Left && settings.FloatY == floating.Top, "dragged position is remembered independently of settings draft");
        Field<NumericUpDown>(dialog, "seconds").Value = interval + 1;
        using (var bitmap = new Bitmap(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(screenshot); }
        dialog.Controls.OfType<Button>().Single(b => b.Text == "取消").PerformClick(); Application.DoEvents();
        Check(settings.RefreshSeconds == interval && Field<SettingsForm>(main, "settingsWindow") == null && dialog.IsDisposed, "cancel discards settings draft and releases the window");
    }
    [STAThread] public static int Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        try {
            StartupCheck(false, true);
            StartupCheck(false, false);
            StartupCheck(true, true);
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
                Invoke(main, "OpenSettings"); var saved = Field<SettingsForm>(main, "settingsWindow");
                Field<NumericUpDown>(saved, "seconds").Value = 90;
                saved.Controls.OfType<Button>().Single(b => b.Text == "保存").PerformClick(); Application.DoEvents();
                Check(saved.IsDisposed && Field<SettingsForm>(main, "settingsWindow") == null && Field<Settings>(main, "settings").RefreshSeconds == 90, "save applies settings and releases the modeless window");
                Invoke(main, "OpenSettings"); var exitDialog = Field<SettingsForm>(main, "settingsWindow");
                main.ExitManager();
                Check(exitDialog.IsDisposed && !Application.OpenForms.OfType<SettingsForm>().Any(), "exiting manager closes an open settings window");
            }
            File.WriteAllText(Path.Combine(output, "ui-actions.json"), Json.Write(new { success = true, checks = checks }), new UTF8Encoding(false));
            Console.WriteLine("All " + checks + " UI action checks passed."); return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.ToString()); File.WriteAllText(Path.Combine(output, "ui-actions.json"), Json.Write(new { success = false, checks = checks, error = ex.GetBaseException().Message }), new UTF8Encoding(false)); return 1;
        }
    }
}
