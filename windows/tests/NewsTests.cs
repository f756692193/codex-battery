using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CodexQuotaManager;

class NewsTests {
    static int count;
    static readonly string Post = NewsParser.Profile + "/status/2107913674593644711";
    static void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
    static string Item(string body, DateTimeOffset date, string url) { return "<item><title>ignored</title><description><![CDATA[" + body + "]]></description><link>" + url + "</link><pubDate>" + date.ToString("r") + "</pubDate></item>"; }
    static string Feed(string items) { return "<rss><channel><title>Tibo</title><link>" + NewsParser.Profile + "</link><lastBuildDate>" + DateTimeOffset.UtcNow.ToString("r") + "</lastBuildDate>" + items + "</channel></rss>"; }
    static void Tests(string fixture) {
        var now = DateTimeOffset.UtcNow; var old = now.AddDays(-60);
        Check(NewsParser.CanonicalPost(Post + "?s=20") == Post, "normalize valid X link");
        Check(NewsParser.CanonicalPost(Post.Replace("x.com", "twitter.com")) == Post, "normalize legacy Twitter host");
        Check(NewsParser.CanonicalPost(Post.Replace("thsottiaux", "someone_else")) == null, "reject other authors");
        Check(NewsParser.CanonicalPost(Post.Replace("x.com", "x.com.evil.invalid")) == null, "reject lookalike host");
        Check(NewsParser.CanonicalPost("javascript:alert(1)") == null && NewsParser.CanonicalPost(Post.Replace("https", "http")) == null, "only HTTPS post links");
        Check(NewsParser.Kind("Loading a banked reset in paid accounts") == "card", "banked reset candidate");
        Check(NewsParser.Kind("Global reset by EOD") == "reset", "general reset is separate");
        Check(NewsParser.Kind("Free merch for developers") == null, "unrelated gifts excluded");
        Check(NewsParser.Kind("Free global reset for everyone") == "reset", "free global reset isn't a reset card");
        Check(NewsParser.Kind("Your reset credit expires tomorrow") == "card", "reset credit is card related");
        Check(NewsParser.Plain("<p>free &amp; full reset</p><script>alert(1)</script>") == "free & full reset", "strip markup without executing it");
        var feed = NewsParser.ParseRss(Feed(Item("old reset", old, Post) + Item("Free reset card", now.AddHours(-1), Post.Replace("4711", "4712"))), "test", now);
        Check(feed.Items.Count == 2 && feed.Items[0].Published > feed.Items[1].Published, "sort publication dates, not feed order");
        Check(!feed.Stale(now), "fresh underlying post recognized");
        feed = NewsParser.ParseRss(Feed(Item("old banked reset", old, Post)), "test", now);
        Check(feed.Stale(now), "fresh build date cannot conceal old posts");
        var store = new NewsStore(); store.Apply(feed);
        Check(!store.HasBadge(now), "historical reset cannot light new badge");
        var item = NewsParser.Make(Post, "free banked reset", now.AddHours(-1));
        store.Apply(new NewsFeed { LatestPost = now.AddHours(-1), Items = new List<NewsItem> { item } });
        Check(store.HasBadge(now), "recent unread post lights badge");
        string handedToBrowser = null;
        Check(NewsNavigation.Open(item, store, delegate(string url) { handedToBrowser = url; return true; }) && handedToBrowser == Post, "click hands original post URL to browser");
        Check(!store.HasBadge(now), "successful open clears only that version");
        var edited = NewsParser.Make(Post, "free banked reset — corrected date", item.Published);
        store.Apply(new NewsFeed { LatestPost = item.Published, Items = new List<NewsItem> { edited } });
        Check(store.HasBadge(now), "edited post becomes unread again");
        Check(!NewsNavigation.Open(edited, store, delegate { return false; }) && store.Unread(edited, now), "failed browser launch does not mark opened");
        bool called = false;
        Check(!NewsNavigation.Open(new NewsItem { Url = "https://evil.invalid" }, store, delegate { called = true; return true; }) && !called, "untrusted URL never reaches browser");
        store.Healthy = false; Check(!store.HasBadge(now) && store.Feed.Items.Count > 0, "fetch failure preserves history without false new badge");
        store.Healthy = true; store.Apply(new NewsFeed { LatestPost = now, Items = new List<NewsItem>() });
        Check(store.Feed.Items.Count > 0, "missing from timeline is not a withdrawal");
        var ordinary = NewsParser.Make(Post.Replace("4711", "4713"), "Global reset by EOD", now.AddHours(-1));
        var outside = NewsParser.Make(Post.Replace("4711", "4714"), "A banked reset", now.AddDays(-4));
        store.Apply(new NewsFeed { LatestPost = now, Items = new List<NewsItem> { ordinary, outside, edited } });
        Check(store.VisibleItems(now).All(i => i.Kind == "card") && !store.VisibleItems(now).Any(i => i.Id == ordinary.Id), "ordinary reset excluded from displayed messages");
        Check(!store.Feed.Items.Any(i => i.Id == outside.Id), "messages over 3 days removed from stored list");
        var boundary = NewsParser.Make(Post, "reset card", now.AddDays(-3));
        Check(boundary.Recent(now) && !boundary.Recent(now.AddTicks(1)), "rolling 72-hour boundary");
        var summary = NewsParser.Make(Post, "A model was released. A banked reset is available. More details follow.", now);
        Check(summary.Summary == "A banked reset is available.", "one-line excerpt selects card-related sentence");
        Check(!summary.Summary.Contains("\n") && !summary.Summary.Contains("\r"), "summary contains no newlines");
        bool bad = false; try { NewsParser.ParseRss("<html>login required</html>", "test", now); } catch { bad = true; }
        Check(bad, "HTML login page cannot count as checked feed");
        bad = false; try { NewsParser.ParseRss(Feed(Item("free reset", now, Post)).Replace("<link>" + NewsParser.Profile + "</link>", "<link>https://x.com/other</link>"), "test", now); } catch { bad = true; }
        Check(bad, "reject wrong feed account");
        bad = false; try { NewsParser.ParseRss("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///c:/windows/win.ini'>]>" + Feed("&e;"), "test", now); } catch { bad = true; }
        Check(bad, "DTD/external entities forbidden");
        feed = NewsParser.ParseRss(Feed(Item("reset", now.AddDays(2), Post)), "test", now);
        Check(feed.Items.Count == 0 && feed.Stale(now), "future publication doesn't become new signal");
        string index = Json.Write(new { username = "thsottiaux", updatedAt = now.ToString("o"), latestObservedPost = new { url = Post, postCreatedAt = old.ToString("o") }, recent = new[] { new { url = Post, text = "banked reset", postCreatedAt = old.ToString("o") } } });
        feed = NewsParser.ParseIndex(index, "index", now);
        Check(feed.Stale(now) && feed.Items.Count == 1, "index update time cannot conceal old source posts");
        if (!File.Exists(fixture)) throw new FileNotFoundException("Synthetic RSS fixture is required.", fixture);
        feed = NewsParser.ParseRss(File.ReadAllText(fixture), "synthetic-fixture", now);
        Check(feed.Items.Count == 2 && feed.Items.All(i => i.Url.StartsWith(NewsParser.Profile + "/status/")), "synthetic RSS fixture only keeps target author");
        Check(feed.Stale(now), "synthetic stale RSS correctly flagged");
        Console.WriteLine("All " + count + " news checks passed.");
    }
    static int Live(string output) {
        var client = new NewsClient();
        try {
            var f = client.Fetch("", CancellationToken.None).GetAwaiter().GetResult(); var store = new NewsStore(); store.Apply(f);
            File.WriteAllText(output, Json.Write(new { success = true, items = f.Items.Count, source = f.Source, latestPost = f.LatestPost.ToString("o"), stale = f.Stale(DateTimeOffset.UtcNow), badge = store.HasBadge(DateTimeOffset.UtcNow), sampleUrl = f.Items.Select(i => i.Url).FirstOrDefault() })); return 0;
        } catch (Exception ex) { File.WriteAllText(output, Json.Write(new { success = false, error = ex.Message, diagnostic = client.Diagnostic })); return 1; }
    }
    static int Main(string[] args) { if (args.Length == 2 && args[0] == "--live") return Live(args[1]); try { Tests(args.Length > 0 ? args[0] : ""); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; } }
}
