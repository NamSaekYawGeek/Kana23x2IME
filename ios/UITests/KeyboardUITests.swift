import XCTest

/// Runs against an installed app and its real keyboard extension. These tests
/// intentionally fail if Settings cannot discover or activate the extension.
final class KeyboardUITests: XCTestCase {
    private let host = XCUIApplication(bundleIdentifier: "dev.kana23x2.ios")
    private let settings = XCUIApplication(bundleIdentifier: "com.apple.Preferences")
    private let keyboardName = "23×2 Kana"
    private let englishArguments = ["-AppleLanguages", "(en)", "-AppleLocale", "en_US"]

    override func setUpWithError() throws {
        continueAfterFailure = false
        XCUIDevice.shared.orientation = .portrait
        host.launchArguments = englishArguments
        settings.launchArguments = englishArguments
    }

    override func tearDownWithError() throws {
        XCUIDevice.shared.orientation = .portrait
    }

    func testInstalledKeyboardCompositionAndAppearance() throws {
        host.launch()
        try require(host.textViews["keyboardTestEditor"], "Host test editor is missing")
        try enableKeyboardInSettings()

        host.activate()
        let editor = host.textViews["keyboardTestEditor"]
        try require(editor, "Host did not return after enabling the keyboard")
        editor.tap()
        try activateKanaKeyboard()
        try selectFirstHiraganaBoard()

        try key("kana-key-Q").tap()
        try expectReading("あ")
        screenshot("Kana23x2-light")
        try key("candidate-reading").tap()
        try expectValue("あ", in: editor)

        // Z must keep its board-switching role, independent of Shift's script.
        try key("board-key").tap()
        try key("kana-key-Q").tap()
        try expectReading("な")
        try key("candidate-reading").tap()
        try expectValue("あな", in: editor)

        try key("shift-key").tap()
        try key("kana-key-Q").tap()
        try expectReading("ナ")
        try key("candidate-reading").tap()
        try expectValue("あなナ", in: editor)

        // Board 2 W/P/M are に/ほ/ん. Exercise the bundled dictionary and
        // candidate commit through the extension, rather than host typeText().
        try key("shift-key").tap()
        for identifier in ["kana-key-W", "kana-key-P", "kana-key-M"] {
            try key(identifier).tap()
        }
        try expectReading("にほん")
        let candidate = try key("candidate-0")
        try waitFor("The full dictionary did not offer 日本 for にほん") {
            candidate.exists && candidate.label.contains("日本")
        }
        candidate.tap()
        try expectValue("あなナ日本", in: editor)

        // One sustained press must erase several committed characters. This
        // catches repeat timers that stop when a host text callback updates UI.
        try key("delete-key").press(forDuration: 1.2)
        try expectValue("", in: editor)

        // This launch argument only changes the host editor's keyboardAppearance
        // in DEBUG builds. The extension must read the real document proxy trait.
        host.terminate()
        host.launchArguments = englishArguments + ["-KanaUITestDarkMode", "YES"]
        host.launch()
        let darkEditor = host.textViews["keyboardTestEditor"]
        try require(darkEditor, "Host editor is missing after dark appearance launch")
        darkEditor.tap()
        try activateKanaKeyboard()
        screenshot("Kana23x2-dark")

        XCUIDevice.shared.orientation = .landscapeLeft
        try waitFor("Host failed to rotate to landscape") {
            self.host.frame.width > self.host.frame.height
        }
        try require(host.descendants(matching: .any)["kana-key-Q"].firstMatch, "Keyboard disappeared in landscape")
        screenshot("Kana23x2-landscape")
    }

    private func enableKeyboardInSettings() throws {
        settings.terminate()
        settings.launch()
        // Preferences may restore the page left by an earlier test run.
        for _ in 0..<6 {
            if settings.navigationBars["Settings"].exists { break }
            let back = ["Keyboard", "General", "Settings", "Apps", "Back"]
                .map { settings.navigationBars.buttons[$0].firstMatch }
                .first { canTap($0, in: settings) }
            guard let back = back else { break }
            back.tap()
        }
        try tapSettingsRow("General")
        try require(settings.navigationBars["General"], "Settings did not open General",
                    application: settings)
        try tapSettingsRow("Keyboard")
        try tapSettingsRow("Keyboards")

        if !kanaSettingsRow.exists {
            // iOS 26 exposes this as a button with identifier AddNewKeyboard;
            // its visible English label no longer includes an ellipsis.
            try tapSettingsRow("AddNewKeyboard", alternatives: [
                "Add New Keyboard", "Add New Keyboard…", "Add New Keyboard..."
            ])
            try require(kanaSettingsRow,
                        "Settings did not list 23×2 Kana as an available third-party keyboard",
                        application: settings)
            try tapKanaSettingsRow()
        }
        try require(settings.navigationBars["Keyboards"],
                    "Settings did not return to the enabled Keyboards list", application: settings)
        try require(kanaSettingsRow,
                    "23×2 Kana was not added to the enabled Keyboards list", application: settings)
    }

    private var kanaSettingsRow: XCUIElement {
        let predicate = NSPredicate(format: "label CONTAINS %@", keyboardName)
        let cell = settings.cells.matching(predicate).firstMatch
        if cell.exists { return cell }
        // Settings uses different row accessibility types between iOS versions.
        let button = settings.buttons.matching(predicate).firstMatch
        if button.exists { return button }
        return settings.staticTexts.matching(predicate).firstMatch
    }

    private func tapSettingsRow(_ title: String, alternatives: [String] = []) throws {
        let titles = [title] + alternatives
        for attempt in 0..<8 {
            for name in titles {
                // Prefer the concrete row/button identifier when Settings
                // exposes one; virtualized ancestor cells may have no frame.
                let button = settings.buttons[name].firstMatch
                if canTap(button, in: settings) { button.tap(); return }
                let identifiedCell = settings.cells[name].firstMatch
                if canTap(identifiedCell, in: settings) { identifiedCell.tap(); return }
                let cell = settings.cells.containing(.staticText, identifier: name).firstMatch
                if canTap(cell, in: settings) { cell.tap(); return }
                let label = settings.staticTexts[name].firstMatch
                if canTap(label, in: settings) { label.tap(); return }
            }
            if attempt < 7 { settings.swipeUp() }
        }
        attachHierarchy(settings, name: "Settings navigation failure")
        XCTFail("Settings row not found: \(title)")
        throw UIError.missing(title)
    }

    private func tapKanaSettingsRow() throws {
        for attempt in 0..<8 {
            let row = kanaSettingsRow
            if canTap(row, in: settings) { row.tap(); return }
            if attempt < 7 { settings.swipeUp() }
        }
        attachHierarchy(settings, name: "Keyboard registration row failure")
        XCTFail("The available 23×2 Kana keyboard row could not be tapped")
        throw UIError.missing(keyboardName)
    }

    private func canTap(_ element: XCUIElement, in application: XCUIApplication) -> Bool {
        guard element.exists else { return false }
        let frame = element.frame
        // UIKit/SwiftUI can expose offscreen Settings rows with {{inf, inf},
        // {0, 0}}. Querying isHittable on those raises an XCTest failure rather
        // than returning false, so check geometry before requesting hit testing.
        guard frame.origin.x.isFinite, frame.origin.y.isFinite,
              frame.width.isFinite, frame.height.isFinite,
              frame.width > 0, frame.height > 0,
              frame.intersects(application.frame) else { return false }
        return element.isHittable
    }

    private func activateKanaKeyboard() throws {
        if host.descendants(matching: .any)["kana-key-Q"].firstMatch.waitForExistence(timeout: 2) { return }
        let globePredicate = NSPredicate(
            format: "label CONTAINS[c] %@ OR identifier CONTAINS[c] %@ OR label == %@",
            "Next keyboard", "Next keyboard", "Switch keyboard")
        for _ in 0..<6 {
            let globe = host.descendants(matching: .any).matching(globePredicate).firstMatch
            guard globe.waitForExistence(timeout: 2), canTap(globe, in: host) else { break }
            globe.tap()
            if host.descendants(matching: .any)["kana-key-Q"].firstMatch.waitForExistence(timeout: 2) { return }
        }
        attachHierarchy(host, name: "Keyboard activation failure")
        XCTFail("The installed Kana keyboard did not activate through the system globe. " +
                "Check extension registration and that the simulator software keyboard is enabled.")
        throw UIError.missing("kana-key-Q")
    }

    private func selectFirstHiraganaBoard() throws {
        let q = try key("kana-key-Q")
        if q.label.contains("な") || q.label.contains("ナ") {
            try key("board-key").tap()
        }
        if q.label.contains("ア") {
            try key("shift-key").tap()
        }
    }

    @discardableResult
    private func require(_ element: XCUIElement, _ message: String,
                         timeout: TimeInterval = 10,
                         application: XCUIApplication? = nil) throws -> XCUIElement {
        if element.waitForExistence(timeout: timeout) { return element }
        attachHierarchy(application ?? host, name: "Missing required UI element")
        XCTFail(message)
        throw UIError.missing(message)
    }

    private func key(_ identifier: String) throws -> XCUIElement {
        try require(host.descendants(matching: .any)[identifier].firstMatch, "Missing keyboard control: \(identifier)")
    }

    private func expectReading(_ text: String) throws {
        let reading = try key("candidate-reading")
        try waitFor("Draft did not contain \(text)") {
            reading.label.contains(text) || (reading.value as? String)?.contains(text) == true
        }
    }

    private func expectValue(_ text: String, in editor: XCUIElement) throws {
        try waitFor("Expected committed text \(text.debugDescription), got \(String(describing: editor.value))") {
            (editor.value as? String) == text
        }
    }

    private func waitFor(_ message: String, timeout: TimeInterval = 10,
                         condition: @escaping () -> Bool) throws {
        let expectation = XCTNSPredicateExpectation(
            predicate: NSPredicate { _, _ in condition() }, object: nil)
        let result = XCTWaiter.wait(for: [expectation], timeout: timeout)
        if result != .completed {
            attachHierarchy(host, name: "Keyboard assertion failure")
            XCTFail(message)
            throw UIError.missing(message)
        }
    }

    private func screenshot(_ name: String) {
        let attachment = XCTAttachment(screenshot: XCUIScreen.main.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }

    private func attachHierarchy(_ application: XCUIApplication, name: String) {
        let attachment = XCTAttachment(string: application.debugDescription)
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }

    private enum UIError: Error {
        case missing(String)
    }
}
