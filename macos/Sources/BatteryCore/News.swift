import Foundation
import FoundationXML
import CryptoKit

public struct NewsItem: Codable {
    public let url: String, text: String, published: Date, version: String
    public var id: String { url.components(separatedBy: "/").last ?? url }
    public func recent(at now: Date) -> Bool { published >= now.addingTimeInterval(-72 * 3600) && published <= now.addingTimeInterval(300) }
    public var summary: String {
        let sentences = text.replacingOccurrences(of: #"(?<=[.!?。！？])\s+"#, with: "\n", options: .regularExpression).components(separatedBy: "\n")
        let line = sentences.first(where: NewsParser.cardMention) ?? text
        return line.count > 65 ? String(line.prefix(65)) + "…" : line
    }
}

public struct NewsFeed {
    public var items: [NewsItem] = []
    public var latestPost: Date?, source = ""
    public func stale(at now: Date) -> Bool { latestPost == nil || latestPost! < now.addingTimeInterval(-48 * 3600) }
}

public enum NewsParser {
    public static let profile = "https://x.com/thsottiaux"
    public static func canonicalPost(_ text: String) -> String? {
        guard let url = URLComponents(string: text), url.scheme?.lowercased() == "https", url.user == nil, url.password == nil,
              url.port == nil || url.port == 443,
              ["x.com", "www.x.com", "twitter.com", "www.twitter.com", "mobile.twitter.com"].contains(url.host?.lowercased() ?? ""),
              url.path.range(of: #"^/thsottiaux/status/[0-9]{10,25}/?$"#, options: [.regularExpression, .caseInsensitive]) != nil else { return nil }
        return profile + "/status/" + String(url.path.split(separator: "/")[2])
    }
    public static func plain(_ html: String) -> String {
        var text = html.replacingOccurrences(of: #"(?is)<(script|style)\b[^>]*>.*?</\1>"#, with: " ", options: .regularExpression)
        text = text.replacingOccurrences(of: #"<[^>]+>"#, with: " ", options: .regularExpression)
        for (entity, value) in [("&lt;", "<"), ("&gt;", ">"), ("&quot;", "\""), ("&#39;", "'"), ("&apos;", "'"), ("&nbsp;", " "), ("&amp;", "&")] { text = text.replacingOccurrences(of: entity, with: value) }
        return text.replacingOccurrences(of: #"\s+"#, with: " ", options: .regularExpression).trimmingCharacters(in: .whitespacesAndNewlines)
    }
    public static func cardMention(_ text: String) -> Bool {
        text.range(of: #"\bbank(?:ed|ing)?\s+(?:(?:a|an|the|full|rate[- ]limit)\s+){0,3}resets?\b|\bresets?\s+(?:(?:in\s+(?:your|the)\s+)?bank|cards?|credits?)\b|\bearned\s+resets?\b|重置(?:卡|券|次数)|(?:存入|领取|兑换).{0,8}重置"#, options: [.regularExpression, .caseInsensitive]) != nil
    }
    public static func make(url: String, text: String, published: Date) -> NewsItem? {
        guard let link = canonicalPost(url) else { return nil }
        let body = String(plain(text).prefix(5000))
        guard cardMention(body) else { return nil }
        let raw = link + "|\(Int(published.timeIntervalSince1970))|" + body
        let hash = SHA256.hash(data: Data(raw.utf8)).map { String(format: "%02x", $0) }.joined()
        return NewsItem(url: link, text: body, published: published, version: hash)
    }
    public static func date(_ text: String) -> Date? {
        let iso = ISO8601DateFormatter()
        if let date = iso.date(from: text) { return date }
        iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = iso.date(from: text) { return date }
        let f = DateFormatter(); f.locale = Locale(identifier: "en_US_POSIX"); f.timeZone = TimeZone(secondsFromGMT: 0)
        for pattern in ["EEE, dd MMM yyyy HH:mm:ss Z", "EEE, d MMM yyyy HH:mm:ss Z", "yyyy-MM-dd HH:mm:ss Z"] {
            f.dateFormat = pattern; if let date = f.date(from: text) { return date }
        }
        return nil
    }
    public static func rss(_ data: Data, source: String, now: Date) throws -> NewsFeed {
        guard data.count <= 2_000_000, let xml = String(data: data, encoding: .utf8), xml.range(of: #"<!\s*(DOCTYPE|ENTITY)\b"#, options: [.regularExpression, .caseInsensitive]) == nil else { throw BatteryError("RSS 过大或包含不支持的声明。") }
        let reader = RSSReader(); let parser = XMLParser(data: data)
        parser.shouldResolveExternalEntities = false; parser.delegate = reader
        guard parser.parse(), reader.channel, [profile, "https://twitter.com/thsottiaux"].contains(reader.link.trimmingCharacters(in: .whitespacesAndNewlines).trimmingCharacters(in: CharacterSet(charactersIn: "/"))) else { throw BatteryError("RSS 无效或不是 @thsottiaux 的消息源。") }
        var feed = NewsFeed(); feed.source = source
        for row in reader.rows {
            guard canonicalPost(row["link"] ?? "") != nil, let date = date(row["pubDate"] ?? ""), date <= now.addingTimeInterval(300) else { continue }
            if date > (feed.latestPost ?? .distantPast) { feed.latestPost = date }
            let text = (row["description"] ?? "").isEmpty ? row["title"] ?? "" : row["description"] ?? ""
            if let item = make(url: row["link"] ?? "", text: text, published: date) { feed.items.append(item) }
        }
        feed.items = unique(feed.items); return feed
    }
    public static func index(_ data: Data, source: String, now: Date) throws -> NewsFeed {
        guard data.count <= 2_000_000, let root = try JSONSerialization.jsonObject(with: data) as? [String: Any], root["username"] as? String == "thsottiaux" else { throw BatteryError("公开索引无效或账号不匹配。") }
        var feed = NewsFeed(); feed.source = source
        if let latest = root["latestObservedPost"] as? [String: Any], canonicalPost(latest["url"] as? String ?? "") != nil,
           let date = date(latest["postCreatedAt"] as? String ?? ""), date <= now.addingTimeInterval(300) { feed.latestPost = date }
        var rows = root["recent"] as? [[String: Any]] ?? []
        rows += Array((root["latest"] as? [String: [String: Any]] ?? [:]).values)
        for row in rows.prefix(500) {
            guard let date = date(row["postCreatedAt"] as? String ?? ""), date <= now.addingTimeInterval(300),
                  let item = make(url: row["url"] as? String ?? "", text: row["text"] as? String ?? "", published: date) else { continue }
            feed.items.append(item)
            if date > (feed.latestPost ?? .distantPast) { feed.latestPost = date }
        }
        feed.items = unique(feed.items); return feed
    }
    static func unique(_ items: [NewsItem]) -> [NewsItem] {
        var ids = Set<String>(); return items.sorted { $0.published > $1.published }.filter { ids.insert($0.id).inserted }.prefix(30).map { $0 }
    }
}

private final class RSSReader: NSObject, XMLParserDelegate {
    var rows: [[String: String]] = [], link = "", channel = false
    private var path: [String] = [], fields: [String: String] = [:]
    func parser(_ parser: XMLParser, didStartElement name: String, namespaceURI: String?, qualifiedName: String?, attributes: [String: String]) {
        path.append(name)
        if path == ["rss", "channel"] { channel = true }
        if path == ["rss", "channel", "item"] { fields = [:] }
    }
    private func append(_ text: String) {
        if path == ["rss", "channel", "link"] { link += text }
        if path.count == 4 && path.prefix(3).elementsEqual(["rss", "channel", "item"]), let field = path.last { fields[field, default: ""] += text }
    }
    func parser(_ parser: XMLParser, foundCharacters string: String) { append(string) }
    func parser(_ parser: XMLParser, foundCDATA block: Data) { append(String(data: block, encoding: .utf8) ?? "") }
    func parser(_ parser: XMLParser, didEndElement name: String, namespaceURI: String?, qualifiedName: String?) {
        if path == ["rss", "channel", "item"] && rows.count < 500 { rows.append(fields) }
        if !path.isEmpty { path.removeLast() }
    }
}

private final class BoundedTransfer: NSObject, URLSessionDataDelegate {
    let done = DispatchSemaphore(value: 0)
    var data = Data(), error: Error?
    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse, completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        guard let http = response as? HTTPURLResponse, (200...299).contains(http.statusCode), response.expectedContentLength <= 2_000_000 else { error = BatteryError("公开来源响应无效或过大。"); completionHandler(.cancel); return }
        completionHandler(.allow)
    }
    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive chunk: Data) {
        if data.count + chunk.count > 2_000_000 { error = BatteryError("公开来源响应过大。"); dataTask.cancel(); return }
        data.append(chunk)
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) {
        guard request.url?.scheme == "https", request.url?.user == nil, request.url?.password == nil else { error = BatteryError("公开来源重定向无效。"); completionHandler(nil); return }
        completionHandler(request)
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError failure: Error?) { if error == nil { error = failure }; done.signal() }
}

public final class NewsClient {
    public static let defaultRSS = "https://rsshub.stsecurity.moe/twitter/user/thsottiaux"
    public static let fallbackIndex = "https://raw.githubusercontent.com/AllenXiao95/codex-reset-signal/monitor-state/status.json"
    private let lock = NSLock()
    private var active: URLSession?, stopped = false
    public init() {}
    public func stop() { lock.lock(); stopped = true; let session = active; active = nil; lock.unlock(); session?.invalidateAndCancel() }
    private func get(_ address: String) throws -> Data {
        guard let url = URL(string: address), url.scheme == "https", url.user == nil, url.password == nil else { throw BatteryError("仅支持没有账号信息的 HTTPS 消息源。") }
        let reader = BoundedTransfer(), config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = 18; config.timeoutIntervalForResource = 20
        config.httpCookieStorage = nil; config.httpShouldSetCookies = false; config.urlCredentialStorage = nil
        let session = URLSession(configuration: config, delegate: reader, delegateQueue: nil)
        lock.lock(); if stopped { lock.unlock(); session.invalidateAndCancel(); throw BatteryError("查询已取消。") }; active = session; lock.unlock()
        defer { session.invalidateAndCancel(); lock.lock(); if active === session { active = nil }; lock.unlock() }
        var request = URLRequest(url: url); request.setValue("CodexBattery/0.1", forHTTPHeaderField: "User-Agent")
        session.dataTask(with: request).resume()
        guard reader.done.wait(timeout: .now() + 21) == .success else { throw BatteryError("公开来源查询超时。") }
        if let error = reader.error { throw error }
        return reader.data
    }
    public func fetch(customRSS: String) throws -> NewsFeed {
        let custom = customRSS.trimmingCharacters(in: .whitespacesAndNewlines), now = Date()
        var best: NewsFeed?
        if let data = try? get(custom.isEmpty ? Self.defaultRSS : custom) { best = try? NewsParser.rss(data, source: custom.isEmpty ? Self.defaultRSS : custom, now: now) }
        if custom.isEmpty && (best == nil || best!.stale(at: now)), let data = try? get(Self.fallbackIndex), let fallback = try? NewsParser.index(data, source: Self.fallbackIndex, now: now), best == nil || (fallback.latestPost ?? .distantPast) > (best!.latestPost ?? .distantPast) { best = fallback }
        guard let best else { throw BatteryError("公开来源不可用，无法确认是否有新消息。") }
        return best
    }
}

public final class NewsStore {
    public var feed = NewsFeed(), healthy = false, enabled = true
    public var opened: [String: String] = [:]
    public init() {}
    public func visible(at now: Date) -> [NewsItem] { enabled ? feed.items.filter { $0.recent(at: now) }.sorted { $0.published > $1.published } : [] }
    public func prune(at now: Date) { feed.items = feed.items.filter { $0.recent(at: now) }; let ids = Set(feed.items.map(\.id)); opened = opened.filter { ids.contains($0.key) } }
    public func unread(_ item: NewsItem) -> Bool { opened[item.id] != item.version }
    public func badge(at now: Date) -> Bool { enabled && healthy && !feed.stale(at: now) && visible(at: now).contains(where: unread) }
    public func apply(_ incoming: NewsFeed, now: Date) { let ids = Set(incoming.items.map(\.id)); var merged = incoming; merged.items = NewsParser.unique(incoming.items + feed.items.filter { !ids.contains($0.id) }); feed = merged; healthy = true; prune(at: now) }
    public func load(from url: URL, now: Date) {
        guard let data = try? Data(contentsOf: url), data.count <= 2_000_000, let cache = try? JSONDecoder().decode(NewsCache.self, from: data) else { return }
        opened = cache.opened
        feed.items = cache.items.compactMap { NewsParser.make(url: $0.url, text: $0.text, published: $0.published) }
        prune(at: now)
    }
    public func save(to url: URL, now: Date) throws { prune(at: now); let data = try JSONEncoder().encode(NewsCache(items: feed.items, opened: opened)); try data.write(to: url, options: .atomic) }
}
private struct NewsCache: Codable { let items: [NewsItem], opened: [String: String] }
