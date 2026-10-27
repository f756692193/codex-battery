using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CodexQuotaManager {
    public static class Theme {
        public static readonly Color Bg = Color.FromArgb(17, 23, 34), Card = Color.FromArgb(27, 35, 49), Text = Color.FromArgb(236, 241, 247), Muted = Color.FromArgb(154, 171, 193), Green = Color.FromArgb(102, 228, 178), Amber = Color.FromArgb(255, 192, 100), Red = Color.FromArgb(255, 115, 126);
        public static Color Level(double n) { return n <= 10 ? Red : n <= 25 ? Amber : Green; }
        public static Button Button(string text, int width) {
            var b = new Button { Text = text, Width = width, Height = 36, FlatStyle = FlatStyle.Flat, BackColor = Card, ForeColor = Text, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0) };
            b.FlatAppearance.BorderColor = Color.FromArgb(56, 72, 94); return b;
        }
        public static Label Label(string text, float size, Color color) { return new Label { Text = text, AutoSize = true, Font = new Font("Microsoft YaHei UI", size), ForeColor = color, BackColor = Color.Transparent }; }
    }
    public static class BatteryIcons {
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        public static void Battery(Graphics g, RectangleF r, double? remaining, Color color) {
            using (var p = new Pen(color, Math.Max(1.5f, r.Height / 13))) {
                g.DrawRectangle(p, r.X, r.Y, r.Width - r.Width / 10, r.Height);
                float cap = r.Width / 16;
                using (var brush = new SolidBrush(color)) {
                    g.FillRectangle(brush, r.Right - r.Width / 10, r.Y + r.Height * .3f, cap, r.Height * .4f);
                    if (remaining.HasValue) {
                        float inset = Math.Max(2, r.Height / 6);
                        g.FillRectangle(brush, r.X + inset, r.Y + inset, Math.Max(0, (r.Width * .9f - inset * 2) * (float)remaining.Value / 100), r.Height - inset * 2);
                    }
                }
            }
        }
        public static Icon Make(double? n, bool number, bool unreadError) {
            using (var bitmap = new Bitmap(32, 32)) using (var g = Graphics.FromImage(bitmap)) {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                Color c = n.HasValue ? Theme.Level(n.Value) : Theme.Muted;
                if (number && n.HasValue) {
                    using (var f = new Font("Segoe UI", n >= 100 ? 13 : 17, FontStyle.Bold, GraphicsUnit.Pixel)) using (var b = new SolidBrush(c))
                        g.DrawString(Math.Floor(n.Value).ToString(CultureInfo.InvariantCulture), f, b, new RectangleF(0, 1, 32, 29), new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
                } else {
                    Battery(g, new RectangleF(2, 8, 28, 16), n, c);
                    if (!n.HasValue) using (var f = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel)) using (var b = new SolidBrush(c)) g.DrawString("?", f, b, 10, 7);
                }
                if (unreadError) using (var b = new SolidBrush(Theme.Amber)) g.FillEllipse(b, 23, 1, 8, 8);
                IntPtr h = bitmap.GetHicon(); try { return (Icon)Icon.FromHandle(h).Clone(); } finally { DestroyIcon(h); }
            }
        }
    }
    public class Hero : Control {
        public QuotaWindow Window; public bool Fresh; public string Message = "正在读取本机账号…";
        public Hero() { DoubleBuffered = true; Height = 152; BackColor = Theme.Card; }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = g.DpiX / 96f;
            var w = Fresh ? Window : null;
            Color c = w != null ? Theme.Level(w.Remaining) : Theme.Muted;
            BatteryIcons.Battery(g, new RectangleF(24*s, 49*s, 87*s, 49*s), w == null ? (double?)null : w.Remaining, c);
            using (var big = new Font("Segoe UI", 35, FontStyle.Bold)) using (var b = new SolidBrush(c)) g.DrawString(w == null ? "—" : Math.Floor(w.Remaining).ToString() + "%", big, b, 131*s, 23*s);
            using (var small = new Font("Microsoft YaHei UI", 10)) using (var b = new SolidBrush(Theme.Muted)) {
                string line = w == null ? Message : w.BucketName + " · " + w.DurationLabel + " · 剩余额度";
                g.DrawString(line, small, b, new RectangleF(136*s, 91*s, Math.Max(1, Width - 150*s), 50*s));
            }
        }
    }
    public class WindowCard : Control {
        public QuotaWindow Window; public bool Fresh, LocalTime, Selected;
        public WindowCard() { DoubleBuffered = true; Height = 111; BackColor = Theme.Card; Cursor = Cursors.Hand; }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g = e.Graphics; float s = g.DpiX / 96f; bool valid = Fresh && !Window.Expired(DateTimeOffset.UtcNow);
            using (var f = new Font("Microsoft YaHei UI", 10, FontStyle.Bold)) using (var b = new SolidBrush(Theme.Text))
                g.DrawString(Window.BucketName + " · " + Window.DurationLabel + (Selected ? "   · 当前图标" : ""), f, b, 17*s, 12*s);
            using (var f = new Font("Microsoft YaHei UI", 9)) using (var b = new SolidBrush(Theme.Muted)) {
                string reset = Clock.InZone(Window.Reset, LocalTime).ToString("yyyy-MM-dd HH:mm:ss");
                g.DrawString("重置：" + reset + "  ·  " + Clock.Zone(LocalTime), f, b, 17*s, 41*s);
                string status = valid ? "剩余 " + Math.Floor(Window.Remaining) + "%  ·  " + Window.Countdown(DateTimeOffset.UtcNow) + "后重置" : "上次结果 " + Math.Floor(Window.Remaining) + "%  ·  " + (Window.Expired(DateTimeOffset.UtcNow) ? "到期，等待确认" : "当前状态未知");
                g.DrawString(status, f, b, 17*s, 65*s);
            }
            using (var b = new SolidBrush(Color.FromArgb(47, 59, 77))) g.FillRectangle(b, 18*s, Height - 12*s, Width - 36*s, 4*s);
            if (valid) using (var b = new SolidBrush(Theme.Level(Window.Remaining))) g.FillRectangle(b, 18*s, Height - 12*s, (Width - 36*s) * (float)Window.Remaining / 100, 4*s);
        }
    }
    public class NewsSection : FlowLayoutPanel {
        public NewsSection() { FlowDirection = FlowDirection.TopDown; WrapContents = false; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; BackColor = Theme.Card; }
        public void Bind(NewsStore store, bool local, Action<NewsItem> open, Action check, Action more, bool all) {
            SuspendLayout(); while (Controls.Count > 0) { var c = Controls[0]; Controls.Remove(c); c.Dispose(); }
            if (!store.Enabled) { ResumeLayout(); FitWidth(Width); return; }
            var items = store.VisibleItems(DateTimeOffset.UtcNow).Take(30).ToList();
            if (items.Count == 0 && !all) { ResumeLayout(); FitWidth(Width); return; }
            bool uncertain = !store.Healthy || store.Feed.Stale(DateTimeOffset.UtcNow);
            if (items.Count == 0) {
                string line = store.Busy ? "Tibo：正在检查近3天重置卡消息…" : uncertain ? "Tibo：近3天暂无可显示消息（来源待更新）" : "Tibo：近3天暂无重置卡消息";
                AddText(line, Theme.Muted, 8.5f);
            } else AddText("Tibo · 重置卡（近3天，待核实）" + (uncertain ? " · 待更新" : ""), Theme.Muted, 8.5f);
            foreach (var item in items) {
                bool unread = store.Unread(item, DateTimeOffset.UtcNow);
                var link = Link(Clock.InZone(item.Published, local).ToString("MM-dd") + "  " + item.Summary + "  ↗", delegate { open(item); });
                link.AutoSize = false; link.AutoEllipsis = true; link.Height = 24; link.Width = Math.Max(80, Width - 8); link.Tag = item;
                link.LinkColor = unread ? Theme.Green : Theme.Muted;
                link.AccessibleDescription = item.Text;
            }
            if (all) { Link("立即检查消息", check); Link("打开 Tibo 的 X 主页", more); }
            ResumeLayout(); FitWidth(Width);
        }
        void AddText(string text, Color color, float size) { var label = Theme.Label(text, size, color); label.Margin = new Padding(0, 0, 0, 8); Controls.Add(label); }
        LinkLabel Link(string text, Action click) {
            var link = new LinkLabel { Text = text, AutoSize = true, Font = new Font("Microsoft YaHei UI", 9), BackColor = Theme.Card,
                LinkColor = Color.FromArgb(122, 190, 246), ActiveLinkColor = Theme.Green, VisitedLinkColor = Theme.Muted, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 2, 0, 12) };
            link.LinkClicked += delegate { click(); }; Controls.Add(link); return link;
        }
        public void FitWidth(int width) {
            width = Math.Max(80, width); MinimumSize = new Size(width, 0); MaximumSize = new Size(width, 0); Width = width;
            foreach (Control c in Controls) {
                c.MaximumSize = new Size(Math.Max(60, width - 8), 0);
                if (c.Tag is NewsItem) { c.Width = Math.Max(60, width - 8); c.Height = (int)(24 * DeviceDpi / 96f); }
            }
            PerformLayout();
        }
    }
    public class HoverQuotaState {
        public Snapshot Snapshot;
        public bool Fresh, Busy, LocalTime;
        public string Status = "正在查询账号额度…";
    }
    public class HoverQuotaView : Control {
        HoverQuotaState state = new HoverQuotaState();
        public HoverQuotaState State { get { return state; } }
        List<QuotaWindow> Windows { get { return state.Fresh && state.Snapshot != null ? state.Snapshot.Windows : new List<QuotaWindow>(); } }
        public HoverQuotaView() { DoubleBuffered = true; BackColor = Theme.Card; }
        public void Bind(HoverQuotaState value) {
            state = value;
            var windows = Windows;
            int groups = windows.Select(w => w.BucketId).Distinct().Count();
            int logicalHeight = 32 + (windows.Count == 0 ? 48 : windows.Count * 48 + (groups > 1 ? groups * 23 : 0)) + 59;
            Height = (int)Math.Ceiling(logicalHeight * DeviceDpi / 96f);
            string facts = string.Join("; ", windows.Select(w => w.DurationLabel + ": " + (w.Expired(DateTimeOffset.UtcNow) ? "到期，待确认" : BatteryGauge.Percent(w.Remaining) + "，" + w.Countdown(DateTimeOffset.UtcNow) + "后重置")));
            AccessibleName = "Codex " + (state.Snapshot == null || !state.Fresh ? "" : state.Snapshot.Plan) + "; " + (windows.Count == 0 ? state.Status : facts) + "; 重置卡 " + CardText() + "; " + ExpiryText();
            Invalidate();
        }
        string CardText() { return state.Fresh && state.Snapshot != null && state.Snapshot.ResetCards.HasValue ? state.Snapshot.ResetCards.Value + " 张" : "待更新"; }
        string ExpiryText() { return state.Fresh && state.Snapshot != null ? state.Snapshot.ResetCardExpiryText(DateTimeOffset.UtcNow, state.LocalTime) : "到期时间待确认"; }
        static void TextLine(Graphics g, string text, float size, Color color, RectangleF rect, bool bold, StringAlignment align) {
            using (var font = new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap, Alignment = align, LineAlignment = StringAlignment.Center }) g.DrawString(text, font, brush, rect, format);
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e); var g = e.Graphics; float scale = DeviceDpi / 96f; g.ScaleTransform(scale, scale);
            float width = Width / scale; var now = DateTimeOffset.UtcNow;
            var windows = Windows; int groups = windows.Select(w => w.BucketId).Distinct().Count();
            string plan = state.Fresh && state.Snapshot != null && !string.IsNullOrWhiteSpace(state.Snapshot.Plan) ? " · " + state.Snapshot.Plan : "";
            TextLine(g, "Codex" + plan, 13, Theme.Text, new RectangleF(0, 0, width - 64, 25), true, StringAlignment.Near);
            TextLine(g, "剩余", 11, Theme.Muted, new RectangleF(width - 64, 0, 64, 25), false, StringAlignment.Far);
            float y = 32;
            if (windows.Count == 0) {
                TextLine(g, state.Busy ? "正在查询账号额度…" : "当前额度未知 · 待刷新", 13, Theme.Muted, new RectangleF(0, y, width, 22), false, StringAlignment.Near);
                TextLine(g, state.Busy ? "复用本机 Codex 登录" : state.Status, 11, Theme.Muted, new RectangleF(0, y + 22, width, 20), false, StringAlignment.Near); y += 48;
            } else foreach (var group in windows.GroupBy(w => w.BucketId)) {
                if (groups > 1) { TextLine(g, group.First().BucketName, 11, Theme.Muted, new RectangleF(0, y, width, 20), false, StringAlignment.Near); y += 23; }
                foreach (var w in group.OrderBy(w => w.Minutes)) {
                    bool valid = !w.Expired(now);
                    string period = w.DurationLabel.Replace(" 天周期", "天").Replace(" 小时周期", "小时").Replace(" 分钟周期", "分");
                    TextLine(g, period, 12, Theme.Muted, new RectangleF(0, y, 51, 21), false, StringAlignment.Near);
                    var bar = new RectangleF(55, y + 6, 108, 10);
                    using (var track = new SolidBrush(Color.FromArgb(58, 72, 73))) g.FillRectangle(track, bar);
                    if (valid) using (var fill = new SolidBrush(Color.FromArgb(120, 208, 151))) g.FillRectangle(fill, bar.X, bar.Y, bar.Width * (float)w.Remaining / 100, bar.Height);
                    using (var line = new Pen(Color.FromArgb(70, Theme.Card), .7f)) for (int i = 1; i < 12; i++) g.DrawLine(line, bar.X + bar.Width * i / 12, bar.Top, bar.X + bar.Width * i / 12, bar.Bottom);
                    TextLine(g, valid ? BatteryGauge.Percent(w.Remaining) : "—", 12, valid ? Theme.Green : Theme.Muted, new RectangleF(170, y, 49, 21), true, StringAlignment.Far);
                    TextLine(g, valid ? w.Countdown(now) + "后重置" : "到期 · 待确认", 12, valid ? Theme.Green : Theme.Amber, new RectangleF(232, y, Math.Max(1, width - 232), 21), false, StringAlignment.Far);
                    TextLine(g, "下次重置：" + Clock.InZone(w.Reset, state.LocalTime).ToString("MM-dd HH:mm") + " · " + Clock.Zone(state.LocalTime), 10.5f, Theme.Muted, new RectangleF(55, y + 23, Math.Max(1, width - 55), 20), false, StringAlignment.Near);
                    y += 48;
                }
            }
            using (var line = new Pen(Color.FromArgb(49, 62, 79))) g.DrawLine(line, 0, y + 2, width, y + 2);
            TextLine(g, "重置卡", 12, Theme.Text, new RectangleF(0, y + 9, width / 2, 23), false, StringAlignment.Near);
            TextLine(g, CardText(), 12, Theme.Text, new RectangleF(width / 2, y + 9, width / 2, 23), true, StringAlignment.Far);
            Color expiryColor = Theme.Muted;
            if (state.Fresh && state.Snapshot != null && state.Snapshot.ResetCardExpiryKnown && state.Snapshot.ResetCardExpiresAt.HasValue) {
                var remaining = state.Snapshot.ResetCardExpiresAt.Value - now;
                expiryColor = remaining.TotalDays < 1 ? Theme.Red : remaining.TotalDays < 3 ? Theme.Amber : Theme.Muted;
            }
            TextLine(g, ExpiryText(), 10.5f, expiryColor, new RectangleF(0, y + 34, width, 20), false, StringAlignment.Near);
        }
    }
    public class HoverDetailsForm : Form {
        readonly HoverQuotaView content;
        readonly Panel scroll;
        readonly NewsSection news;
        readonly Panel newsRule, footer;
        readonly LinkLabel refreshLink;
        internal string Information { get { return content.AccessibleName; } }
        internal HoverQuotaState QuotaState { get { return content.State; } }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } }
        public HoverDetailsForm(Action refresh, Action configure, Action exit) {
            Text = "Codex 额度信息"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true;
            StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.Dpi; BackColor = Theme.Card; Padding = new Padding(1);
            scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = Padding.Empty, BackColor = Theme.Card };
            content = new HoverQuotaView(); content.Bind(new HoverQuotaState());
            news = new NewsSection { Location = new Point(14, 180), Width = 300 };
            newsRule = new Panel { Height = 1, BackColor = Color.FromArgb(49, 62, 79) };
            footer = new Panel { BackColor = Theme.Card };
            var rule = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Color.FromArgb(49, 62, 79) }; footer.Controls.Add(rule);
            refreshLink = AddAction("刷新", refresh, false, 0, false); AddAction("设置", configure, true, 83, true); AddAction("退出", exit, true, 0, true);
            scroll.Controls.Add(content); scroll.Controls.Add(newsRule); scroll.Controls.Add(news); scroll.Controls.Add(footer); Controls.Add(scroll);
        }
        LinkLabel AddAction(string text, Action action, bool right, int offset, bool dismiss) {
            var link = new LinkLabel { Text = text, AutoSize = false, Size = new Size(64, 30), Font = new Font("Microsoft YaHei UI", 9), LinkColor = Theme.Muted, ActiveLinkColor = Theme.Green, LinkBehavior = LinkBehavior.HoverUnderline, TextAlign = right ? ContentAlignment.MiddleRight : ContentAlignment.MiddleLeft, Cursor = Cursors.Hand, Tag = right ? offset + 1 : 0 };
            link.LinkClicked += delegate { if (dismiss) Hide(); action(); }; footer.Controls.Add(link); return link;
        }
        public void SetNews(NewsStore state, bool local, Action<NewsItem> open, Action check, Action more) { news.Bind(state, local, open, check, more, false); }
        public void SetQuota(HoverQuotaState state) { content.Bind(state); refreshLink.Text = state.Busy ? "刷新中…" : "刷新"; refreshLink.Enabled = !state.Busy; }
        public void UpdateContent(Rectangle anchor) {
            var previousScroll = scroll.AutoScrollPosition; scroll.SuspendLayout(); scroll.AutoScrollPosition = Point.Empty;
            var area = Screen.FromRectangle(anchor).WorkingArea;
            float scale = DeviceDpi / 96f; int width = Math.Min((int)(424 * scale), area.Width - 16), inset = (int)(16 * scale);
            bool hasNews = news.Controls.Count > 0;
            int needed = 0, reserve = 0;
            for (int pass = 0; pass < 2; pass++) {
                content.Location = new Point(inset, (int)(10 * scale)); content.Width = width - inset * 2 - 2 - reserve;
                newsRule.Visible = hasNews; newsRule.SetBounds(inset, content.Bottom + (int)(8 * scale), content.Width, 1);
                news.Location = new Point(inset, newsRule.Bottom + (int)(12 * scale)); news.FitWidth(content.Width);
                footer.SetBounds(inset, hasNews ? news.Bottom : content.Bottom + (int)(8 * scale), content.Width, (int)(39 * scale));
                needed = footer.Bottom + (int)(5 * scale);
                if (needed + 2 <= area.Height - 24) break;
                reserve = SystemInformation.VerticalScrollBarWidth;
            }
            foreach (LinkLabel link in footer.Controls.OfType<LinkLabel>()) {
                int tag = (int)link.Tag; link.SetBounds(tag == 0 ? 0 : footer.Width - (int)((64 + tag - 1) * scale), (int)(5 * scale), (int)(64 * scale), (int)(30 * scale));
            }
            int height = Math.Min(needed + 2, area.Height - 24);
            ClientSize = new Size(width, Math.Max((int)(90 * scale), height));
            scroll.AutoScrollMinSize = new Size(0, needed);
            Reposition(anchor);
            scroll.AutoScrollPosition = new Point(0, -previousScroll.Y); scroll.ResumeLayout();
        }
        public void Reposition(Rectangle anchor) {
            var area = Screen.FromRectangle(anchor).WorkingArea;
            int x = Math.Max(area.Left + 8, Math.Min(anchor.Right - Width, area.Right - Width - 8));
            int y = anchor.Top - Height - 7;
            if (y < area.Top + 8) y = anchor.Bottom + 7;
            y = Math.Max(area.Top + 8, Math.Min(y, area.Bottom - Height - 8));
            Location = new Point(x, y);
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); ActiveControl = null; scroll.AutoScrollPosition = Point.Empty; if (Owner != null) UpdateContent(Owner.Bounds); }
        internal object Metrics() { return new { client = ClientSize.ToString(), screen = Screen.FromControl(this).WorkingArea.ToString(), content = content.Bounds.ToString(), news = news.Bounds.ToString(), scroll = scroll.AutoScrollPosition.ToString() }; }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var p = new Pen(Color.FromArgb(58, 77, 98))) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }
    }
    public class BatteryGauge : Control {
        double? remaining; bool unread;
        public double? Remaining { get { return remaining; } set { if (remaining != value) { remaining = value; Invalidate(); } } }
        public bool UnreadNews { get { return unread; } set { if (unread != value) { unread = value; Invalidate(); } } }
        public BatteryGauge() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        public static string Percent(double? n) { return n.HasValue ? (Math.Floor(n.Value * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture) + "%" : "—"; }
        public static int CapWidth(int width) { return Math.Max(3, (int)Math.Round(width * .065)); }
        public bool OnCap(Point point) { return point.X >= Width - CapWidth(Width) - 1; }
        public static GraphicsPath Shape(Size size) {
            int cap = CapWidth(size.Width), body = size.Width - cap;
            int top = (int)Math.Round(size.Height * .31), bottom = size.Height - top;
            var path = new GraphicsPath();
            path.AddPolygon(new[] { new Point(0, 0), new Point(body, 0), new Point(body, top), new Point(size.Width, top), new Point(size.Width, bottom), new Point(body, bottom), new Point(body, size.Height), new Point(0, size.Height) });
            return path;
        }
        public static void DrawGauge(Graphics g, Size size, double? value, bool hasNews) {
            if (size.Width < 10 || size.Height < 8) return;
            int cap = CapWidth(size.Width), bodyWidth = size.Width - cap;
            float scale = size.Height / 24f, inset = Math.Max(1.5f, 2 * scale);
            var body = new RectangleF(.5f, .5f, bodyWidth - 1, size.Height - 1);
            var inside = new RectangleF(inset, inset, bodyWidth - inset * 2, size.Height - inset * 2);
            g.SmoothingMode = SmoothingMode.None;
            using (var background = new SolidBrush(Color.FromArgb(25, 29, 34))) g.FillRectangle(background, body);
            if (value.HasValue) {
                double n = Math.Max(0, Math.Min(100, value.Value));
                Color top = Color.FromArgb(66, 190, 125);
                Color bottom = Color.FromArgb(23, 128, 74);
                using (var fill = new LinearGradientBrush(inside, top, bottom, LinearGradientMode.Vertical)) g.FillRectangle(fill, inside.X, inside.Y, inside.Width * (float)n / 100, inside.Height);
            }
            using (var border = new Pen(Color.FromArgb(142, 154, 165), Math.Max(1, scale))) g.DrawRectangle(border, body.X, body.Y, body.Width, body.Height);
            float capY = (float)Math.Round(size.Height * .31), capHeight = size.Height - capY * 2;
            using (var tip = new SolidBrush(hasNews ? Theme.Red : Color.FromArgb(123, 135, 146))) g.FillRectangle(tip, bodyWidth, capY, cap - .5f, capHeight);
            using (var font = new Font("Segoe UI", size.Height * .53f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap }) {
                var textBox = new RectangleF(0, 0, bodyWidth, size.Height - scale);
                // A slight shadow keeps white text readable over both the fill and empty area.
                using (var shadow = new SolidBrush(Color.FromArgb(130, 0, 0, 0))) g.DrawString(Percent(value), font, shadow, new RectangleF(textBox.X + scale, textBox.Y + scale, textBox.Width, textBox.Height), format);
                using (var white = new SolidBrush(Color.FromArgb(245, 248, 252))) g.DrawString(Percent(value), font, white, textBox, format);
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { DrawGauge(e.Graphics, ClientSize, remaining, unread); }
    }
    public class FloatingForm : Form {
        readonly BatteryGauge label;
        readonly HoverDetailsForm details;
        readonly System.Windows.Forms.Timer hoverTimer;
        readonly ContextMenuStrip menu;
        Point drag; bool moving, dragged, cleaned; readonly Settings settings;
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams { get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; } }
        public FloatingForm(Settings s, Action open, Action refresh, Action configure, Action exit, Action showNews) {
            settings = s; Text = "Codex 剩余额度"; FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(64, 24); BackColor = Theme.Card; Padding = Padding.Empty;
            label = new BatteryGauge { Dock = DockStyle.Fill, Cursor = Cursors.SizeAll, Text = "—" }; Controls.Add(label);
            details = new HoverDetailsForm(refresh, configure, exit);
            hoverTimer = new System.Windows.Forms.Timer { Interval = 140 };
            hoverTimer.Tick += delegate {
                if (!Visible || moving || menu.Visible) { details.Hide(); return; }
                var point = Cursor.Position;
                // Inflate by the gap so moving from the small window to its popup doesn't flicker.
                var anchor = Bounds; anchor.Inflate(8, 8);
                var popup = details.Bounds; popup.Inflate(8, 8);
                EvaluateHover(anchor.Contains(point) || (details.Visible && popup.Contains(point)));
            };
            label.MouseEnter += delegate { if (!hoverTimer.Enabled) hoverTimer.Start(); };
            label.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { drag = e.Location; moving = !(label.UnreadNews && label.OnCap(e.Location)); dragged = false; details.Hide(); } };
            label.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left && label.UnreadNews && label.OnCap(e.Location)) { details.Hide(); showNews(); } };
            label.MouseMove += delegate(object sender, MouseEventArgs e) {
                if (moving && (dragged || Math.Abs(e.X - drag.X) + Math.Abs(e.Y - drag.Y) >= 3)) { dragged = true; Location = new Point(Left + e.X - drag.X, Top + e.Y - drag.Y); }
            };
            label.MouseUp += delegate { moving = false; if (dragged) { settings.FloatX = Left; settings.FloatY = Top; try { settings.Save(); } catch { } } };
            label.DoubleClick += delegate { details.Hide(); open(); };
            menu = new ContextMenuStrip(); menu.Opening += delegate { details.Hide(); };
            menu.Items.Add("打开额度详情", null, delegate { open(); });
            menu.Items.Add("立即刷新", null, delegate { refresh(); });
            menu.Items.Add("Tibo 重置 / 赠送消息…", null, delegate { showNews(); });
            menu.Items.Add("设置…", null, delegate { configure(); });
            menu.Items.Add("隐藏小浮窗", null, delegate { Hide(); settings.Floating = false; try { settings.Save(); } catch { } });
            menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出管家", null, delegate { exit(); }); label.ContextMenuStrip = menu;
            StartPosition = FormStartPosition.Manual;
            var area = Screen.PrimaryScreen.WorkingArea;
            Location = s.FloatX < 0 ? new Point(area.Right - Width - 24, area.Bottom - Height - 24) : new Point(s.FloatX, s.FloatY);
            if (!Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(Bounds))) Location = new Point(area.Left + 24, area.Top + 24);
        }
        void EvaluateHover(bool inside) {
            if (inside && Visible && !moving) {
                details.UpdateContent(Bounds);
                if (!details.Visible) details.Show(this);
            } else { details.Hide(); hoverTimer.Stop(); }
        }
        public void UpdateQuota(QuotaWindow w, HoverQuotaState state) {
            label.Remaining = w == null ? (double?)null : w.Remaining;
            label.Text = BatteryGauge.Percent(label.Remaining);
            details.SetQuota(state); label.AccessibleName = w == null ? "Codex 当前额度未知：" + state.Status : "Codex 剩余 " + Math.Floor(w.Remaining) + "%";
            if (details.Visible) details.UpdateContent(Bounds);
        }
        public void SetNews(NewsStore state, bool local, Action<NewsItem> open, Action check, Action more) { details.SetNews(state, local, open, check, more); label.UnreadNews = state.HasBadge(DateTimeOffset.UtcNow); if (details.Visible) details.UpdateContent(Bounds); }
        public void UpdateNewsBadge(bool visible) { label.UnreadNews = visible; }
        public void HideHover() { details.Hide(); hoverTimer.Stop(); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible && details != null) { details.Hide(); hoverTimer.Stop(); } }
        protected override void OnResize(EventArgs e) {
            base.OnResize(e); if (ClientSize.Width < 10 || ClientSize.Height < 8) return;
            using (var shape = BatteryGauge.Shape(ClientSize)) { var old = Region; Region = new Region(shape); if (old != null) old.Dispose(); }
        }
        protected override void Dispose(bool disposing) {
            if (disposing && !cleaned) { cleaned = true; hoverTimer.Dispose(); details.Dispose(); menu.Dispose(); }
            base.Dispose(disposing);
        }
        internal object RenderAndCheck(string folder) {
            Show(); Application.DoEvents();
            bool badgeShown = label.UnreadNews;
            using (var b = new Bitmap(Width, Height)) { using (var g = Graphics.FromImage(b)) BatteryGauge.DrawGauge(g, ClientSize, label.Remaining, label.UnreadNews); b.Save(Path.Combine(folder, "floating-small.png")); }
            EvaluateHover(true); Application.DoEvents(); bool shown = details.Visible;
            var bounds = Rectangle.Union(Bounds, details.Bounds); using (var b = new Bitmap(bounds.Width, bounds.Height)) {
                using (var g = Graphics.FromImage(b)) g.Clear(Theme.Bg);
                DrawToBitmap(b, new Rectangle(Left - bounds.Left, Top - bounds.Top, Width, Height));
                details.DrawToBitmap(b, new Rectangle(details.Left - bounds.Left, details.Top - bounds.Top, details.Width, details.Height));
                b.Save(Path.Combine(folder, "floating-hover.png"));
            }
            EvaluateHover(true); bool stayed = details.Visible;
            EvaluateHover(false); bool hidden = !details.Visible;
            EvaluateHover(true); Hide(); bool follows = !details.Visible;
            string originalText = label.Text; var originalState = details.QuotaState; double? originalValue = label.Remaining;
            UpdateQuota(null, new HoverQuotaState { Snapshot = originalState.Snapshot, Status = "查询失败 · 当前额度未知" }); Show(); EvaluateHover(true);
            bool unknown = label.Text == "—", changed = details.Information.Contains("查询失败");
            var area = Screen.FromControl(this).WorkingArea;
            details.UpdateContent(new Rectangle(area.Left + 2, area.Top + 2, Width, Height)); bool fits = area.Contains(details.Bounds);
            Hide(); label.Text = originalText; label.Remaining = originalValue; details.SetQuota(originalState); details.UpdateContent(Bounds);
            using (var b = new Bitmap(320, 132)) using (var g = Graphics.FromImage(b)) {
                g.Clear(Theme.Bg);
                var samples = new double?[] { 0, 10, 25, 56.6, 95, 100, null };
                for (int i = 0; i < samples.Length; i++) { var state = g.Save(); g.TranslateTransform(12 + (i % 4) * 76, 16 + (i / 4) * 58); BatteryGauge.DrawGauge(g, new Size(64, 24), samples[i], false); g.Restore(state); }
                b.Save(Path.Combine(folder, "battery-states.png"));
            }
            return new { compactWidth = Width, compactHeight = Height, text = label.Text, hoverOpens = shown, staysWhileInside = stayed, leavesClose = hidden, hidesWithAnchor = follows, unknownShowsDash = unknown, hoverUpdates = changed, popupStaysOnScreen = fits, badgeVisible = badgeShown, popup = details.Metrics() };
        }
    }
    public class SettingsForm : Form {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        readonly Settings s;
        readonly TextBox path, newsFeed;
        readonly NumericUpDown seconds, threshold;
        readonly CheckBox notify, floating, local, number, startup, newsEnabled;
        public static bool StartupEnabled() { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("CodexQuotaManager") != null; }
        public SettingsForm(Settings settings, string detected) {
            s = settings; Text = "设置 · Codex 额度电量管家"; Font = new Font("Microsoft YaHei UI", 10); AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Theme.Bg; ForeColor = Theme.Text; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(580, 634);
            var title = Theme.Label("偏好设置", 17, Theme.Text); title.Location = new Point(24, 19); Controls.Add(title);
            var desc = Theme.Label("复用本机 Codex 登录，无需填写密码或 API Key。", 9, Theme.Muted); desc.Location = new Point(25, 61); Controls.Add(desc);
            var pLabel = Theme.Label("Codex EXE 路径（留空自动检测）", 10, Theme.Text); pLabel.Location = new Point(25, 99); Controls.Add(pLabel);
            path = new TextBox { Text = s.CodexPath, Location = new Point(25, 129), Width = 440, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle }; Controls.Add(path);
            var browse = Theme.Button("选择", 70); browse.Location = new Point(478, 124); browse.Click += delegate { using (var d = new OpenFileDialog { Filter = "Codex 程序 (codex.exe)|codex.exe|EXE 程序 (*.exe)|*.exe", Title = "选择本机 codex.exe" }) { if (d.ShowDialog(this) == DialogResult.OK) path.Text = d.FileName; } }; Controls.Add(browse);
            var detectedLabel = Theme.Label("自动检测：" + (string.IsNullOrEmpty(detected) ? "尚未找到" : detected), 8, Theme.Muted); detectedLabel.AutoSize = false; detectedLabel.Size = new Size(530, 35); detectedLabel.Location = new Point(25, 165); Controls.Add(detectedLabel);
            var refreshLabel = Theme.Label("刷新间隔（秒）", 10, Theme.Text); refreshLabel.Location = new Point(25, 212); Controls.Add(refreshLabel);
            seconds = new NumericUpDown { Minimum = 30, Maximum = 3600, Value = s.RefreshSeconds, Location = new Point(170, 208), Width = 90 }; Controls.Add(seconds);
            notify = Check("额度低于阈值时提醒", s.LowNotify, 25, 251); threshold = new NumericUpDown { Minimum = 1, Maximum = 99, Value = s.LowThreshold, Location = new Point(260, 249), Width = 68 }; Controls.Add(threshold);
            var percent = Theme.Label("%（每个周期提醒一次）", 9, Theme.Muted); percent.Location = new Point(335, 253); Controls.Add(percent);
            floating = Check("显示小浮窗（悬停查看更多信息）", s.Floating, 25, 293);
            number = Check("托盘显示数字（替代电池图标）", s.NumberIcon, 25, 333);
            local = Check("使用系统本地时区（默认北京时间）", s.LocalTime, 25, 373);
            startup = Check("登录 Windows 后自动启动", StartupEnabled(), 25, 413);
            newsEnabled = Check("检查 Tibo 的重置 / 赠送消息（每5分钟）", s.TiboNews, 25, 453);
            var feedLabel = Theme.Label("自定义 RSS 地址（留空使用公开转发源）", 9, Theme.Muted); feedLabel.Location = new Point(25, 493); Controls.Add(feedLabel);
            newsFeed = new TextBox { Text = s.TiboFeedUrl, Location = new Point(25, 522), Width = 523, BackColor = Theme.Card, ForeColor = Theme.Text }; Controls.Add(newsFeed);
            var hint = Theme.Label("不需要 X 密码或 API Key；消息需点开原帖确认。", 8, Theme.Muted); hint.Location = new Point(25, 555); Controls.Add(hint);
            var save = Theme.Button("保存", 90); save.Location = new Point(458, 585); save.Click += Save; Controls.Add(save);
            var cancel = Theme.Button("取消", 90); cancel.Location = new Point(358, 585); cancel.Click += delegate { Close(); }; Controls.Add(cancel); CancelButton = cancel;
        }
        CheckBox Check(string text, bool value, int x, int y) { var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Location = new Point(x, y), ForeColor = Theme.Text }; Controls.Add(c); return c; }
        void Save(object sender, EventArgs e) {
            try {
                string manual = path.Text.Trim().Trim('"'); if (manual.Length > 0) Locator.Find(manual);
                Uri uri; string feed = newsFeed.Text.Trim(); if (feed.Length > 0 && (!Uri.TryCreate(feed, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0)) throw new InvalidOperationException("RSS 地址须为不包含账号信息的 HTTPS 链接。");
                // Startup is changed only after the user clicks Save.
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) {
                    if (startup.Checked) key.SetValue("CodexQuotaManager", "\"" + Application.ExecutablePath + "\" --background");
                    else key.DeleteValue("CodexQuotaManager", false);
                }
                s.CodexPath = manual; s.RefreshSeconds = (int)seconds.Value; s.LowThreshold = (int)threshold.Value; s.LowNotify = notify.Checked;
                s.Floating = floating.Checked; s.LocalTime = local.Checked; s.NumberIcon = number.Checked; s.TiboNews = newsEnabled.Checked; s.TiboFeedUrl = feed; s.Save(); DialogResult = DialogResult.OK; Close();
            } catch (Exception ex) { MessageBox.Show(this, ex.Message, "设置未保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
    public class MainForm : Form {
        readonly Settings settings; readonly QuotaClient client = new QuotaClient(); readonly CancellationTokenSource stop = new CancellationTokenSource();
        readonly NotifyIcon tray; Icon trayImage; readonly Hero hero; readonly Label accountLabel, stateLabel, updatedLabel, sourceLabel; readonly FlowLayoutPanel cards;
        readonly ComboBox windowPicker; readonly Button refresh; readonly System.Windows.Forms.Timer timer; readonly FloatingForm floating;
        Snapshot snapshot; DateTimeOffset lastSuccess, nextRefresh = DateTimeOffset.MinValue; bool healthy, busy, quitting, preview, pickerUpdating, expiryQueued, resourcesDisposed; string error = "首次查询中"; string detected = ""; string iconSignature = "";
        readonly List<string> pickerKeys = new List<string>(); readonly HashSet<string> alerted = new HashSet<string>();
        readonly NewsStore newsState = new NewsStore(); readonly NewsClient newsClient = new NewsClient();
        DateTimeOffset nextNews = DateTimeOffset.MinValue; bool newsBusy; NewsSection newsWindowSection;
        public MainForm(bool background, bool previewMode) {
            preview = previewMode; settings = preview ? new Settings() : Settings.Load();
            Text = "Codex 额度电量管家"; Font = new Font("Microsoft YaHei UI", 10); AutoScaleMode = AutoScaleMode.Dpi; BackColor = Theme.Bg; ForeColor = Theme.Text;
            ClientSize = new Size(650, 720); MinimumSize = new Size(640, 700); StartPosition = FormStartPosition.CenterScreen;
            Icon = BatteryIcons.Make(100, false, false);
            var title = Theme.Label("Codex", 25, Theme.Text); title.Location = new Point(27, 19); Controls.Add(title);
            var subtitle = Theme.Label("额度电量管家  /  WINDOWS · v" + BuildInfo.Version, 9, Theme.Muted); subtitle.Location = new Point(30, 72); Controls.Add(subtitle);
            accountLabel = Theme.Label("本机账号 · 正在检测", 9, Theme.Muted); accountLabel.Location = new Point(30, 106); accountLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left; Controls.Add(accountLabel);
            hero = new Hero { Location = new Point(30, 143), Width = 590, Font = Font, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right }; Controls.Add(hero);
            stateLabel = Theme.Label("正在读取真实额度", 9, Theme.Green); stateLabel.Location = new Point(31, 310); stateLabel.MaximumSize = new Size(580, 43); stateLabel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; Controls.Add(stateLabel);
            var selectLabel = Theme.Label("托盘显示", 9, Theme.Muted); selectLabel.Location = new Point(31, 365); Controls.Add(selectLabel);
            windowPicker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(112, 360), Width = 508, BackColor = Theme.Card, ForeColor = Theme.Text, FlatStyle = FlatStyle.Flat, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            windowPicker.DrawMode = DrawMode.OwnerDrawFixed;
            windowPicker.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                using (var b = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? Color.FromArgb(48, 71, 90) : Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                if (e.Index >= 0) TextRenderer.DrawText(e.Graphics, windowPicker.Items[e.Index].ToString(), e.Font, e.Bounds, Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            windowPicker.SelectedIndexChanged += delegate { if (!pickerUpdating && windowPicker.SelectedIndex >= 0) { settings.SelectedWindow = pickerKeys[windowPicker.SelectedIndex]; SaveSettings(); RebuildCards(); UpdateVisuals(); } }; Controls.Add(windowPicker); UpdatePicker();
            cards = new FlowLayoutPanel { Location = new Point(30, 406), Size = new Size(590, 143), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, BackColor = Theme.Bg }; Controls.Add(cards);
            cards.Resize += delegate { ResizeCards(); };
            updatedLabel = Theme.Label("尚无成功刷新记录", 9, Theme.Muted); updatedLabel.Location = new Point(31, 566); updatedLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; Controls.Add(updatedLabel);
            sourceLabel = Theme.Label("数据来自本机 Codex · 联网查询账号额度", 8, Theme.Muted); sourceLabel.Location = new Point(31, 595); sourceLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; Controls.Add(sourceLabel);
            var closeHint = Theme.Label("关闭窗口后留在托盘；右键托盘可彻底退出。", 8, Theme.Muted); closeHint.Location = new Point(31, 623); closeHint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; Controls.Add(closeHint);
            refresh = Theme.Button("立即刷新", 112); refresh.Location = new Point(30, 660); refresh.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; refresh.Click += async delegate { await RefreshQuota(); }; Controls.Add(refresh);
            var config = Theme.Button("设置", 84); config.Location = new Point(153, 660); config.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; config.Click += delegate { OpenSettings(); }; Controls.Add(config);
            var mini = Theme.Button("小浮窗模式", 128); mini.Location = new Point(249, 660); mini.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; mini.Click += delegate { settings.Floating = true; SaveSettings(); SetFloating(); Hide(); }; Controls.Add(mini);
            var messages = Theme.Button("Tibo 消息", 112); messages.Location = new Point(389, 660); messages.Anchor = AnchorStyles.Bottom | AnchorStyles.Left; messages.Click += delegate { OpenNews(); }; Controls.Add(messages);
            var exit = Theme.Button("退出管家", 105); exit.Location = new Point(515, 660); exit.Anchor = AnchorStyles.Bottom | AnchorStyles.Right; exit.Click += delegate { ExitManager(); }; Controls.Add(exit);
            tray = new NotifyIcon { Text = "Codex 额度 · 正在查询", Visible = !preview };
            var menu = new ContextMenuStrip(); menu.Items.Add("打开额度详情", null, delegate { OpenDetails(); }); menu.Items.Add("立即刷新", null, async delegate { await RefreshQuota(); }); menu.Items.Add("设置…", null, delegate { OpenSettings(); });
            menu.Items.Add("Tibo 重置 / 赠送消息…", null, delegate { OpenNews(); });
            menu.Items.Add("显示 / 隐藏小浮窗", null, delegate { settings.Floating = !settings.Floating; SaveSettings(); SetFloating(); }); menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("打开配置目录", null, delegate { Directory.CreateDirectory(Settings.DataDir); Process.Start("explorer.exe", "\"" + Settings.DataDir + "\""); });
            menu.Items.Add("退出管家", null, delegate { ExitManager(); }); tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { OpenDetails(); if (DateTimeOffset.UtcNow - lastSuccess > TimeSpan.FromSeconds(5)) { var ignored = RefreshQuota(); } } };
            floating = new FloatingForm(settings, OpenDetails, delegate { var ignored = RefreshQuota(); }, OpenSettings, ExitManager, OpenNews);
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!quitting && !preview && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            timer = new System.Windows.Forms.Timer { Interval = 1000 }; timer.Tick += delegate { UpdateVisuals(); if (!busy && DateTimeOffset.UtcNow >= nextRefresh) { var ignored = RefreshQuota(); } if (!newsBusy && settings.TiboNews && DateTimeOffset.UtcNow >= nextNews) { var ignored = RefreshNews(); } };
            if (!preview) {
                LoadLocalState(); newsState.Load(); SystemEvents.PowerModeChanged += OnPower; SystemEvents.SessionEnding += OnSessionEnding;
                System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += OnNetwork;
                Shown += async delegate { if (background || settings.Floating) Hide(); SetFloating(); timer.Start(); var ignored = RefreshNews(); await RefreshQuota(); };
            }
            newsState.Enabled = settings.TiboNews; PublishNews();
            UpdateVisuals();
        }
        void LoadLocalState() {
            try { var d = Json.Read(File.ReadAllText(Path.Combine(Settings.DataDir, "last-success.json"), Encoding.UTF8)); DateTimeOffset parsed; if (DateTimeOffset.TryParse(Json.Str(d, "fetchedAt"), out parsed)) lastSuccess = parsed; } catch { }
            try { foreach (var key in File.ReadAllLines(Path.Combine(Settings.DataDir, "notifications.txt"))) alerted.Add(key); } catch { }
        }
        void SaveSettings() { if (preview) return; try { settings.Save(); } catch { stateLabel.Text = "无法保存设置，请确认本地配置目录可写。"; } }
        void QueueRefresh() { if (IsDisposed || quitting || !IsHandleCreated) return; try { BeginInvoke((Action)(() => { nextRefresh = DateTimeOffset.MinValue; nextNews = DateTimeOffset.MinValue; })); } catch { } }
        void OnPower(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) QueueRefresh(); }
        void OnNetwork(object sender, System.Net.NetworkInformation.NetworkAvailabilityEventArgs e) { if (e.IsAvailable) QueueRefresh(); }
        void OnSessionEnding(object sender, SessionEndingEventArgs e) { if (!IsDisposed) try { Invoke((Action)ExitManager); } catch { } }
        bool IsFresh { get { return healthy && snapshot != null && DateTimeOffset.UtcNow - snapshot.FetchedAt <= TimeSpan.FromSeconds(Math.Max(150, settings.RefreshSeconds * 2 + 30)); } }
        async Task RefreshNews() {
            if (newsBusy || quitting || preview || !settings.TiboNews) return;
            string requestedSource = settings.TiboFeedUrl; bool changed = false;
            newsBusy = true; newsState.Busy = true; newsState.LastAttempt = DateTimeOffset.UtcNow; PublishNews();
            try {
                var result = await newsClient.Fetch(requestedSource, stop.Token);
                if (quitting) return;
                if (!settings.TiboNews || requestedSource != settings.TiboFeedUrl) { changed = true; return; }
                newsState.Apply(result); try { newsState.Save(); } catch { }
            } catch (Exception) { if (!quitting) { newsState.Healthy = false; newsState.Error = "公开来源获取失败，无法确认新消息"; } }
            finally { newsBusy = false; newsState.Busy = false; nextNews = changed ? DateTimeOffset.MinValue : DateTimeOffset.UtcNow.AddMinutes(5); if (!quitting) PublishNews(); }
        }
        void CheckNews() { var ignored = RefreshNews(); }
        void PublishNews() {
            newsState.Enabled = settings.TiboNews;
            if (floating != null) floating.SetNews(newsState, settings.LocalTime, OpenPost, CheckNews, OpenNews);
            if (newsWindowSection != null && !newsWindowSection.IsDisposed) newsWindowSection.Bind(newsState, settings.LocalTime, OpenPost, CheckNews, OpenProfile, true);
        }
        void OpenPost(NewsItem item) {
            if (preview) return;
            try {
                if (!NewsNavigation.Open(item, newsState, delegate(string url) { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return true; })) throw new InvalidOperationException("原帖链接无效或浏览器未能打开。");
                try { newsState.Save(); } catch { }
                // Rebuild link controls after their current click event completes.
                BeginInvoke((Action)PublishNews);
            } catch { MessageBox.Show("无法打开默认浏览器，请检查 Windows 的默认浏览器设置。", "打开 X 原帖", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
        void OpenProfile() { if (preview) return; try { Process.Start(new ProcessStartInfo(NewsParser.Profile) { UseShellExecute = true }); } catch { MessageBox.Show("无法打开默认浏览器。", "Tibo 的 X 主页"); } }
        void OpenNews() {
            if (quitting || newsWindowSection != null) return;
            floating.HideHover();
            using (var window = new Form { Text = "Tibo · 重置 / 赠送消息", BackColor = Theme.Card, ForeColor = Theme.Text, Font = Font, AutoScaleMode = AutoScaleMode.Dpi, ClientSize = new Size(620, 650), StartPosition = FormStartPosition.CenterScreen, MinimizeBox = false, MaximizeBox = false }) {
                var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
                newsWindowSection = new NewsSection { Location = new Point(20, 20), Width = 556 };
                scroll.Controls.Add(newsWindowSection); window.Controls.Add(scroll); PublishNews();
                try { window.ShowDialog(this); } finally { newsWindowSection = null; }
            }
        }
        async Task RefreshQuota() {
            if (busy || quitting || preview) return;
            busy = true; refresh.Enabled = false; refresh.Text = "查询中…"; UpdateVisuals();
            try {
                detected = Locator.Find(settings.CodexPath);
                var value = await Task.Run(() => client.Fetch(detected, stop.Token));
                if (quitting) return;
                snapshot = value; lastSuccess = value.FetchedAt; healthy = true; expiryQueued = false; error = value.Windows.Count == 0 ? "账号未返回可显示的额度窗口" : "";
                try { Settings.WriteLocal("last-success.json", Json.Write(new { fetchedAt = value.FetchedAt.ToString("o"), windows = value.Windows.Select(w => new { bucket = w.BucketId, slot = w.Slot, usedPercent = w.Used, windowDurationMins = w.Minutes, resetsAt = w.Reset.ToUnixTimeSeconds() }).ToArray() })); } catch { }
                UpdatePicker(); RebuildCards(); MaybeNotify();
            } catch (Exception ex) {
                if (quitting) return;
                healthy = false; error = ex is TimeoutException || ex is InvalidOperationException || ex is FileNotFoundException ? ex.Message : "无法读取额度，请确认本机 Codex 已登录、网络可用后重试。";
                RebuildCards();
            } finally {
                busy = false; nextRefresh = DateTimeOffset.UtcNow.AddSeconds(settings.RefreshSeconds);
                if (!quitting) { refresh.Text = "立即刷新"; refresh.Enabled = true; UpdateVisuals(); }
            }
        }
        void MaybeNotify() {
            if (!settings.LowNotify || snapshot == null || !IsFresh) return;
            foreach (var w in snapshot.Windows.Where(w => !w.Expired(DateTimeOffset.UtcNow) && w.Remaining <= settings.LowThreshold)) {
                string raw = snapshot.Account + "|" + w.Key + "|" + w.Reset.ToUnixTimeSeconds(); string key;
                using (var sha = SHA256.Create()) key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw))).Replace("-", "");
                if (alerted.Contains(key)) continue;
                alerted.Add(key);
                tray.ShowBalloonTip(6000, "Codex 额度偏低", w.BucketName + " · " + w.DurationLabel + "剩余 " + Math.Floor(w.Remaining) + "%\n" + w.Countdown(DateTimeOffset.UtcNow) + "后重置", ToolTipIcon.Warning);
            }
            if (alerted.Count > 512) { alerted.Clear(); }
            try { Settings.WriteLocal("notifications.txt", string.Join(Environment.NewLine, alerted)); } catch { }
        }
        void UpdatePicker() {
            pickerUpdating = true; windowPicker.Items.Clear(); pickerKeys.Clear(); pickerKeys.Add("auto"); windowPicker.Items.Add("自动 · Codex 桶内剩余较少的有效周期");
            if (snapshot != null) foreach (var w in snapshot.Windows) { pickerKeys.Add(w.Key); windowPicker.Items.Add(w.BucketName + " · " + w.DurationLabel); }
            int index = pickerKeys.IndexOf(settings.SelectedWindow); windowPicker.SelectedIndex = index >= 0 ? index : 0;
            // Keep a missing explicit choice until it returns; don't silently switch buckets.
            if (index < 0 && settings.SelectedWindow != "auto") { pickerKeys.Add(settings.SelectedWindow); windowPicker.Items.Add("所选周期当前不可用 · " + settings.SelectedWindow); windowPicker.SelectedIndex = windowPicker.Items.Count - 1; }
            pickerUpdating = false;
        }
        void RebuildCards() {
            if (cards == null) return;
            cards.SuspendLayout(); while (cards.Controls.Count > 0) { var c = cards.Controls[0]; cards.Controls.Remove(c); c.Dispose(); }
            var selected = snapshot == null ? null : snapshot.Select(settings.SelectedWindow, DateTimeOffset.UtcNow);
            if (snapshot != null) foreach (var w in snapshot.Windows) {
                var c = new WindowCard { Window = w, Fresh = IsFresh, LocalTime = settings.LocalTime, Selected = selected == w, Width = Math.Max(100, cards.ClientSize.Width - 22), Height = (int)(111 * DeviceDpi / 96f), Margin = new Padding(0, 0, 0, 12), Font = Font };
                c.Click += delegate { settings.SelectedWindow = w.Key; SaveSettings(); UpdatePicker(); RebuildCards(); UpdateVisuals(); }; cards.Controls.Add(c);
            }
            cards.ResumeLayout();
        }
        void ResizeCards() { foreach (Control c in cards.Controls) c.Width = Math.Max(100, cards.ClientSize.Width - 22); }
        void UpdateVisuals() {
            if (quitting) return;
            if (newsState.Prune(DateTimeOffset.UtcNow)) { PublishNews(); if (!preview) try { newsState.Save(); } catch { } }
            bool fresh = IsFresh; var selected = snapshot == null ? null : snapshot.Select(settings.SelectedWindow, DateTimeOffset.UtcNow); var displayed = fresh ? selected : null;
            if (snapshot != null && snapshot.Windows.Any(w => w.Expired(DateTimeOffset.UtcNow)) && !expiryQueued) { expiryQueued = true; nextRefresh = DateTimeOffset.MinValue; }
            string status = !healthy ? error : !fresh ? "数据已过期 · 等待刷新" : selected == null ? "暂无有效额度窗口 · 等待确认" : "数据正常 · " + selected.Countdown(DateTimeOffset.UtcNow) + "后重置";
            if (preview) status = "界面示例 · 非账号数据";
            hero.Fresh = fresh; hero.Window = selected; hero.Message = busy ? "正在查询账号额度…" : "当前额度未知 / 待更新"; hero.Invalidate();
            accountLabel.Text = snapshot == null || !fresh ? "本机账号 · " + (busy ? "正在检测" : "当前状态待确认") : Clock.Mask(snapshot.Account) + "  ·  " + snapshot.Plan.ToUpperInvariant();
            stateLabel.Text = status; stateLabel.ForeColor = displayed == null ? Theme.Amber : Theme.Green;
            updatedLabel.Text = lastSuccess == default(DateTimeOffset) ? "尚无成功刷新记录" : "上次成功：" + Clock.InZone(lastSuccess, settings.LocalTime).ToString("yyyy-MM-dd HH:mm:ss") + "  ·  " + Clock.Zone(settings.LocalTime);
            sourceLabel.Text = busy ? "正在联网查询 · 不创建对话" : "来源：本机 Codex 账号额度 · " + settings.RefreshSeconds + " 秒刷新";
            foreach (WindowCard c in cards.Controls) { c.Fresh = fresh; c.LocalTime = settings.LocalTime; c.Selected = displayed == c.Window; c.Invalidate(); }
            string signature = displayed == null ? "unknown" : Math.Floor(displayed.Remaining).ToString(); signature += "|" + settings.NumberIcon + "|" + (displayed == null);
            if (signature != iconSignature) { iconSignature = signature; var icon = BatteryIcons.Make(displayed == null ? (double?)null : displayed.Remaining, settings.NumberIcon, displayed == null); tray.Icon = icon; if (trayImage != null) trayImage.Dispose(); trayImage = icon; }
            string tip = displayed == null ? "Codex · 当前额度未知 / 待更新" : "Codex · " + Math.Floor(displayed.Remaining) + "% 剩余\n" + displayed.DurationLabel + " · " + displayed.Countdown(DateTimeOffset.UtcNow) + "后重置";
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            if (floating != null) floating.UpdateQuota(displayed, new HoverQuotaState { Snapshot = snapshot, Fresh = fresh, Busy = busy, LocalTime = settings.LocalTime, Status = status });
            if (floating != null) floating.UpdateNewsBadge(newsState.HasBadge(DateTimeOffset.UtcNow));
        }
        void SetFloating() { if (settings.Floating) floating.Show(); else floating.Hide(); }
        void OpenSettings() {
            if (quitting) return;
            floating.HideHover();
            using (var f = new SettingsForm(settings, detected)) {
                // A hidden/minimized main window cannot provide a useful owner for a tray-launched dialog.
                Form owner = Visible && WindowState != FormWindowState.Minimized ? this : floating.Visible ? (Form)floating : null;
                if (owner != this) { f.ShowInTaskbar = true; f.StartPosition = FormStartPosition.CenterScreen; }
                f.Shown += delegate { f.Activate(); };
                var result = owner == null ? f.ShowDialog() : f.ShowDialog(owner);
                if (result == DialogResult.OK) { SetFloating(); RebuildCards(); UpdateVisuals(); nextRefresh = DateTimeOffset.MinValue; nextNews = DateTimeOffset.MinValue; PublishNews(); }
            }
        }
        public void OpenDetails() { if (quitting) return; Show(); if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); if (!preview && DateTimeOffset.UtcNow - lastSuccess > TimeSpan.FromSeconds(5)) nextRefresh = DateTimeOffset.MinValue; }
        public void ExitManager() { if (quitting) return; quitting = true; timer.Stop(); stop.Cancel(); client.Stop(); tray.Visible = false; floating.Close(); Close(); }
        protected override void Dispose(bool disposing) {
            if (disposing && !resourcesDisposed) {
                resourcesDisposed = true;
                quitting = true; timer.Stop(); stop.Cancel(); client.Stop();
                if (!preview) { SystemEvents.PowerModeChanged -= OnPower; SystemEvents.SessionEnding -= OnSessionEnding; System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= OnNetwork; }
                tray.Dispose(); if (trayImage != null) trayImage.Dispose(); floating.Dispose(); timer.Dispose(); stop.Dispose();
            }
            base.Dispose(disposing);
        }
        public void RenderPreview(string file) {
            var now = DateTimeOffset.UtcNow; snapshot = new Snapshot { Account = "demo@example.invalid", Plan = "Pro", FetchedAt = now, ResetCards = 2, ResetCardExpiryKnown = true, ResetCardExpiresAt = now.AddDays(2).AddHours(11) };
            snapshot.Windows.Add(new QuotaWindow { BucketId = "codex", BucketName = "Codex", Slot = "primary", Used = 38, Minutes = 10080, Reset = now.AddDays(3).AddHours(6) });
            healthy = true; lastSuccess = now; UpdatePicker(); RebuildCards(); UpdateVisuals();
            var sample = NewsParser.Make(NewsParser.Profile + "/status/2107913674593644711", "[界面示例，不代表实时公告] banked reset / 赠送重置卡，请点原帖确认。", now.AddHours(-1));
            newsState.Apply(new NewsFeed { Items = new List<NewsItem> { sample }, Source = "preview", Name = "界面示例", CheckedAt = now, LatestPost = now }); newsState.LastAttempt = now; PublishNews();
            Show(); Application.DoEvents(); using (var bmp = new Bitmap(Width, Height)) { DrawToBitmap(bmp, new Rectangle(Point.Empty, Size)); bmp.Save(file); } Hide();
            var check = floating.RenderAndCheck(Path.GetDirectoryName(file)); File.WriteAllText(Path.Combine(Path.GetDirectoryName(file), "floating-ui-check.json"), Json.Write(check), new UTF8Encoding(false));
            using (var f = new SettingsForm(settings, "自动检测本机 Codex")) { f.Show(); Application.DoEvents(); using (var bmp = new Bitmap(f.Width, f.Height)) { f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size)); bmp.Save(Path.Combine(Path.GetDirectoryName(file), "settings-preview.png")); } f.Close(); }
        }
        public async Task<object> SmokeTest(string folder) {
            // Exercise our own window lifecycle without touching other applications.
            Show(); await RefreshQuota(); if (newsBusy) { while (newsBusy) await Task.Delay(100); } else await RefreshNews();
            bool fetched = IsFresh && snapshot != null && snapshot.Windows.Count > 0;
            string status = stateLabel.Text;
            using (var bitmap = new Bitmap(Width, Height)) { DrawToBitmap(bitmap, new Rectangle(Point.Empty, Size)); bitmap.Save(Path.Combine(folder, "live-ui.png")); }
            var hover = floating.RenderAndCheck(folder);
            Close(); Application.DoEvents(); bool hidden = !Visible && !IsDisposed && tray.Visible;
            OpenDetails(); Application.DoEvents(); bool reopened = Visible;
            int helper = client.LastProcessId;
            ExitManager();
            bool alive; try { using (var p = Process.GetProcessById(helper)) alive = !p.HasExited; } catch { alive = false; }
            return new { success = fetched && hidden && reopened && !alive, liveQuota = fetched, resetCards = snapshot == null ? (int?)null : snapshot.ResetCards, resetCardExpiryKnown = snapshot != null && snapshot.ResetCardExpiryKnown, earliestResetCardExpiry = snapshot == null || !snapshot.ResetCardExpiresAt.HasValue ? null : Clock.InZone(snapshot.ResetCardExpiresAt.Value, settings.LocalTime).ToString("yyyy-MM-dd HH:mm:ss zzz"), closedToTray = hidden, reopened = reopened, helperExited = !alive, status = status, floating = hover, news = new { fetched = newsState.Healthy, items = newsState.Feed.Items.Count, source = newsState.Feed.Source, stale = newsState.Feed.Stale(DateTimeOffset.UtcNow), badge = newsState.HasBadge(DateTimeOffset.UtcNow) } };
        }
        internal bool busyForTest() { return busy; }
    }
    public static class Program {
        [STAThread] public static int Main(string[] args) {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--render-preview") { using (var f = new MainForm(false, true)) f.RenderPreview(Path.GetFullPath(args[1])); return 0; }
            if (args.Length == 2 && args[0] == "--verify-live") return Verify(args[1]);
            if (args.Length == 2 && args[0] == "--smoke-ui") {
                string folder = Path.GetFullPath(args[1]); Directory.CreateDirectory(folder); int code = 1;
                var main = new MainForm(false, false);
                // Shown starts the ordinary refresh; allow it to finish before the lifecycle test.
                main.Shown += async delegate {
                    await Task.Delay(150); while (main.busyForTest()) await Task.Delay(100);
                    try { var result = await main.SmokeTest(folder); File.WriteAllText(Path.Combine(folder, "ui-smoke.json"), Json.Write(result), new UTF8Encoding(false)); code = (bool)Json.Get(Json.Read(Json.Write(result)), "success") ? 0 : 1; }
                    catch (Exception ex) { File.WriteAllText(Path.Combine(folder, "ui-smoke.json"), Json.Write(new { success = false, error = ex.Message })); main.ExitManager(); }
                };
                Application.Run(main); main.Dispose(); return code;
            }
            bool created; using (var mutex = new Mutex(true, "Local\\CodexQuotaManager_" + Environment.UserName, out created)) using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\CodexQuotaManager_Show_" + Environment.UserName)) {
                if (!created) { signal.Set(); return 0; }
                try {
                    using (var main = new MainForm(args.Contains("--background"), false)) {
                        var ready = main.Handle;
                        var wait = ThreadPool.RegisterWaitForSingleObject(signal, delegate { if (!main.IsDisposed) try { main.BeginInvoke((Action)main.OpenDetails); } catch { } }, null, -1, false);
                        try { Application.Run(main); } finally { wait.Unregister(null); }
                    }
                } catch (Exception ex) { MessageBox.Show("启动失败：" + ex.Message, "Codex 额度电量管家", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
            }
            return 0;
        }
        static int Verify(string output) {
            var client = new QuotaClient(); var result = new Dictionary<string, object>(); int code = 0;
            try {
                string exe = Locator.Find(Settings.Load().CodexPath);
                var s = client.Fetch(exe, CancellationToken.None).GetAwaiter().GetResult();
                result["success"] = true; result["plan"] = s.Plan; result["windowCount"] = s.Windows.Count;
                result["resetCards"] = s.ResetCards;
                result["windows"] = s.Windows.Select(w => new { bucket = w.BucketId, durationMinutes = w.Minutes, remainingPercent = w.Remaining, resetsAt = w.Reset.ToUnixTimeSeconds() }).ToArray();
                result["codexPath"] = exe;
            } catch (Exception ex) { result["success"] = false; result["error"] = ex.Message; code = 1; }
            finally { client.Stop(); }
            bool alive; try { using (var p = Process.GetProcessById(client.LastProcessId)) alive = !p.HasExited; } catch { alive = false; }
            result["helperExited"] = !alive;
            File.WriteAllText(Path.GetFullPath(output), Json.Write(result), new UTF8Encoding(false)); return code;
        }
    }
}
