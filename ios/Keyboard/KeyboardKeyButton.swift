import UIKit

/// A keycap whose touch lifetime is independent of changes to its title or image.
/// Keep repeating keys in the view hierarchy while updating the composition UI.
final class KeyboardKeyButton: UIButton {
    enum Style {
        case character
        case function
        case accent
    }

    var keyStyle: Style {
        didSet { updateAppearance() }
    }

    /// Capture the controller weakly when assigning these callbacks.
    var onPress: (() -> Void)?

    /// The controller owns the preview bubble and keeps it inside the keyboard.
    /// Character keys report true while tracked inside, and false on every exit.
    var onPreviewChange: ((Bool) -> Void)?

    /// Intended for Delete: one action on touch-down, then repeat after a pause.
    /// A release never adds another action. Leaving the key cancels this hold.
    var repeatsOnHold = false {
        didSet {
            if oldValue != repeatsOnHold { cancelInteraction() }
        }
    }

    private var appearanceIsDark = false
    private var hasExplicitAppearance = false
    private var repeatTimer: Timer?
    private var interactionGeneration: UInt = 0
    private var interactionActive = false
    private var fingerInside = false
    private var repeatingGesture = false
    private var holdCancelled = false
    private var releaseAllowed = false
    private var previewVisible = false

    init(style: Style) {
        keyStyle = style
        super.init(frame: .zero)
        configure()
    }

    required init?(coder: NSCoder) {
        keyStyle = .character
        super.init(coder: coder)
        configure()
    }

    deinit {
        repeatTimer?.invalidate()
    }

    private func configure() {
        appearanceIsDark = traitCollection.userInterfaceStyle == .dark
        layer.cornerRadius = 5
        layer.cornerCurve = .continuous
        layer.masksToBounds = false
        layer.shadowOffset = CGSize(width: 0, height: 1)
        layer.shadowRadius = 0
        adjustsImageWhenHighlighted = false
        adjustsImageWhenDisabled = false
        contentEdgeInsets = UIEdgeInsets(top: 0, left: 3, bottom: 0, right: 3)
        titleLabel?.font = .systemFont(ofSize: keyStyle == .character ? 23 : 17)
        titleLabel?.adjustsFontSizeToFitWidth = true
        titleLabel?.minimumScaleFactor = 0.6
        titleLabel?.textAlignment = .center
        accessibilityTraits.insert(.keyboardKey)
        addTarget(self, action: #selector(activateOnRelease), for: .touchUpInside)
        updateAppearance()
    }

    /// An explicit appearance also supports a host field's keyboardAppearance.
    /// When no override is supplied, the key follows its trait collection.
    func applyAppearance(isDark: Bool) {
        hasExplicitAppearance = true
        appearanceIsDark = isDark
        updateAppearance()
    }

    func setSymbol(_ name: String, pointSize: CGFloat = 20,
                   weight: UIImage.SymbolWeight = .regular) {
        let configuration = UIImage.SymbolConfiguration(pointSize: pointSize, weight: weight)
        setImage(UIImage(systemName: name, withConfiguration: configuration), for: .normal)
    }

    override var isHighlighted: Bool {
        didSet { updateAppearance() }
    }

    override var isSelected: Bool {
        didSet { updateAppearance() }
    }

    override var isEnabled: Bool {
        didSet {
            if !isEnabled { cancelInteraction() }
            updateAppearance()
        }
    }

    override var isHidden: Bool {
        didSet {
            if isHidden { cancelInteraction() }
        }
    }

    override func layoutSubviews() {
        super.layoutSubviews()
        layer.shadowPath = UIBezierPath(roundedRect: bounds, cornerRadius: 5).cgPath
    }

    override func traitCollectionDidChange(_ previousTraitCollection: UITraitCollection?) {
        super.traitCollectionDidChange(previousTraitCollection)
        if !hasExplicitAppearance {
            appearanceIsDark = traitCollection.userInterfaceStyle == .dark
        }
        updateAppearance()
    }

    private func updateAppearance() {
        let dark = appearanceIsDark
        let foreground: UIColor
        let resting: UIColor
        let pressed: UIColor
        switch keyStyle {
        case .character:
            foreground = dark ? .white : .black
            resting = dark ? UIColor(white: 0.42, alpha: 1) : .white
            pressed = dark ? UIColor(white: 0.56, alpha: 1) : UIColor(white: 0.82, alpha: 1)
        case .function:
            foreground = isSelected ? .black : (dark ? .white : .black)
            resting = isSelected ? .white : (dark
                ? UIColor(white: 0.27, alpha: 1)
                : UIColor(red: 0.67, green: 0.69, blue: 0.72, alpha: 1))
            pressed = dark ? UIColor(white: 0.52, alpha: 1) : UIColor(white: 0.91, alpha: 1)
        case .accent:
            foreground = .white
            resting = UIColor(red: 0, green: 0.48, blue: 1, alpha: 1)
            pressed = UIColor(red: 0, green: 0.38, blue: 0.82, alpha: 1)
        }
        backgroundColor = isHighlighted ? pressed : resting
        tintColor = foreground
        setTitleColor(foreground, for: .normal)
        setTitleColor(foreground, for: .highlighted)
        setTitleColor(foreground, for: .selected)
        setTitleColor(foreground, for: [.selected, .highlighted])
        setTitleColor(foreground.withAlphaComponent(0.45), for: .disabled)
        layer.shadowColor = (dark ? UIColor.black : UIColor(white: 0.35, alpha: 1)).cgColor
        layer.shadowOpacity = isHighlighted ? 0.15 : (dark ? 0.7 : 0.55)
        alpha = isEnabled ? 1 : 0.45
    }

    override func beginTracking(_ touch: UITouch, with event: UIEvent?) -> Bool {
        guard isEnabled, super.beginTracking(touch, with: event) else { return false }
        stopTimer()
        interactionGeneration &+= 1
        interactionActive = true
        fingerInside = bounds.contains(touch.location(in: self))
        repeatingGesture = repeatsOnHold
        holdCancelled = false
        releaseAllowed = false
        updatePreview()
        if repeatingGesture && fingerInside {
            // Arm first: onPress may synchronously hide/remove this key, which
            // cancels the timer through the view-lifecycle overrides below.
            startRepeatDelay(generation: interactionGeneration)
            onPress?()
        }
        return true
    }

    override func continueTracking(_ touch: UITouch, with event: UIEvent?) -> Bool {
        let accepted = super.continueTracking(touch, with: event)
        fingerInside = bounds.contains(touch.location(in: self))
        if !fingerInside || !accepted {
            holdCancelled = true
            stopTimer()
        }
        if !accepted { interactionActive = false }
        updatePreview()
        return accepted
    }

    override func endTracking(_ touch: UITouch?, with event: UIEvent?) {
        let endedInside = touch.map { bounds.contains($0.location(in: self)) } ?? false
        releaseAllowed = interactionActive && endedInside && isEnabled && !repeatingGesture
        finishTracking()
        super.endTracking(touch, with: event)
        isHighlighted = false
    }

    override func cancelTracking(with event: UIEvent?) {
        releaseAllowed = false
        finishTracking()
        super.cancelTracking(with: event)
        isHighlighted = false
    }

    /// Also call this from the keyboard controller's viewWillDisappear: a
    /// keyboard can disappear without UIKit immediately detaching every key.
    func cancelInteraction() {
        cancelTracking(with: nil)
    }

    override func didMoveToWindow() {
        super.didMoveToWindow()
        if window == nil { cancelInteraction() }
    }

    override func didMoveToSuperview() {
        super.didMoveToSuperview()
        if superview == nil { cancelInteraction() }
    }

    override func accessibilityActivate() -> Bool {
        guard isEnabled, let onPress = onPress else { return super.accessibilityActivate() }
        cancelInteraction()
        onPress()
        return true
    }

    @objc private func activateOnRelease() {
        guard releaseAllowed, isEnabled else { return }
        releaseAllowed = false
        onPress?()
    }

    private func finishTracking() {
        stopTimer()
        interactionGeneration &+= 1
        interactionActive = false
        fingerInside = false
        holdCancelled = true
        updatePreview()
    }

    private func updatePreview() {
        let visible = interactionActive && fingerInside && !repeatingGesture && keyStyle == .character
        guard visible != previewVisible else { return }
        previewVisible = visible
        onPreviewChange?(visible)
    }

    private func canRepeat(generation: UInt) -> Bool {
        interactionGeneration == generation && interactionActive && fingerInside &&
            repeatingGesture && !holdCancelled && isEnabled && !isHidden && window != nil
    }

    private func startRepeatDelay(generation: UInt) {
        let timer = Timer(timeInterval: 0.4, repeats: false) { [weak self] _ in
            guard let self = self, self.canRepeat(generation: generation) else { return }
            self.stopTimer()
            self.onPress?()
            guard self.canRepeat(generation: generation) else { return }
            let repeating = Timer(timeInterval: 0.075, repeats: true) { [weak self] timer in
                guard let self = self, self.canRepeat(generation: generation) else {
                    timer.invalidate()
                    return
                }
                self.onPress?()
            }
            self.repeatTimer = repeating
            RunLoop.main.add(repeating, forMode: .common)
        }
        repeatTimer = timer
        RunLoop.main.add(timer, forMode: .common)
    }

    private func stopTimer() {
        repeatTimer?.invalidate()
        repeatTimer = nil
    }
}
