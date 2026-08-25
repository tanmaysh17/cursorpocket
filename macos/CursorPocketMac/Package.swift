// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "CursorPocketMac",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "CursorPocketMac", targets: ["CursorPocketMac"])
    ],
    targets: [
        .executableTarget(
            name: "CursorPocketMac",
            path: "Sources/CursorPocketMac"
        )
    ]
)
