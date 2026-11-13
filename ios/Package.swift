// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "KanaCore",
    platforms: [.iOS(.v15), .macOS(.v12)],
    products: [.library(name: "KanaCore", targets: ["KanaCore"])],
    targets: [
        .target(name: "KanaCore"),
        .testTarget(name: "KanaCoreTests", dependencies: ["KanaCore"])
    ]
)
