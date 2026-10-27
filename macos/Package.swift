// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "CodexBattery",
    platforms: [.macOS(.v13)],
    products: [.executable(name: "CodexBattery", targets: ["CodexBattery"])],
    targets: [
        .target(name: "BatteryCore"),
        .executableTarget(name: "CodexBattery", dependencies: ["BatteryCore"]),
        .testTarget(name: "BatteryCoreTests", dependencies: ["BatteryCore"])
    ],
    swiftLanguageVersions: [.v5]
)
