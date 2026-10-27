import XCTest
@testable import BatteryCore

final class NewsTests: XCTestCase {
    let post = NewsParser.profile + "/status/2107913674593644711"
    let now = Date(timeIntervalSince1970: 1_800_000_000)
    func testURLAllowlist() {
        XCTAssertEqual(NewsParser.canonicalPost(post + "?s=20"), post)
        XCTAssertEqual(NewsParser.canonicalPost(post.replacingOccurrences(of: "x.com", with: "twitter.com")), post)
        for bad in [post.replacingOccurrences(of: "x.com", with: "x.com.evil.invalid"), post.replacingOccurrences(of: "thsottiaux", with: "other"), "javascript:alert(1)", post.replacingOccurrences(of: "https:", with: "http:"), post.replacingOccurrences(of: "x.com", with: "user@x.com")] { XCTAssertNil(NewsParser.canonicalPost(bad)) }
    }
    func testOnlyCardsAndOneLineSummary() {
        XCTAssertNil(NewsParser.make(url: post, text: "Free global reset for everyone", published: now))
        let item = NewsParser.make(url: post, text: "A new model. A banked reset is available. More details.", published: now)!
        XCTAssertEqual(item.summary, "A banked reset is available.")
        XCTAssertFalse(item.summary.contains("\n"))
        XCTAssertTrue(NewsParser.cardMention("Your reset credit expires tomorrow"))
        XCTAssertEqual(NewsParser.plain("<p>reset card &amp; details</p><script>\nignored\n</script>"), "reset card & details")
    }
    func testRolling72HoursAndVersionReadState() {
        let item = NewsParser.make(url: post, text: "A reset card", published: now.addingTimeInterval(-72 * 3600))!
        XCTAssertTrue(item.recent(at: now)); XCTAssertFalse(item.recent(at: now.addingTimeInterval(0.001)))
        let store = NewsStore(); var feed = NewsFeed(); feed.items = [item]; feed.latestPost = now
        store.apply(feed, now: now); XCTAssertTrue(store.badge(at: now))
        store.opened[item.id] = item.version; XCTAssertFalse(store.badge(at: now))
        let edited = NewsParser.make(url: post, text: "A reset card, updated timing", published: item.published)!
        feed.items = [edited]; store.apply(feed, now: now); XCTAssertTrue(store.badge(at: now))
        store.healthy = false; XCTAssertFalse(store.badge(at: now))
        store.prune(at: now.addingTimeInterval(1)); XCTAssertTrue(store.visible(at: now.addingTimeInterval(1)).isEmpty)
    }
    func testRSSDateAndAuthorValidation() throws {
        let xml = """
        <rss><channel><link>https://x.com/thsottiaux</link><lastBuildDate>Fri, 15 Jan 2027 08:00:00 +0000</lastBuildDate>
        <item><link>\(post)</link><description><![CDATA[Example reset card]]></description><pubDate>Mon, 01 Jan 2024 08:00:00 +0000</pubDate></item>
        <item><link>https://x.com/other/status/2107913674593644712</link><description>reset card</description><pubDate>Mon, 01 Jan 2024 08:00:00 +0000</pubDate></item>
        </channel></rss>
        """
        let feed = try NewsParser.rss(Data(xml.utf8), source: "test", now: now)
        XCTAssertEqual(feed.items.count, 1); XCTAssertTrue(feed.stale(at: now))
        XCTAssertThrowsError(try NewsParser.rss(Data(xml.replacingOccurrences(of: "<link>https://x.com/thsottiaux</link>", with: "<link>https://x.com/other</link>").utf8), source: "test", now: now))
        XCTAssertThrowsError(try NewsParser.rss(Data(("<!DOCTYPE rss [<!ENTITY x SYSTEM 'file:///etc/passwd'>]>" + xml).utf8), source: "test", now: now))
    }
    func testIndexAndCachePruning() throws {
        let root: [String: Any] = ["username": "thsottiaux", "updatedAt": "2027-01-15T08:00:00Z", "recent": [["url": post, "text": "An old reset card", "postCreatedAt": "2024-01-01T08:00:00Z"]]]
        let feed = try NewsParser.index(JSONSerialization.data(withJSONObject: root), source: "test", now: now)
        XCTAssertTrue(feed.stale(at: now)); XCTAssertEqual(feed.items.count, 1)
        let store = NewsStore(); store.apply(feed, now: now); XCTAssertTrue(store.feed.items.isEmpty)
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        let cache = folder.appendingPathComponent("news.json"); try store.save(to: cache, now: now)
        let restored = NewsStore(); restored.load(from: cache, now: now)
        XCTAssertFalse(restored.healthy); XCTAssertTrue(restored.feed.items.isEmpty)
    }
}
