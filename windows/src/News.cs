using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace CodexQuotaManager {
    public static class NewsNavigation {
        public static bool Open(NewsItem item, NewsStore store, Func<string, bool> browser) {
            string url = NewsParser.CanonicalPost(item.Url);
            if (url == null || !browser(url)) return false;
            store.MarkOpened(item); return true;
        }
    }
    public class NewsItem {
        public string Id = "", Url = "", Text = "", Kind = "", Version = "";
        public DateTimeOffset Published;
        public bool Recent(DateTimeOffset now) { return Published >= now.AddDays(-3) && Published <= now.AddMinutes(5); }
        public string Summary {
            get {
                string sentence = Regex.Split(Text, @"(?<=[.!?。！？])\s+").FirstOrDefault(s => NewsParser.CardMention(s)) ?? Text;
                return sentence.Length > 65 ? sentence.Substring(0, 65) + "…" : sentence;
            }
        }
        public string Caption { get { return Kind == "card" ? "重置卡 / 赠送相关 · 待核实" : "额度重置相关 · 待核实"; } }
    }
    public class NewsFeed {
        public List<NewsItem> Items = new List<NewsItem>();
        public string Source = "", Name = "";
        public DateTimeOffset CheckedAt, LatestPost, SourceUpdated;
        public bool Stale(DateTimeOffset now) { return LatestPost == default(DateTimeOffset) || LatestPost < now.AddHours(-48); }
    }
    public static class NewsParser {
        public const string Profile = "https://x.com/thsottiaux";
        public static string CanonicalPost(string link) {
            Uri uri; if (!Uri.TryCreate(link, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 || !uri.IsDefaultPort) return null;
            string host = uri.Host.ToLowerInvariant();
            if (host != "x.com" && host != "www.x.com" && host != "twitter.com" && host != "www.twitter.com" && host != "mobile.twitter.com") return null;
            var m = Regex.Match(uri.AbsolutePath, @"^/thsottiaux/status/([0-9]{10,25})(?:/)?$", RegexOptions.IgnoreCase);
            return m.Success ? Profile + "/status/" + m.Groups[1].Value : null;
        }
        public static string Plain(string html) {
            html = Regex.Replace(html ?? "", @"<(script|style)\b[^>]*>.*?</\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            html = Regex.Replace(html, @"<(br\b[^>]*|/p|/div)>", "\n", RegexOptions.IgnoreCase);
            html = Regex.Replace(html, "<[^>]+>", " ");
            return Regex.Replace(WebUtility.HtmlDecode(html), @"\s+", " ").Trim();
        }
        public static bool CardMention(string text) {
            return Regex.IsMatch(text, @"\bbank(?:ed|ing)?\s+(?:(?:a|an|the|full|rate[- ]limit)\s+){0,3}resets?\b|\bresets?\s+(?:(?:in\s+(?:your|the)\s+)?bank|cards?|credits?)\b|\bearned\s+resets?\b|重置(?:卡|券|次数)|(?:存入|领取|兑换).{0,8}重置", RegexOptions.IgnoreCase);
        }
        public static string Kind(string text) {
            if (!Regex.IsMatch(text, @"\breset(?:s|ting)?\b|重置", RegexOptions.IgnoreCase)) return null;
            if (CardMention(text)) return "card";
            return "reset";
        }
        static DateTimeOffset Date(string text) {
            DateTimeOffset date;
            return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out date) ? date : default(DateTimeOffset);
        }
        public static NewsItem Make(string link, string text, DateTimeOffset published) {
            string url = CanonicalPost(link), plain = Plain(text), kind = Kind(plain);
            if (url == null || kind == null || published == default(DateTimeOffset)) return null;
            if (plain.Length > 5000) plain = plain.Substring(0, 5000);
            string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url + "|" + published.ToUnixTimeSeconds() + "|" + plain))).Replace("-", "");
            return new NewsItem { Id = url.Substring(url.LastIndexOf('/') + 1), Url = url, Text = plain, Kind = kind, Published = published, Version = hash };
        }
        static string Child(XElement node, string name) { var child = node.Elements().FirstOrDefault(e => e.Name.LocalName == name); return child == null ? "" : child.Value; }
        public static NewsFeed ParseRss(string xml, string source, DateTimeOffset now) {
            XDocument doc;
            using (var input = new StringReader(xml)) using (var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2000000 })) doc = XDocument.Load(reader);
            var channel = doc.Root == null ? null : doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "channel");
            if (channel == null) throw new InvalidDataException("消息源不是有效 RSS。");
            bool identity = channel.Elements().Where(e => e.Name.LocalName == "link").Any(e => e.Value.TrimEnd('/').Equals(Profile, StringComparison.OrdinalIgnoreCase) || e.Value.TrimEnd('/').Equals("https://twitter.com/thsottiaux", StringComparison.OrdinalIgnoreCase));
            if (!identity) throw new InvalidDataException("消息源账号不是 @thsottiaux。");
            var feed = new NewsFeed { Source = source, Name = "公开 RSS 转发源", CheckedAt = now, SourceUpdated = Date(Child(channel, "lastBuildDate")) };
            foreach (var item in channel.Elements().Where(e => e.Name.LocalName == "item").Take(500)) {
                string link = CanonicalPost(Child(item, "link")); if (link == null) continue;
                var date = Date(Child(item, "pubDate")); if (date > now.AddMinutes(5)) continue;
                if (date > feed.LatestPost) feed.LatestPost = date;
                string body = Child(item, "description"); if (body.Length == 0) body = Child(item, "title");
                var post = Make(link, body, date); if (post != null) feed.Items.Add(post);
            }
            feed.Items = feed.Items.GroupBy(i => i.Id).Select(g => g.First()).OrderByDescending(i => i.Published).Take(30).ToList(); return feed;
        }
        public static NewsFeed ParseIndex(string json, string source, DateTimeOffset now) {
            var root = Json.Read(json); if (Json.Str(root, "username") != "thsottiaux") throw new InvalidDataException("索引账号不是 @thsottiaux。");
            var feed = new NewsFeed { Source = source, Name = "公开监控索引（第三方）", CheckedAt = now, SourceUpdated = Date(Json.Str(root, "updatedAt")) };
            var latest = Json.Get(root, "latestObservedPost");
            if (CanonicalPost(Json.Str(latest, "url")) != null) { var d = Date(Json.Str(latest, "postCreatedAt")); if (d <= now.AddMinutes(5)) feed.LatestPost = d; }
            var rows = new List<object>(); var recent = Json.Get(root, "recent") as IEnumerable; if (recent != null) foreach (var r in recent) rows.Add(r);
            var pinned = Json.Obj(Json.Get(root, "latest")); if (pinned != null) rows.AddRange(pinned.Values);
            foreach (var row in rows.Take(500)) {
                var d = Date(Json.Str(row, "postCreatedAt")); if (d > now.AddMinutes(5)) continue;
                var item = Make(Json.Str(row, "url"), Json.Str(row, "text"), d); if (item == null) continue;
                if (item.Published > feed.LatestPost) feed.LatestPost = item.Published;
                // Treat upstream event classifications as hints, never account entitlements.
                feed.Items.Add(item);
            }
            feed.Items = feed.Items.GroupBy(i => i.Id).Select(g => g.First()).OrderByDescending(i => i.Published).Take(30).ToList(); return feed;
        }
    }
    public class NewsClient {
        static NewsClient() { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12; }
        public const string DefaultRss = "https://rsshub.stsecurity.moe/twitter/user/thsottiaux";
        public const string FallbackIndex = "https://raw.githubusercontent.com/AllenXiao95/codex-reset-signal/monitor-state/status.json";
        public string Diagnostic = "";
        public async Task<NewsFeed> Fetch(string customRss, CancellationToken cancel) {
            Diagnostic = "";
            string source = string.IsNullOrWhiteSpace(customRss) ? DefaultRss : customRss;
            var best = await TryFeed(source, false, cancel).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(customRss) && (best == null || best.Stale(DateTimeOffset.UtcNow))) {
                var fallback = await TryFeed(FallbackIndex, true, cancel).ConfigureAwait(false);
                if (fallback != null && (best == null || fallback.LatestPost > best.LatestPost)) best = fallback;
            }
            cancel.ThrowIfCancellationRequested();
            if (best == null) throw new IOException("公开来源连接或解析失败，无法确认是否有新消息。");
            return best;
        }
        async Task<NewsFeed> TryFeed(string url, bool index, CancellationToken cancel) {
            try {
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
                deadline.CancelAfter(TimeSpan.FromSeconds(20));
                Uri uri; if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0) throw new InvalidDataException("仅支持无账号信息的 HTTPS RSS 地址。");
                using (var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate, UseCookies = false, UseDefaultCredentials = false })
                using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(18) }) {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("CodexBattery/" + BuildInfo.Version);
                    using (var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                    using (var abort = deadline.Token.Register(delegate { response.Dispose(); })) {
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > 2000000) throw new InvalidDataException("消息源过大。");
                        using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false)) using (var output = new MemoryStream()) {
                            byte[] buffer = new byte[8192]; int count;
                            while ((count = await input.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) > 0) {
                                output.Write(buffer, 0, count); if (output.Length > 2000000) throw new InvalidDataException("消息源过大。");
                            }
                            string text = Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF');
                            return index ? NewsParser.ParseIndex(text, url, DateTimeOffset.UtcNow) : NewsParser.ParseRss(text, url, DateTimeOffset.UtcNow);
                        }
                    }
                }
                }
            } catch (OperationCanceledException) { cancel.ThrowIfCancellationRequested(); Diagnostic += "Timeout; "; return null; } catch (Exception ex) { Diagnostic += ex.GetType().Name + ": " + ex.GetBaseException().Message + "; "; return null; }
        }
    }
    public class NewsStore {
        public NewsFeed Feed = new NewsFeed();
        public bool Healthy, Busy, Enabled = true;
        public string Error = "尚未检查";
        public Dictionary<string, string> OpenedVersions = new Dictionary<string, string>();
        public DateTimeOffset LastAttempt;
        public List<NewsItem> VisibleItems(DateTimeOffset now) { return Feed.Items.Where(i => i.Kind == "card" && i.Recent(now)).OrderByDescending(i => i.Published).ToList(); }
        public bool Prune(DateTimeOffset now) { int old = Feed.Items.Count; Feed.Items = VisibleItems(now); return old != Feed.Items.Count; }
        public bool Unread(NewsItem item, DateTimeOffset now) { string v; return item.Recent(now) && (!OpenedVersions.TryGetValue(item.Id, out v) || v != item.Version); }
        public bool HasBadge(DateTimeOffset now) { return Enabled && Healthy && !Feed.Stale(now) && VisibleItems(now).Any(i => Unread(i, now)); }
        public string Status(bool local) {
            if (!Enabled) return "消息检查已关闭";
            if (Busy) return "正在检查公开来源…";
            if (!Healthy) return Error + "（历史消息仍可打开）";
            string prefix = Feed.Stale(DateTimeOffset.UtcNow) ? "来源内容较旧，需打开 X 确认" : "获取成功 · 完整性未核实";
            if (Feed.LatestPost != default(DateTimeOffset)) prefix += "\n来源最近帖子：" + Clock.InZone(Feed.LatestPost, local).ToString("yyyy-MM-dd HH:mm");
            return prefix;
        }
        public void Apply(NewsFeed incoming) {
            var currentIds = new HashSet<string>(incoming.Items.Select(i => i.Id));
            // Missing from a limited timeline does not prove that a post was withdrawn.
            incoming.Items = incoming.Items.Concat(Feed.Items.Where(i => !currentIds.Contains(i.Id))).OrderByDescending(i => i.Published).Take(30).ToList();
            Feed = incoming; Prune(DateTimeOffset.UtcNow); Healthy = true; Error = "";
        }
        public void MarkOpened(NewsItem item) { OpenedVersions[item.Id] = item.Version; }
        public void Load() {
            try {
                var data = Json.Read(File.ReadAllText(Path.Combine(Settings.DataDir, "tibo-news.json"), Encoding.UTF8));
                var seen = Json.Obj(Json.Get(data, "opened")); if (seen != null) foreach (var p in seen) OpenedVersions[p.Key] = Convert.ToString(p.Value);
                var rows = Json.Get(data, "items") as IEnumerable;
                if (rows != null) foreach (var r in rows) {
                    DateTimeOffset date; if (!DateTimeOffset.TryParse(Json.Str(r, "published"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out date)) continue;
                    var item = NewsParser.Make(Json.Str(r, "url"), Json.Str(r, "text"), date); if (item != null) Feed.Items.Add(item);
                }
                // Disk contents remain history until a real network fetch succeeds.
            } catch { }
            Prune(DateTimeOffset.UtcNow);
        }
        public void Save() {
            Prune(DateTimeOffset.UtcNow);
            var ids = new HashSet<string>(Feed.Items.Select(i => i.Id));
            OpenedVersions = OpenedVersions.Where(p => ids.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            Settings.WriteLocal("tibo-news.json", Json.Write(new { opened = OpenedVersions, items = Feed.Items.Select(i => new { url = i.Url, text = i.Text, published = i.Published.ToString("o") }).ToArray() }));
        }
    }
}
