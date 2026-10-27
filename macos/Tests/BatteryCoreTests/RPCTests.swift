import XCTest
@testable import BatteryCore

final class RPCTests: XCTestCase {
    func fake(_ scenario: String) throws -> (URL, URL) {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let file = folder.appendingPathComponent("codex")
        // Only synthetic account data; no actual Codex install or login required.
        let script = """
        #!/bin/sh
        account_reads=0
        while IFS= read -r line; do
          case "$line" in
            *initialized*) ;;
            *initialize*) printf '%s\\n' '{"id":1,"result":{}}' ;;
            *account/read*)
              account_reads=$((account_reads + 1))
              id=2
              email='demo@example.invalid'
              if [ "$account_reads" -gt 1 ]; then id=4; fi
              if [ '\(scenario)' = 'switch' ] && [ "$account_reads" -gt 1 ]; then email='other@example.invalid'; fi
              if [ '\(scenario)' = 'no-login' ]; then
                printf '{"id":%s,"result":{"account":null}}\\n' "$id"
              else
                printf '{"id":%s,"result":{"account":{"type":"chatgpt","email":"%s","planType":"pro"}}}\\n' "$id" "$email"
              fi ;;
            *account/rateLimits/read*)
              printf '%s\\n' '{"id":3,"result":{"rateLimits":{"primary":{"usedPercent":25,"windowDurationMins":10080,"resetsAt":2000000000}},"rateLimitResetCredits":{"availableCount":2}}}' ;;
          esac
        done
        """
        try script.write(to: file, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: file.path)
        return (folder, file)
    }
    func testRPCHandshakeAndAccountConsistency() throws {
        for scenario in ["normal", "switch", "no-login"] {
            let (folder, file) = try fake(scenario)
            defer { try? FileManager.default.removeItem(at: folder) }
            let client = CodexClient(); defer { client.stop() }
            if scenario == "normal" {
                let value = try client.fetch(manualPath: file.path)
                XCTAssertEqual(value.resetCards, 2); XCTAssertEqual(value.windows.first?.remaining, 75)
            } else { XCTAssertThrowsError(try client.fetch(manualPath: file.path)) }
        }
    }
    func testMissingExecutableIsClear() { XCTAssertThrowsError(try CodexLocator.find(manual: "/missing/codex-battery-test")) }
}
