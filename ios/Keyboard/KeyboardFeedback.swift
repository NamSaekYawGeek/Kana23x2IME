import UIKit

/// Opt in to the documented system keyboard click, including the user's sound
/// preference. Never substitute undocumented sound IDs or a custom audio session.
final class KeyboardInputView: UIInputView, UIInputViewAudioFeedback {
    var enableInputClicksWhenVisible: Bool { true }
}

/// Preferences belong to the extension; no shared container or network is used.
final class KeyboardFeedback {
    private enum Preference {
        static let sound = "keyboard.feedback.sound"
        static let haptics = "keyboard.feedback.haptics"
    }

    private let defaults: UserDefaults
    private weak var keyboardView: UIView?
    private let hasFullAccess: () -> Bool
    private let impact: UIImpactFeedbackGenerator

    init(view: UIView, defaults: UserDefaults = .standard,
         hasFullAccess: @escaping () -> Bool) {
        keyboardView = view
        self.defaults = defaults
        self.hasFullAccess = hasFullAccess
        defaults.register(defaults: [Preference.sound: true, Preference.haptics: true])
        if #available(iOS 17.5, *) {
            impact = UIImpactFeedbackGenerator(style: .light, view: view)
        } else {
            impact = UIImpactFeedbackGenerator(style: .light)
        }
    }

    var soundEnabled: Bool {
        get { defaults.bool(forKey: Preference.sound) }
        set { defaults.set(newValue, forKey: Preference.sound) }
    }

    var hapticsEnabled: Bool {
        get { defaults.bool(forKey: Preference.haptics) }
        set { defaults.set(newValue, forKey: Preference.haptics) }
    }

    private var isAvailable: Bool { hasFullAccess() && keyboardView?.window != nil }

    func prepare() {
        guard isAvailable, hapticsEnabled else { return }
        impact.prepare()
    }

    /// Called once on touch-down, on each repeat-delete, or on an accepted
    /// candidate/VoiceOver activation. Touch-up must not play a second click.
    func keyPressed() {
        guard isAvailable else { return }
        if soundEnabled { UIDevice.current.playInputClick() }
        pulse(intensity: 0.35)
    }

    /// Trackpad entry is tactile, without inserting a space or playing a click.
    func cursorModeEntered() {
        guard isAvailable else { return }
        pulse(intensity: 0.5)
    }

    private func pulse(intensity: CGFloat) {
        guard hapticsEnabled else { return }
        impact.impactOccurred(intensity: intensity)
        impact.prepare()
    }
}
