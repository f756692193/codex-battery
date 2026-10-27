import Foundation

public struct BatteryError: LocalizedError {
    public let message: String
    public init(_ message: String) { self.message = message }
    public var errorDescription: String? { message }
}

public struct QuotaWindow {
    public let bucketID: String, bucketName: String, slot: String
    public let used: Double, minutes: Int, reset: Date
    public var key: String { bucketID + ":" + slot }
    public var remaining: Double { max(0, min(100, 100 - used)) }
    public var period: String {
        if minutes % 1440 == 0 { return "\(minutes / 1440)天" }
        if minutes % 60 == 0 { return "\(minutes / 60)小时" }
        return "\(minutes)分"
    }
    public func expired(at now: Date) -> Bool { reset <= now }
    public func countdown(at now: Date) -> String {
        let seconds = max(0, Int(reset.timeIntervalSince(now)))
        if seconds == 0 { return "到期 · 待确认" }
        if seconds >= 86400 { return "\(seconds / 86400)天\(seconds % 86400 / 3600)小时后重置" }
        if seconds >= 3600 { return "\(seconds / 3600)小时\(seconds % 3600 / 60)分后重置" }
        return "\(seconds / 60)分\(seconds % 60)秒后重置"
    }
}

public struct QuotaSnapshot {
    public let account: String, plan: String, fetchedAt: Date
    public let windows: [QuotaWindow], resetCards: Int?
    public func selected(key: String = "auto", at now: Date) -> QuotaWindow? {
        let valid = windows.filter { !$0.expired(at: now) }
        if key != "auto" { return valid.first { $0.key == key } }
        let bucket = windows.contains { $0.bucketID == "codex" } ? "codex" : windows.first?.bucketID
        return valid.filter { $0.bucketID == bucket }.min {
            $0.remaining == $1.remaining ? $0.minutes < $1.minutes : $0.remaining < $1.remaining
        }
    }
    public static func number(_ value: Any?) -> Double? {
        // JSON booleans must not become percentages, durations or card counts.
        guard let n = value as? NSNumber, CFGetTypeID(n) != CFBooleanGetTypeID(), n.doubleValue.isFinite else { return nil }
        return n.doubleValue
    }
    public static func parse(_ limits: [String: Any], account: String, plan: String, now: Date) -> QuotaSnapshot {
        var rows: [QuotaWindow] = []
        var buckets = limits["rateLimitsByLimitId"] as? [String: [String: Any]] ?? [:]
        if buckets.isEmpty, let legacy = limits["rateLimits"] as? [String: Any] { buckets["codex"] = legacy }
        for (fallback, bucket) in buckets {
            let id = (bucket["limitId"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? fallback
            let name = (bucket["limitName"] as? String).flatMap { $0.isEmpty ? nil : $0 } ?? (id == "codex" ? "Codex" : id)
            for slot in ["primary", "secondary"] {
                guard let row = bucket[slot] as? [String: Any],
                      let used = number(row["usedPercent"]), let minutes = number(row["windowDurationMins"]),
                      let timestamp = number(row["resetsAt"]), minutes > 0, minutes <= Double(Int32.max), minutes.rounded(.down) == minutes,
                      timestamp >= -62135596800, timestamp <= 253402300799 else { continue }
                rows.append(QuotaWindow(bucketID: id, bucketName: name, slot: slot, used: used, minutes: Int(minutes), reset: Date(timeIntervalSince1970: timestamp)))
            }
        }
        rows.sort {
            if ($0.bucketID == "codex") != ($1.bucketID == "codex") { return $0.bucketID == "codex" }
            return $0.bucketID == $1.bucketID ? $0.minutes < $1.minutes : $0.bucketID < $1.bucketID
        }
        let credits = limits["rateLimitResetCredits"] as? [String: Any]
        let count = number(credits?["availableCount"])
        let cards = count.flatMap { $0 >= 0 && $0 <= Double(Int32.max) && $0.rounded(.down) == $0 ? Int($0) : nil }
        return QuotaSnapshot(account: account, plan: plan, fetchedAt: now, windows: rows, resetCards: cards)
    }
}

import CoreFoundation

public enum DisplayClock {
    public static func percent(_ value: Double?) -> String {
        guard let value else { return "—" }
        let n = floor(value * 10) / 10
        return n == floor(n) ? String(format: "%.0f%%", n) : String(format: "%.1f%%", n)
    }
    public static func date(_ value: Date, local: Bool, format: String = "MM-dd HH:mm") -> String {
        let f = DateFormatter(); f.locale = Locale(identifier: "zh_CN")
        f.timeZone = local ? .current : TimeZone(secondsFromGMT: 8 * 3600); f.dateFormat = format
        return f.string(from: value)
    }
}
