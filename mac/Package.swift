// swift-tools-version:6.0
import PackageDescription

let package = Package(
    name: "SnapStudio",
    platforms: [.macOS(.v14)],
    targets: [
        .executableTarget(
            name: "SnapStudio",
            path: "Sources/SnapStudio",
            linkerSettings: [
                .linkedFramework("AppKit"),
                .linkedFramework("Carbon"),
                .linkedFramework("ScreenCaptureKit"),
                .linkedFramework("AVFoundation"),
                .linkedFramework("CoreImage"),
                .linkedFramework("QuickLookThumbnailing"),
                .linkedFramework("ServiceManagement"),
                .linkedFramework("UserNotifications"),
            ]
        )
    ],
    swiftLanguageModes: [.v5]
)
