// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "CursorPocketMac",
    platforms: [.macOS(.v13)],
    products: [
        .executable(name: "CursorPocketMac", targets: ["CursorPocketMac"])
    ],
    targets: [
        .target(
            name: "CursorPocketMacKit",
            path: "Sources/CursorPocketMacKit"
        ),
        .executableTarget(
            name: "CursorPocketMac",
            dependencies: ["CursorPocketMacKit"],
            path: "Sources/CursorPocketMac"
        ),
        .testTarget(
            name: "CursorPocketMacKitTests",
            dependencies: ["CursorPocketMacKit"],
            path: "Tests/CursorPocketMacKitTests"
        )
    ]
)
