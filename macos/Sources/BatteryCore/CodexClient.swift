import Foundation
import Darwin

public enum CodexLocator {
    public static func find(manual: String) throws -> String {
        let fm = FileManager.default
        if !manual.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            let path = NSString(string: manual).expandingTildeInPath
            guard fm.isExecutableFile(atPath: path), !isDirectory(path) else { throw BatteryError("设置中的 Codex 路径不可执行。") }
            return path
        }
        let home = fm.homeDirectoryForCurrentUser.path
        let bundled = ["/Applications/Codex.app/Contents/Resources/codex", home + "/Applications/Codex.app/Contents/Resources/codex"]
        let paths = (ProcessInfo.processInfo.environment["PATH"] ?? "").split(separator: ":").map(String.init)
        let candidates = bundled + (["/opt/homebrew/bin", "/usr/local/bin", home + "/.local/bin", home + "/.cargo/bin"] + paths).map { $0 + "/codex" }
        guard let found = candidates.first(where: { fm.isExecutableFile(atPath: $0) && !isDirectory($0) }) else {
            throw BatteryError("未找到 codex。请在设置中选择本机 Codex 可执行文件。")
        }
        return found
    }
    private static func isDirectory(_ path: String) -> Bool {
        var directory: ObjCBool = false
        FileManager.default.fileExists(atPath: path, isDirectory: &directory)
        return directory.boolValue
    }
}

// A fresh, privately owned stdio helper per query. No login/token file is opened here.
final class StdioRPC {
    private let process = Process(), input = Pipe(), output = Pipe()
    private let condition = NSCondition()
    private var buffer = Data(), response: [String: Any]?, waitingID = 0, nextID = 0
    private var failure: Error?, closed = false
    init(path: String) throws {
        process.executableURL = URL(fileURLWithPath: path)
        process.arguments = ["app-server", "--listen", "stdio://"]
        var environment = ProcessInfo.processInfo.environment
        environment["PATH"] = "/opt/homebrew/bin:/usr/local/bin:" + (environment["PATH"] ?? "/usr/bin:/bin")
        process.environment = environment
        process.standardInput = input; process.standardOutput = output; process.standardError = FileHandle.nullDevice
        output.fileHandleForReading.readabilityHandler = { [weak self] handle in self?.receive(handle.availableData) }
        do { try process.run() } catch {
            output.fileHandleForReading.readabilityHandler = nil
            throw BatteryError("无法启动本机 Codex 查询进程。请检查程序路径。")
        }
    }
    private func receive(_ bytes: Data) {
        condition.lock(); defer { condition.unlock() }
        if closed { return }
        if bytes.isEmpty { failure = BatteryError("Codex 查询进程已结束。"); condition.broadcast(); return }
        buffer.append(bytes)
        if buffer.count > 4_000_000 { failure = BatteryError("Codex 响应过大。"); condition.broadcast(); return }
        while let end = buffer.firstIndex(of: 10) {
            let line = Data(buffer[..<end]); buffer.removeSubrange(...end)
            guard let message = (try? JSONSerialization.jsonObject(with: line)) as? [String: Any],
                  let id = message["id"] as? Int, id == waitingID else { continue }
            response = message; condition.broadcast()
        }
    }
    private func write(_ object: [String: Any]) throws {
        var data = try JSONSerialization.data(withJSONObject: object, options: [.withoutEscapingSlashes]); data.append(10)
        do { try input.fileHandleForWriting.write(contentsOf: data) }
        catch { throw BatteryError("Codex 查询连接已断开。") }
    }
    func call(_ method: String, params: [String: Any]? = nil) throws -> [String: Any] {
        condition.lock(); defer { condition.unlock() }
        if closed { throw BatteryError("查询已取消。") }
        nextID += 1; waitingID = nextID; response = nil
        var request: [String: Any] = ["id": waitingID, "method": method]
        if let params { request["params"] = params }
        try write(request)
        let deadline = Date().addingTimeInterval(30)
        while response == nil && failure == nil && !closed {
            if !condition.wait(until: deadline) { throw BatteryError("额度查询超时，请检查网络后刷新。") }
        }
        if let failure { throw failure }
        guard !closed, let message = response else { throw BatteryError("查询已取消。") }
        if message["error"] != nil { throw BatteryError("额度查询失败，请确认 Codex 已登录且网络可用。") }
        guard let result = message["result"] as? [String: Any] else { throw BatteryError("Codex 返回了无法识别的数据。") }
        return result
    }
    func initialized() throws {
        condition.lock(); defer { condition.unlock() }
        if closed { throw BatteryError("查询已取消。") }
        try write(["method": "initialized", "params": [:]])
    }
    func close() {
        condition.lock()
        if closed { condition.unlock(); return }
        closed = true; condition.broadcast(); condition.unlock()
        output.fileHandleForReading.readabilityHandler = nil
        try? input.fileHandleForWriting.close()
        if process.isRunning {
            process.terminate()
            // Do not wait on the UI thread. Kill only this still-owned Process if it ignores SIGTERM.
            DispatchQueue.global().async { [process] in
                let deadline = Date().addingTimeInterval(1)
                while process.isRunning && Date() < deadline { Thread.sleep(forTimeInterval: 0.025) }
                if process.isRunning { _ = Darwin.kill(process.processIdentifier, SIGKILL) }
            }
        }
    }
    deinit { close() }
}

public final class CodexClient {
    private let lock = NSLock()
    private var active: StdioRPC?, stopped = false
    public init() {}
    public func stop() {
        lock.lock(); stopped = true; let helper = active; active = nil; lock.unlock(); helper?.close()
    }
    public func fetch(manualPath: String) throws -> QuotaSnapshot {
        let helper = try StdioRPC(path: CodexLocator.find(manual: manualPath))
        lock.lock()
        if stopped { lock.unlock(); helper.close(); throw BatteryError("查询已取消。") }
        active = helper; lock.unlock()
        defer {
            helper.close(); lock.lock(); if active === helper { active = nil }; lock.unlock()
        }
        _ = try helper.call("initialize", params: ["clientInfo": ["name": "codex_battery", "title": "Codex Battery", "version": "0.1.0"]])
        try helper.initialized()
        let before = try helper.call("account/read", params: ["refreshToken": false])
        guard let account = before["account"] as? [String: Any] else { throw BatteryError("请先在本机 Codex 中登录，再刷新额度。") }
        guard account["type"] as? String == "chatgpt" else { throw BatteryError("当前登录方式没有 ChatGPT 订阅额度。") }
        let limits = try helper.call("account/rateLimits/read")
        let after = try helper.call("account/read", params: ["refreshToken": false])
        guard let current = after["account"] as? [String: Any], current["type"] as? String == "chatgpt",
              current["email"] as? String == account["email"] as? String else { throw BatteryError("查询期间账号发生变化，请重新刷新。") }
        return QuotaSnapshot.parse(limits, account: account["email"] as? String ?? "", plan: current["planType"] as? String ?? "", now: Date())
    }
}
