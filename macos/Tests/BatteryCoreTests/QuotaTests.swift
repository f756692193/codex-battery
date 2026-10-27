import XCTest
@testable import BatteryCore

final class QuotaTests: XCTestCase {
    let now = Date(timeIntervalSince1970: 1_800_000_000)
    func window(_ used: Double, _ minutes: Int, _ offset: TimeInterval = 3600) -> [String: Any] {
        ["usedPercent": used, "windowDurationMins": minutes, "resetsAt": now.addingTimeInterval(offset).timeIntervalSince1970]
    }
    func snapshot(_ limits: [String: Any]) -> QuotaSnapshot { QuotaSnapshot.parse(limits, account: "demo@example.invalid", plan: "pro", now: now) }
    func testDurationsAndBucketSelection() {
        let value = snapshot(["rateLimitsByLimitId": [
            "codex": ["primary": window(6, 10080), "secondary": window(40, 300)],
            "other": ["primary": window(99, 60)]
        ]])
        XCTAssertEqual(value.windows.map(\.period), ["5小时", "7天", "1小时"])
        XCTAssertEqual(value.selected(at: now)?.remaining, 60)
        XCTAssertEqual(value.selected(key: "other:primary", at: now)?.remaining, 1)
        XCTAssertNil(value.selected(key: "missing", at: now))
    }
    func testExpiredAndMissingNeverBecomeFull() {
        let value = snapshot(["rateLimits": ["primary": window(25, 10080, -1), "secondary": ["usedPercent": 10]]])
        XCTAssertEqual(value.windows.count, 1); XCTAssertNil(value.selected(at: now))
        XCTAssertEqual(value.windows[0].countdown(at: now), "到期 · 待确认")
    }
    func testAuthoritativeCards() {
        for (raw, expected) in [(2.0, 2 as Int?), (0.0, 0), (-1.0, nil), (2.5, nil)] {
            let value = snapshot(["rateLimitResetCredits": ["availableCount": raw, "credits": []]])
            XCTAssertEqual(value.resetCards, expected)
        }
        XCTAssertNil(snapshot([:]).resetCards)
        XCTAssertNil(snapshot(["rateLimitResetCredits": ["availableCount": true]]).resetCards)
    }
    func testClampAndMalformedNumbers() {
        let value = snapshot(["rateLimits": ["primary": window(200, 90), "secondary": window(-5, 30)]])
        XCTAssertEqual(value.windows.map(\.remaining), [100, 0])
        XCTAssertEqual(value.windows.map(\.period), ["30分", "90分"])
        XCTAssertNil(QuotaSnapshot.number(Double.nan)); XCTAssertNil(QuotaSnapshot.number(true))
    }
    func testLegacyNotDuplicatedAndEmptyMapFallsBack() {
        let legacy: [String: Any] = ["primary": window(25, 10080)]
        XCTAssertEqual(snapshot(["rateLimitsByLimitId": ["codex": legacy], "rateLimits": legacy]).windows.count, 1)
        XCTAssertEqual(snapshot(["rateLimitsByLimitId": [:], "rateLimits": legacy]).windows.count, 1)
    }
    func testPercentAndTimeZone() {
        XCTAssertEqual(DisplayClock.percent(nil), "—"); XCTAssertEqual(DisplayClock.percent(56.69), "56.6%")
        XCTAssertEqual(DisplayClock.date(Date(timeIntervalSince1970: 0), local: false), "01-01 08:00")
    }
}
