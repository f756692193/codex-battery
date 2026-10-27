import AppKit
import BatteryCore
import Darwin

private let cardColor = NSColor(calibratedRed: 27/255, green: 35/255, blue: 49/255, alpha: 1)
private let muted = NSColor(calibratedRed: 154/255, green: 171/255, blue: 193/255, alpha: 1)
private let green = NSColor(calibratedRed: 102/255, green: 228/255, blue: 178/255, alpha: 1)

struct AppSettings: Codable {
    var codexPath = "", rssURL = "", floating = true, news = true, localTime = false
    var floatX: Double?, floatY: Double?
}

final class ActionButton: NSButton {
    var run: (() -> Void)?
    init(_ title: String, _ run: @escaping () -> Void) {
        super.init(frame: .zero); self.title = title; self.run = run
        target = self; action = #selector(invoke); isBordered = false; contentTintColor = muted
        font = .systemFont(ofSize: 12); alignment = .left
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
    @objc private func invoke() { run?() }
}

final class BatteryView: NSView {
    var remaining: Double?, unread = false
    var dragFinished: (() -> Void)?, showDetails: (() -> Void)?, showNews: (() -> Void)?
    override var acceptsFirstResponder: Bool { false }
    override func draw(_ dirtyRect: NSRect) {
        super.draw(dirtyRect)
        let body = NSRect(x: 0.5, y: 0.5, width: bounds.width - 5, height: bounds.height - 1)
        NSColor(calibratedWhite: 0.10, alpha: 1).setFill(); body.fill()
        if let remaining {
            NSColor(calibratedRed: 0.15, green: 0.63, blue: 0.35, alpha: 1).setFill()
            NSRect(x: 2, y: 2, width: (body.width - 3) * CGFloat(max(0, min(100, remaining))) / 100, height: body.height - 3).fill()
        }
        muted.setStroke(); NSBezierPath(rect: body).stroke()
        (unread ? NSColor.systemRed : muted).setFill()
        NSRect(x: bounds.width - 4, y: bounds.height * 0.31, width: 4, height: bounds.height * 0.38).fill()
        let text = DisplayClock.percent(remaining) as NSString
        let paragraph = NSMutableParagraphStyle(); paragraph.alignment = .center
        let attrs: [NSAttributedString.Key: Any] = [.font: NSFont.systemFont(ofSize: 12), .foregroundColor: NSColor.white, .paragraphStyle: paragraph]
        text.draw(in: NSRect(x: 0, y: (bounds.height - 16) / 2, width: body.width, height: 17), withAttributes: attrs)
    }
    override func mouseDown(with event: NSEvent) {
        if event.clickCount == 2 { showDetails?(); return }
        if unread && convert(event.locationInWindow, from: nil).x > bounds.width - 5 { showNews?(); return }
        window?.performDrag(with: event); dragFinished?()
    }
}

final class ProgressView: NSView {
    var remaining: Double?
    override func draw(_ dirtyRect: NSRect) {
        NSColor(calibratedWhite: 0.25, alpha: 1).setFill(); bounds.fill()
        if let remaining { NSColor(calibratedRed: 0.47, green: 0.82, blue: 0.59, alpha: 1).setFill(); NSRect(x: 0, y: 0, width: bounds.width * CGFloat(remaining) / 100, height: bounds.height).fill() }
        cardColor.withAlphaComponent(0.3).setStroke()
        for i in 1..<12 { let p = NSBezierPath(); p.move(to: NSPoint(x: bounds.width * CGFloat(i) / 12, y: 0)); p.line(to: NSPoint(x: bounds.width * CGFloat(i) / 12, y: bounds.height)); p.stroke() }
    }
}

final class FlippedView: NSView { override var isFlipped: Bool { true } }

final class SettingsWindow: NSWindowController {
    let path = NSTextField(), rss = NSTextField()
    let floating = NSButton(checkboxWithTitle: "显示绿色电池浮窗", target: nil, action: nil)
    let news = NSButton(checkboxWithTitle: "每 5 分钟检查 Tibo 重置卡消息", target: nil, action: nil)
    let local = NSButton(checkboxWithTitle: "使用系统时区（默认北京时间）", target: nil, action: nil)
    init(settings: AppSettings, save: @escaping (AppSettings) -> Void) {
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 520, height: 345), styleMask: [.titled, .closable], backing: .buffered, defer: false)
        super.init(window: window); window.title = "Codex Battery · 设置"; window.isReleasedWhenClosed = false
        let root = FlippedView(frame: NSRect(x: 0, y: 0, width: 520, height: 345)); window.contentView = root
        func label(_ text: String, _ y: CGFloat) { let l = NSTextField(labelWithString: text); l.frame = NSRect(x: 20, y: y, width: 480, height: 20); l.font = .systemFont(ofSize: 12); root.addSubview(l) }
        label("Codex 可执行文件路径（留空自动查找）", 17)
        path.stringValue = settings.codexPath; path.frame = NSRect(x: 20, y: 43, width: 395, height: 25); root.addSubview(path)
        let browse = ActionButton("选择…") { [weak self] in
            let picker = NSOpenPanel(); picker.canChooseDirectories = false; picker.allowsMultipleSelection = false
            if picker.runModal() == .OK { self?.path.stringValue = picker.url?.path ?? "" }
        }; browse.frame = NSRect(x: 430, y: 43, width: 70, height: 25); root.addSubview(browse)
        label("HTTPS RSS 地址（留空使用公开转发源）", 82)
        rss.stringValue = settings.rssURL; rss.frame = NSRect(x: 20, y: 108, width: 480, height: 25); root.addSubview(rss)
        floating.state = settings.floating ? .on : .off; news.state = settings.news ? .on : .off; local.state = settings.localTime ? .on : .off
        for (i, button) in [floating, news, local].enumerated() { button.frame = NSRect(x: 20, y: CGFloat(151 + i * 32), width: 480, height: 24); root.addSubview(button) }
        label("复用本机 Codex 登录；无需密码或 API Key。", 253)
        let cancel = ActionButton("取消") { [weak self] in self?.close() }; cancel.frame = NSRect(x: 355, y: 295, width: 65, height: 27); root.addSubview(cancel)
        let apply = ActionButton("保存") { [weak self] in
            guard let self else { return }
            var value = settings
            value.codexPath = self.path.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            value.rssURL = self.rss.stringValue.trimmingCharacters(in: .whitespacesAndNewlines)
            if !value.rssURL.isEmpty {
                guard let url = URL(string: value.rssURL), url.scheme == "https", url.user == nil, url.password == nil else {
                    let alert = NSAlert(); alert.messageText = "请输入没有账号信息的 HTTPS RSS 地址。"; alert.runModal(); return
                }
            }
            value.floating = self.floating.state == .on; value.news = self.news.state == .on; value.localTime = self.local.state == .on
            save(value); self.close()
        }; apply.frame = NSRect(x: 435, y: 295, width: 65, height: 27); root.addSubview(apply)
        window.center()
    }
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    private let client = CodexClient(), newsClient = NewsClient(), newsStore = NewsStore()
    private var settings = AppSettings(), snapshot: QuotaSnapshot?, healthy = false, busy = false, newsBusy = false, quitting = false
    private var status = "正在读取本机账号…", newsStatus = "尚未检查公开来源"
    private var nextQuota = Date.distantPast, nextNews = Date.distantPast
    private var revision = 0, expiredSeen = Set<String>()
    private var statusItem: NSStatusItem!, menu = NSMenu(), battery: BatteryView!, floatPanel: NSPanel!, details: NSPanel!, scroll: NSScrollView!
    private var settingsWindow: SettingsWindow?, tick: Timer?, hover: Timer?, wakeObserver: NSObjectProtocol?
    private var anchor = NSRect.zero, pinned = false
    private let dataDir = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support/CodexBattery", isDirectory: true)
    private var settingsURL: URL { dataDir.appendingPathComponent("settings.json") }
    private var newsURL: URL { dataDir.appendingPathComponent("tibo-news.json") }
    private var fresh: Bool { healthy && snapshot != nil && Date().timeIntervalSince(snapshot!.fetchedAt) <= 150 }

    func applicationDidFinishLaunching(_ notification: Notification) {
        // This accessory app has no Dock icon. Repeated launches reveal the existing process.
        let others = NSRunningApplication.runningApplications(withBundleIdentifier: Bundle.main.bundleIdentifier ?? "local.codexbattery.app").filter { $0.processIdentifier != ProcessInfo.processInfo.processIdentifier }
        if let other = others.first { other.activate(options: [.activateIgnoringOtherApps]); NSApp.terminate(nil); return }
        try? FileManager.default.createDirectory(at: dataDir, withIntermediateDirectories: true)
        if let data = try? Data(contentsOf: settingsURL), let saved = try? JSONDecoder().decode(AppSettings.self, from: data) { settings = saved }
        newsStore.enabled = settings.news; newsStore.load(from: newsURL, now: Date())
        statusItem = NSStatusBar.system.statusItem(withLength: 68)
        if let button = statusItem.button { button.title = "—"; button.target = self; button.action = #selector(statusClick); button.sendAction(on: [.leftMouseUp, .rightMouseUp]) }
        for (title, action) in [("查看额度", #selector(openSummary)), ("立即刷新", #selector(refreshAll)), ("Tibo 重置卡消息", #selector(openNews)), ("显示 / 隐藏电池", #selector(toggleFloat)), ("设置…", #selector(openSettings)), ("退出 Codex Battery", #selector(quit))] {
            let item = NSMenuItem(title: title, action: action, keyEquivalent: ""); item.target = self; menu.addItem(item)
        }
        floatPanel = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 64, height: 24), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        floatPanel.level = .floating; floatPanel.isOpaque = false; floatPanel.backgroundColor = .clear; floatPanel.hasShadow = false
        floatPanel.hidesOnDeactivate = false; floatPanel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        battery = BatteryView(frame: NSRect(x: 0, y: 0, width: 64, height: 24)); battery.menu = menu; floatPanel.contentView = battery
        battery.dragFinished = { [weak self] in self?.savePosition() }; battery.showDetails = { [weak self] in self?.openSummary() }; battery.showNews = { [weak self] in self?.openNews() }
        let screen = NSScreen.main?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1280, height: 800)
        let x = settings.floatX ?? Double(screen.maxX - 90), y = settings.floatY ?? Double(screen.minY + 24)
        floatPanel.setFrameOrigin(NSPoint(x: CGFloat(x), y: CGFloat(y)))
        if !NSScreen.screens.contains(where: { $0.visibleFrame.intersects(floatPanel.frame) }) { floatPanel.setFrameOrigin(NSPoint(x: screen.maxX - 90, y: screen.minY + 24)) }
        details = NSPanel(contentRect: NSRect(x: 0, y: 0, width: 424, height: 190), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: false)
        details.level = .floating; details.backgroundColor = cardColor; details.hidesOnDeactivate = false
        details.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]; details.hasShadow = true
        scroll = NSScrollView(frame: details.contentView!.bounds); scroll.autoresizingMask = [.width, .height]; scroll.hasVerticalScroller = true; scroll.autohidesScrollers = true; scroll.drawsBackground = false; details.contentView = scroll
        if settings.floating { floatPanel.orderFrontRegardless() }
        tick = Timer.scheduledTimer(withTimeInterval: 1, repeats: true) { [weak self] _ in self?.update() }
        hover = Timer.scheduledTimer(withTimeInterval: 0.15, repeats: true) { [weak self] _ in self?.hoverTick() }
        wakeObserver = NSWorkspace.shared.notificationCenter.addObserver(forName: NSWorkspace.didWakeNotification, object: nil, queue: .main) { [weak self] _ in self?.refreshAll() }
        update()
    }
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool { openSummary(); return false }
    private func save() { if let data = try? JSONEncoder().encode(settings) { try? data.write(to: settingsURL, options: .atomic) } }
    private func savePosition() { settings.floatX = Double(floatPanel.frame.minX); settings.floatY = Double(floatPanel.frame.minY); save(); details.orderOut(nil) }
    private func update() {
        guard !quitting else { return }; let now = Date()
        newsStore.prune(at: now)
        if !busy && now >= nextQuota { fetchQuota() }
        if settings.news && !newsBusy && now >= nextNews { fetchNews() }
        if fresh, let snapshot {
            for window in snapshot.windows where window.expired(at: now) {
                if expiredSeen.insert(window.key + "|\(window.reset.timeIntervalSince1970)").inserted { nextQuota = min(nextQuota, now) }
            }
        }
        let selected = fresh ? snapshot?.selected(at: now) : nil
        battery.remaining = selected?.remaining; battery.unread = newsStore.badge(at: now); battery.needsDisplay = true
        if let button = statusItem.button {
            button.title = DisplayClock.percent(selected?.remaining) + (battery.unread ? " •" : "")
            button.contentTintColor = selected == nil ? muted : green; button.toolTip = selected.map { "Codex · " + $0.countdown(at: now) } ?? status
        }
        if details.isVisible && NSEvent.pressedMouseButtons == 0 { renderSummary() }
    }
    private func fetchQuota() {
        busy = true; let path = settings.codexPath, generation = revision
        DispatchQueue.global(qos: .utility).async { [weak self] in
            guard let self else { return }
            let result = Result { try self.client.fetch(manualPath: path) }
            DispatchQueue.main.async {
                guard !self.quitting else { return }; self.busy = false; self.nextQuota = Date().addingTimeInterval(60)
                guard generation == self.revision else { self.nextQuota = .distantPast; self.update(); return }
                switch result { case .success(let value): self.snapshot = value; self.healthy = true; self.status = value.windows.isEmpty ? "暂无有效额度窗口" : "数据正常"
                    let keys = Set(value.windows.map { $0.key + "|\($0.reset.timeIntervalSince1970)" }); self.expiredSeen.formIntersection(keys)
                case .failure(let error): self.healthy = false; self.status = error.localizedDescription }
                self.update()
            }
        }
    }
    private func fetchNews() {
        newsBusy = true; let source = settings.rssURL, generation = revision
        DispatchQueue.global(qos: .utility).async { [weak self] in
            guard let self else { return }; let result = Result { try self.newsClient.fetch(customRSS: source) }
            DispatchQueue.main.async {
                guard !self.quitting else { return }; self.newsBusy = false; self.nextNews = Date().addingTimeInterval(300)
                guard generation == self.revision else { self.nextNews = .distantPast; self.update(); return }
                switch result { case .success(let value): self.newsStore.apply(value, now: Date()); self.newsStatus = value.stale(at: Date()) ? "来源内容较旧，需打开 X 确认" : "获取成功 · 完整性未核实"
                case .failure: self.newsStore.healthy = false; self.newsStatus = "公开来源不可用，可能延迟或漏帖" }
                try? self.newsStore.save(to: self.newsURL, now: Date()); self.update()
            }
        }
    }
    private func hoverTick() {
        guard !quitting && NSEvent.pressedMouseButtons == 0 else { return }; let point = NSEvent.mouseLocation
        if !pinned && floatPanel.isVisible && floatPanel.frame.insetBy(dx: -5, dy: -5).contains(point) { anchor = floatPanel.frame; showPanel(); return }
        if details.isVisible && !pinned && !anchor.insetBy(dx: -9, dy: -9).contains(point) && !details.frame.insetBy(dx: -9, dy: -9).contains(point) { details.orderOut(nil) }
    }
    private func showPanel() { renderSummary(); positionDetails(); details.orderFrontRegardless() }
    private func positionDetails() {
        let screen = NSScreen.screens.first { $0.visibleFrame.intersects(anchor) }?.visibleFrame ?? NSScreen.main!.visibleFrame
        let x = max(screen.minX + 8, min(anchor.maxX - details.frame.width, screen.maxX - details.frame.width - 8))
        var y = anchor.maxY + 7
        if y + details.frame.height > screen.maxY - 8 { y = anchor.minY - details.frame.height - 7 }
        y = max(screen.minY + 8, min(y, screen.maxY - details.frame.height - 8)); details.setFrameOrigin(NSPoint(x: x, y: y))
    }
    private func renderSummary() {
        let now = Date(), root = FlippedView(), width: CGFloat = 424
        var y: CGFloat = 12
        func label(_ text: String, _ x: CGFloat, _ top: CGFloat, _ w: CGFloat, _ size: CGFloat = 12, _ color: NSColor = muted, _ bold: Bool = false, _ right: Bool = false) {
            let l = NSTextField(labelWithString: text); l.font = bold ? .boldSystemFont(ofSize: size) : .systemFont(ofSize: size); l.textColor = color; l.lineBreakMode = .byTruncatingTail
            l.frame = NSRect(x: x, y: top, width: w, height: 20); l.alignment = right ? .right : .left; root.addSubview(l)
        }
        func rule() { let line = NSView(frame: NSRect(x: 16, y: y, width: width - 32, height: 1)); line.wantsLayer = true; line.layer?.backgroundColor = muted.withAlphaComponent(0.25).cgColor; root.addSubview(line); y += 12 }
        let plan = fresh ? snapshot?.plan ?? "" : ""
        label("Codex" + (plan.isEmpty ? "" : " · " + plan), 16, y, 310, 13, .white, true); label("剩余", 350, y, 58, 11, muted, false, true); y += 32
        let windows = fresh ? snapshot?.windows ?? [] : []
        if windows.isEmpty {
            label(busy ? "正在查询账号额度…" : "当前额度未知 · 待刷新", 16, y, 390, 13); y += 23
            label(healthy && !fresh ? "数据已过期，等待刷新" : status, 16, y, 390, 11); y += 25
        } else {
            let multiple = Set(windows.map(\.bucketID)).count > 1; var bucket = ""
            for window in windows {
                if multiple && bucket != window.bucketID { label(window.bucketName, 16, y, 390, 11); y += 23; bucket = window.bucketID }
                let valid = !window.expired(at: now)
                label(window.period, 16, y, 50)
                let progress = ProgressView(frame: NSRect(x: 71, y: y + 5, width: 108, height: 10)); progress.remaining = valid ? window.remaining : nil; root.addSubview(progress)
                label(DisplayClock.percent(valid ? window.remaining : nil), 184, y, 51, 12, valid ? green : muted, true, true)
                label(window.countdown(at: now), 247, y, 161, 12, valid ? green : .systemOrange, false, true)
                label("下次重置：" + DisplayClock.date(window.reset, local: settings.localTime) + (settings.localTime ? " · 本地时间" : " · 北京时间 UTC+8"), 71, y + 23, 337, 10.5); y += 48
            }
        }
        rule(); label("重置卡", 16, y, 180, 12, .white)
        let cards = fresh ? snapshot?.resetCards : nil
        label(cards.map { "\($0) 张" } ?? "待更新", 248, y, 160, 12, .white, true, true); y += 32
        let items = newsStore.visible(at: now)
        if !items.isEmpty {
            rule(); label("Tibo · 重置卡（近3天，待核实）" + (newsStore.healthy && !newsStore.feed.stale(at: now) ? "" : " · 待更新"), 16, y, 390, 11); y += 26
            for item in items {
                let button = ActionButton(DisplayClock.date(item.published, local: settings.localTime, format: "MM-dd") + "  " + item.summary + "  ↗") { [weak self] in self?.openPost(item) }
                button.contentTintColor = newsStore.unread(item) ? green : muted; button.cell?.lineBreakMode = .byTruncatingTail; button.toolTip = item.text
                button.frame = NSRect(x: 16, y: y, width: 390, height: 25); root.addSubview(button); y += 29
            }
        } else if pinned {
            rule(); label("近3天暂无可显示的重置卡消息", 16, y, 390, 11); y += 23; label(newsStatus, 16, y, 390, 11); y += 25
            let profile = ActionButton("打开 Tibo 的 X 主页 ↗") { NSWorkspace.shared.open(URL(string: NewsParser.profile)!) }; profile.frame = NSRect(x: 16, y: y, width: 390, height: 25); root.addSubview(profile); y += 30
        }
        rule()
        let actions: [(String, CGFloat, () -> Void)] = [("刷新", 16, { [weak self] in self?.refreshAll() }), ("设置", 295, { [weak self] in self?.openSettings() }), (pinned ? "收起" : "退出", 365, { [weak self] in guard let self else { return }; if self.pinned { self.pinned = false; self.details.orderOut(nil) } else { self.quit() } })]
        for (text, x, action) in actions {
            let button = ActionButton(text, action); button.frame = NSRect(x: x, y: y - 3, width: 50, height: 26); root.addSubview(button)
        }; y += 33
        root.frame = NSRect(x: 0, y: 0, width: width, height: y)
        // Preserve vertical scroll while countdowns refresh.
        let offset = scroll.contentView.bounds.origin
        scroll.documentView = root
        let available = NSScreen.screens.first { $0.visibleFrame.intersects(anchor) }?.visibleFrame.height ?? 800
        details.setContentSize(NSSize(width: width, height: min(y, available - 24)))
        scroll.contentView.scroll(to: NSPoint(x: 0, y: min(max(0, offset.y), max(0, y - scroll.contentView.bounds.height))))
        scroll.reflectScrolledClipView(scroll.contentView)
        if details.isVisible { positionDetails() }
    }
    private func openPost(_ item: NewsItem) {
        guard let link = NewsParser.canonicalPost(item.url), let url = URL(string: link), NSWorkspace.shared.open(url) else { return }
        newsStore.opened[item.id] = item.version; try? newsStore.save(to: newsURL, now: Date()); update()
    }
    @objc private func statusClick() {
        if NSApp.currentEvent?.type == .rightMouseUp { statusItem.popUpMenu(menu); return }
        if details.isVisible { pinned = false; details.orderOut(nil) } else { openSummary() }
    }
    @objc private func openSummary() {
        pinned = false
        if floatPanel.isVisible { anchor = floatPanel.frame }
        else if let button = statusItem.button, let window = button.window { anchor = window.convertToScreen(button.convert(button.bounds, to: nil)) }
        showPanel()
    }
    @objc private func openNews() { openSummary(); pinned = true; renderSummary() }
    @objc private func refreshAll() { nextQuota = .distantPast; nextNews = .distantPast; update() }
    @objc private func toggleFloat() { settings.floating.toggle(); save(); if settings.floating { floatPanel.orderFrontRegardless() } else { floatPanel.orderOut(nil); details.orderOut(nil) } }
    @objc private func openSettings() {
        details.orderOut(nil); pinned = false
        settingsWindow = SettingsWindow(settings: settings) { [weak self] value in
            guard let self else { return }; self.revision += 1; self.settings = value; self.newsStore.enabled = value.news; self.save()
            if value.floating { self.floatPanel.orderFrontRegardless() } else { self.floatPanel.orderOut(nil) }
            self.healthy = false; self.refreshAll()
        }
        NSApp.activate(ignoringOtherApps: true); settingsWindow?.showWindow(nil); settingsWindow?.window?.makeKeyAndOrderFront(nil)
    }
    @objc private func quit() { NSApp.terminate(nil) }
    func applicationWillTerminate(_ notification: Notification) {
        quitting = true; tick?.invalidate(); hover?.invalidate(); client.stop(); newsClient.stop()
        if let wakeObserver { NSWorkspace.shared.notificationCenter.removeObserver(wakeObserver) }
        try? newsStore.save(to: newsURL, now: Date())
    }
}

@main
enum CodexBatteryMain {
    @MainActor static func main() {
        // Ignore a closed helper pipe; Foundation reports it as a write error instead of exiting the app.
        signal(SIGPIPE, SIG_IGN)
        let app = NSApplication.shared
        let delegate = AppDelegate()
        app.setActivationPolicy(.accessory); app.delegate = delegate; app.run()
    }
}
