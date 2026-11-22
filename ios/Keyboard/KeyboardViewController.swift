import UIKit
import KanaCore

/// Let a drag that starts on a candidate cancel its tap and scroll the strip.
private final class CandidateScrollView: UIScrollView {
    override func touchesShouldCancel(in view: UIView) -> Bool { true }
}

/// Composition stays inside the extension until explicitly committed.
final class KeyboardViewController: UIInputViewController, UIGestureRecognizerDelegate {
    private enum Panel { case kana, numbers, symbols, emoji }
    private var panel: Panel = .kana
    private var board = 0
    private var katakana = false
    private var composing = ""
    private var candidates: [Candidate] = []
    private var candidateGeneration = 0
    private var candidateWork: DispatchWorkItem?
    private let conversionQueue = DispatchQueue(label: "dev.kana23x2.conversion", qos: .userInitiated)
    private lazy var engine = CandidateEngine(dictionaryURL:
        Bundle.main.url(forResource: "skk_compact", withExtension: "tsv") ??
        Bundle.main.url(forResource: "modern_terms", withExtension: "tsv"))

    private let vertical = UIStackView()
    private let header = UIView()
    private let draftButton = UIButton(type: .system)
    private let candidateScroll = CandidateScrollView()
    private let candidateRow = UIStackView()
    private let expandButton = UIButton(type: .system)
    private let keyRows = UIStackView()
    private let bottomRow = UIStackView()
    private let expandedScroll = CandidateScrollView()
    private let expandedRows = UIStackView()
    private let previewLabel = UILabel()
    private weak var previewKey: KeyboardKeyButton?
    private var spaceKey: KeyboardKeyButton?
    private var returnKey: KeyboardKeyButton?
    private var panelKey: KeyboardKeyButton?
    private var keyboardHeight: NSLayoutConstraint?
    private var headerHeight: NSLayoutConstraint?
    private var bottomHeight: NSLayoutConstraint?
    private var expanded = false
    private var symbolPage = 0
    private var emojiPage = 0
    private var cursorMode = false
    private var cursorX: CGFloat = 0
    private var lastLayoutWidth: CGFloat = 0
    private var lastNeedsGlobe: Bool?
    private var landscape: Bool { view.bounds.width > 500 && traitCollection.userInterfaceIdiom == .phone }
    private var isDark: Bool {
        if let appearance = textDocumentProxy.keyboardAppearance, appearance != .default {
            return appearance == .dark
        }
        return traitCollection.userInterfaceStyle == .dark
    }
    private var keyColor: UIColor { isDark ? UIColor(white: 0.40, alpha: 1) : .white }
    private var textColor: UIColor { isDark ? .white : .black }
    private let symbolPages: [[String]] = [
        ["、", "。", "！", "？", "・", "「", "」", "『", "』", "〜",
         "（", "）", "［", "］", "【", "】", "〈", "〉", "《", "》",
         "…", "—", "：", "；", "／", "＼", "＠", "＃", "＆", "％"],
        ["＋", "−", "×", "÷", "＝", "≠", "≈", "≤", "≥", "∞",
         "√", "∑", "∫", "π", "θ", "α", "β", "γ", "δ", "Ω",
         "←", "↑", "→", "↓", "↔", "⇒", "★", "☆", "○", "●"],
        ["¥", "￥", "$", "€", "£", "₩", "℃", "℉", "°", "‰",
         "㎡", "㎥", "㎝", "㎞", "㎏", "㎎", "㎐", "№", "©", "®",
         "ə", "ɪ", "ʊ", "ʃ", "ʒ", "ŋ", "ɲ", "ʔ", "ゝ", "ゞ"]
    ]
    private let emojiPages: [[String]] = [
        ["😀", "😃", "😄", "😁", "😆", "😅", "😂", "🤣", "😊", "🙂",
         "🙃", "😉", "😍", "🥰", "😘", "😋", "😎", "🤔", "🥺", "😭",
         "👍", "👎", "👌", "✌️", "🤞", "👋", "👏", "🙌", "🙏", "💪"],
        ["❤️", "🧡", "💛", "💚", "💙", "💜", "🖤", "🤍", "💔", "💕",
         "✨", "🔥", "💯", "✅", "❌", "⭕", "❗", "❓", "🚀", "🎉",
         "😺", "🐶", "🐼", "🌸", "🌈", "☀️", "🌙", "🍀", "🍵", "🍙"]
    ]


    override func viewDidLoad() {
        super.viewDidLoad()
        view.accessibilityIdentifier = "kanaKeyboard"
        vertical.axis = .vertical
        vertical.spacing = 8
        vertical.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(vertical)
        NSLayoutConstraint.activate([
            vertical.topAnchor.constraint(equalTo: view.topAnchor, constant: 4),
            vertical.leadingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.leadingAnchor, constant: 4),
            vertical.trailingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.trailingAnchor, constant: -4),
            vertical.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor, constant: -6)
        ])
        keyboardHeight = view.heightAnchor.constraint(equalToConstant: 272)
        keyboardHeight?.priority = .init(999)
        keyboardHeight?.isActive = true
        configureHeader()
        vertical.addArrangedSubview(header)
        headerHeight = header.heightAnchor.constraint(equalToConstant: 54)
        headerHeight?.isActive = true

        keyRows.axis = .vertical
        keyRows.spacing = 8
        keyRows.distribution = .fillEqually
        vertical.addArrangedSubview(keyRows)

        expandedRows.axis = .vertical
        expandedRows.spacing = 1
        expandedRows.translatesAutoresizingMaskIntoConstraints = false
        expandedScroll.addSubview(expandedRows)
        NSLayoutConstraint.activate([
            expandedRows.topAnchor.constraint(equalTo: expandedScroll.contentLayoutGuide.topAnchor),
            expandedRows.bottomAnchor.constraint(equalTo: expandedScroll.contentLayoutGuide.bottomAnchor),
            expandedRows.leadingAnchor.constraint(equalTo: expandedScroll.contentLayoutGuide.leadingAnchor),
            expandedRows.trailingAnchor.constraint(equalTo: expandedScroll.contentLayoutGuide.trailingAnchor),
            expandedRows.widthAnchor.constraint(equalTo: expandedScroll.frameLayoutGuide.widthAnchor)
        ])
        expandedScroll.isHidden = true
        vertical.addArrangedSubview(expandedScroll)
        bottomRow.axis = .horizontal
        bottomRow.spacing = 6
        bottomHeight = bottomRow.heightAnchor.constraint(equalToConstant: 44)
        bottomHeight?.isActive = true
        vertical.addArrangedSubview(bottomRow)

        previewLabel.textAlignment = .center
        previewLabel.font = .systemFont(ofSize: 32)
        previewLabel.layer.cornerRadius = 8
        previewLabel.layer.shadowColor = UIColor.black.cgColor
        previewLabel.layer.shadowOpacity = 0.18
        previewLabel.layer.shadowRadius = 3
        previewLabel.layer.shadowOffset = CGSize(width: 0, height: 2)
        previewLabel.isUserInteractionEnabled = false
        previewLabel.isAccessibilityElement = false
        previewLabel.isHidden = true
        view.addSubview(previewLabel)
        rebuildKeys()
        refreshCandidates()
    }

    private func configureHeader() {
        for item in [draftButton, candidateScroll, expandButton] {
            item.translatesAutoresizingMaskIntoConstraints = false
            header.addSubview(item)
        }
        draftButton.titleLabel?.font = .systemFont(ofSize: 13)
        draftButton.titleLabel?.lineBreakMode = .byTruncatingHead
        draftButton.contentHorizontalAlignment = .left
        draftButton.contentEdgeInsets = UIEdgeInsets(top: 0, left: 8, bottom: 0, right: 8)
        draftButton.accessibilityIdentifier = "candidate-reading"
        draftButton.addTarget(self, action: #selector(confirmDraft), for: .touchUpInside)
        candidateScroll.showsHorizontalScrollIndicator = false
        candidateScroll.alwaysBounceHorizontal = true
        candidateScroll.delaysContentTouches = false
        candidateRow.axis = .horizontal
        candidateRow.alignment = .fill
        candidateRow.translatesAutoresizingMaskIntoConstraints = false
        candidateScroll.addSubview(candidateRow)
        expandButton.addTarget(self, action: #selector(toggleExpanded), for: .touchUpInside)
        expandButton.accessibilityIdentifier = "candidate-expand-key"
        NSLayoutConstraint.activate([
            draftButton.topAnchor.constraint(equalTo: header.topAnchor),
            draftButton.leadingAnchor.constraint(equalTo: header.leadingAnchor),
            draftButton.trailingAnchor.constraint(equalTo: expandButton.leadingAnchor),
            draftButton.heightAnchor.constraint(equalToConstant: 18),
            candidateScroll.topAnchor.constraint(equalTo: draftButton.bottomAnchor),
            candidateScroll.leadingAnchor.constraint(equalTo: header.leadingAnchor),
            candidateScroll.trailingAnchor.constraint(equalTo: expandButton.leadingAnchor),
            candidateScroll.bottomAnchor.constraint(equalTo: header.bottomAnchor),
            expandButton.topAnchor.constraint(equalTo: header.topAnchor),
            expandButton.trailingAnchor.constraint(equalTo: header.trailingAnchor),
            expandButton.bottomAnchor.constraint(equalTo: header.bottomAnchor),
            expandButton.widthAnchor.constraint(equalToConstant: 40),
            candidateRow.topAnchor.constraint(equalTo: candidateScroll.contentLayoutGuide.topAnchor),
            candidateRow.leadingAnchor.constraint(equalTo: candidateScroll.contentLayoutGuide.leadingAnchor),
            candidateRow.trailingAnchor.constraint(equalTo: candidateScroll.contentLayoutGuide.trailingAnchor),
            candidateRow.bottomAnchor.constraint(equalTo: candidateScroll.contentLayoutGuide.bottomAnchor),
            candidateRow.heightAnchor.constraint(equalTo: candidateScroll.frameLayoutGuide.heightAnchor)
        ])
    }

    override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        updateMetrics()
        if lastNeedsGlobe != needsInputModeSwitchKey {
            lastNeedsGlobe = needsInputModeSwitchKey
            rebuildBottomRow()
        }
        if abs(view.bounds.width - lastLayoutWidth) > 1 {
            lastLayoutWidth = view.bounds.width
            hidePreview()
        }
    }

    private func updateMetrics() {
        let contentHeight: CGFloat = landscape ? 218 : (traitCollection.userInterfaceIdiom == .pad ? 312 : 272)
        let height = contentHeight + view.safeAreaInsets.bottom
        if keyboardHeight?.constant != height { keyboardHeight?.constant = height }
        headerHeight?.constant = landscape ? 44 : 54
        bottomHeight?.constant = landscape ? 34 : 44
        vertical.spacing = landscape ? 6 : 8
        keyRows.spacing = landscape ? 6 : 8
    }

    override func viewSafeAreaInsetsDidChange() {
        super.viewSafeAreaInsetsDidChange()
        if isViewLoaded { updateMetrics() }
    }

    override func traitCollectionDidChange(_ previousTraitCollection: UITraitCollection?) {
        super.traitCollectionDidChange(previousTraitCollection)
        if isViewLoaded {
            cancelInteractions()
            updateMetrics()
            applyAppearance()
        }
    }

    override func viewWillDisappear(_ animated: Bool) {
        cancelInteractions()
        resetDraft()
        super.viewWillDisappear(animated)
    }

    override func textDidChange(_ textInput: UITextInput?) {
        super.textDidChange(textInput)
        if !composing.isEmpty { resetDraft() }
        if isViewLoaded { updateBottomLabels(); applyAppearance() }
    }

    override func selectionDidChange(_ textInput: UITextInput?) {
        super.selectionDidChange(textInput)
        if !composing.isEmpty { resetDraft() }
    }

    private func clear(_ stack: UIStackView) {
        stack.arrangedSubviews.forEach { stack.removeArrangedSubview($0); $0.removeFromSuperview() }
    }

    private func walk(_ root: UIView, _ action: (UIView) -> Void) {
        action(root)
        root.subviews.forEach { walk($0, action) }
    }

    private func applyAppearance() {
        view.backgroundColor = isDark ? UIColor(white: 0.16, alpha: 1) : UIColor(red: 0.82, green: 0.83, blue: 0.85, alpha: 1)
        walk(vertical) { item in
            if let key = item as? KeyboardKeyButton { key.applyAppearance(isDark: isDark) }
        }
        draftButton.setTitleColor(textColor.withAlphaComponent(0.65), for: .normal)
        expandButton.tintColor = textColor
        previewLabel.textColor = textColor
        previewLabel.layer.backgroundColor = keyColor.cgColor
        renderCandidates()
    }

    private func button(_ title: String, id: String? = nil, accessibility: String? = nil,
                        modifier: Bool = false, preview: Bool = false,
                        action: @escaping (KeyboardViewController) -> Void) -> KeyboardKeyButton {
        let key = KeyboardKeyButton(style: modifier ? .function : .character)
        key.setTitle(title, for: .normal)
        key.titleLabel?.font = .systemFont(ofSize: 21)
        key.titleLabel?.adjustsFontSizeToFitWidth = true
        key.titleLabel?.minimumScaleFactor = 0.6
        key.accessibilityIdentifier = id
        key.accessibilityLabel = accessibility ?? title
        key.onPress = { [weak self] in
            guard let self = self else { return }
            action(self)
        }
        if preview {
            key.onPreviewChange = { [weak self, weak key] visible in
                guard let self = self, let key = key else { return }
                if visible { self.showPreview(for: key, text: title) }
                else if self.previewKey === key { self.hidePreview() }
            }
        }
        key.applyAppearance(isDark: isDark)
        return key
    }

    private func showPreview(for key: KeyboardKeyButton, text: String) {
        guard !UIAccessibility.isVoiceOverRunning else { return }
        let rect = key.convert(key.bounds, to: view)
        let width = max(50, rect.width + 16)
        previewLabel.frame = CGRect(
            x: min(max(2, rect.midX - width / 2), view.bounds.width - width - 2),
            y: max(2, rect.minY - 49), width: width, height: 48)
        previewLabel.text = text
        previewLabel.layer.backgroundColor = keyColor.cgColor
        previewLabel.textColor = textColor
        previewLabel.isHidden = false
        previewKey = key
        view.bringSubviewToFront(previewLabel)
    }

    private func hidePreview() {
        previewLabel.isHidden = true
        previewKey = nil
    }

    private func cancelInteractions() {
        walk(vertical) { ($0 as? KeyboardKeyButton)?.cancelInteraction() }
        for gesture in spaceKey?.gestureRecognizers ?? [] where gesture is UILongPressGestureRecognizer {
            gesture.isEnabled = false
            gesture.isEnabled = true
        }
        hidePreview()
        endCursorMode()
    }

    private func kanaButton(_ index: Int) -> KeyboardKeyButton {
        let kana = KanaLayout.kana(index: index, board: board, katakana: katakana)
        let keyName = KanaLayout.keys[index]
        let key = button(kana, id: "kana-key-\(keyName)", accessibility: "\(kana), \(keyName)", preview: true) { controller in
            controller.append(KanaLayout.kana(index: index, board: controller.board, katakana: controller.katakana))
        }
        key.titleLabel?.font = .systemFont(ofSize: 25, weight: .regular)
        return key
    }

    private func addWeighted(_ elements: [(UIView, CGFloat)], to stack: UIStackView) {
        guard let first = elements.first else { return }
        for (item, weight) in elements {
            stack.addArrangedSubview(item)
            if item !== first.0 {
                item.widthAnchor.constraint(equalTo: first.0.widthAnchor, multiplier: weight / first.1).isActive = true
            }
        }
    }

    private func row(_ elements: [(UIView, CGFloat)]) {
        let line = UIStackView()
        line.axis = .horizontal
        line.spacing = 6
        addWeighted(elements, to: line)
        keyRows.addArrangedSubview(line)
    }

    private func rebuildKeys() {
        cancelInteractions()
        clear(keyRows)
        switch panel {
        case .kana:
            row((0..<10).map { (kanaButton($0), 1) })
            row([(UIView(), 0.5)] + (10..<19).map { (kanaButton($0), 1) } + [(UIView(), 0.5)])
            let shift = button("", id: "shift-key", accessibility: "切换平假名与片假名", modifier: true) {
                $0.katakana.toggle(); $0.rebuildKeys()
            }
            shift.setSymbol(katakana ? "shift.fill" : "shift")
            shift.accessibilityValue = katakana ? "片假名" : "平假名"
            shift.keyStyle = katakana ? .character : .function
            let z = button("Z", id: "board-key", accessibility: "Z，切换假名板", modifier: true) {
                $0.board ^= 1; $0.rebuildKeys()
            }
            z.accessibilityValue = "第 \(board + 1) 板"
            z.keyStyle = board == 1 ? .character : .function
            let x = button("゛", id: "dakuten-key", accessibility: "X，浊音", modifier: true) { $0.transform(KanaTransforms.toggleDakuten) }
            let c = button(board == 0 ? "小" : "゜", id: "small-handakuten-key",
                           accessibility: board == 0 ? "C，小假名" : "C，半浊音", modifier: true) {
                $0.transform($0.board == 0 ? KanaTransforms.toggleSmall : KanaTransforms.toggleHandakuten)
            }
            row([(shift, 1.5), (z, 1), (x, 1), (c, 1)] +
                (19..<23).map { (kanaButton($0), 1) } + [(deleteButton(), 1.5)])
        case .numbers:
            row(Array("1234567890").map { (literalButton(String($0)), 1) })
            row(["-", "/", ":", ";", "(", ")", "¥", "@", "「", "」"].map { (literalButton($0), 1) })
            row([(symbolToggle(), 1.5)] + ["。", "、", "？", "！", "・"].map { (literalButton($0), 1.4) } + [(deleteButton(), 1.5)])
        case .symbols:
            let values = symbolPages[symbolPage]
            row(values[0..<10].map { (literalButton($0), 1) })
            row(values[10..<20].map { (literalButton($0), 1) })
            row(values[20..<30].map { (literalButton($0), 1) })
        case .emoji:
            let values = emojiPages[emojiPage]
            row(values[0..<10].map { (literalButton($0, preview: false), 1) })
            row(values[10..<20].map { (literalButton($0, preview: false), 1) })
            row(values[20..<30].map { (literalButton($0, preview: false), 1) })
        }
        rebuildBottomRow()
        updateHeader()
        applyAppearance()
    }

    private func symbolToggle() -> KeyboardKeyButton {
        button(panel == .numbers ? "#+=" : "123", id: "symbols-toggle-key",
               accessibility: panel == .numbers ? "符号键盘" : "数字键盘", modifier: true) {
            $0.show($0.panel == .numbers ? .symbols : .numbers)
        }
    }

    private func rebuildBottomRow() {
        clear(bottomRow)
        let toggle = button(panel == .kana ? "123" : "かな", id: "panel-toggle-key",
                            accessibility: panel == .kana ? "数字键盘" : "假名键盘", modifier: true) {
            if $0.expanded { $0.setExpanded(false) }
            else { $0.show($0.panel == .kana ? .numbers : .kana) }
        }
        toggle.titleLabel?.font = .systemFont(ofSize: 16)
        panelKey = toggle
        var elements: [(UIView, CGFloat)] = [(toggle, 1.3)]
        if needsInputModeSwitchKey {
            let globe = button("", id: "globe-key", accessibility: "切换键盘", modifier: true) { _ in }
            globe.onPress = nil
            globe.setSymbol("globe")
            globe.addTarget(self, action: #selector(handleInputModeList(from:with:)), for: .allTouchEvents)
            elements.append((globe, 1))
        }
        let emoji = button("", id: "emoji-key", accessibility: panel == .emoji ? "下一页表情" : "表情键盘", modifier: true) {
            if $0.panel == .emoji { $0.emojiPage = ($0.emojiPage + 1) % $0.emojiPages.count; $0.rebuildKeys() }
            else { $0.show(.emoji) }
        }
        emoji.setSymbol(panel == .emoji ? "arrow.triangle.2.circlepath" : "face.smiling")
        if panel == .symbols {
            elements.append((symbolToggle(), 1.2))
            let more = button("", id: "symbol-page-key", accessibility: "更多符号", modifier: true) {
                $0.symbolPage = ($0.symbolPage + 1) % $0.symbolPages.count; $0.rebuildKeys()
            }
            more.setSymbol("arrow.triangle.2.circlepath", pointSize: 17)
            elements.append((more, 1))
        } else {
            elements.append((emoji, 1))
        }
        if panel == .kana { elements.append((button("ー", accessibility: "长音", preview: true) { $0.append("ー") }, 1)) }
        let space = button("空白", id: "space-key", accessibility: "空格，长按拖动可移动光标") { $0.space() }
        space.titleLabel?.font = .systemFont(ofSize: 17)
        let press = UILongPressGestureRecognizer(target: self, action: #selector(moveCursor(_:)))
        press.minimumPressDuration = 0.4
        press.delegate = self
        space.addGestureRecognizer(press)
        spaceKey = space
        elements.append((space, panel == .kana ? 3.4 : 4.4))
        if panel == .kana { elements.append((literalButton("、"), 0.9)) }
        if panel == .emoji || panel == .symbols { elements.append((deleteButton(), 1)) }
        let enter = button("", id: "return-key", accessibility: "回车", modifier: true) { $0.enter() }
        enter.titleLabel?.font = .systemFont(ofSize: 16)
        returnKey = enter
        elements.append((enter, 1.6))
        addWeighted(elements, to: bottomRow)
        updateBottomLabels()
    }

    private func updateBottomLabels() {
        spaceKey?.setTitle(cursorMode ? "移动光标" : (composing.isEmpty ? "空白" : "変換"), for: .normal)
        returnKey?.setTitle(returnLabel, for: .normal)
        returnKey?.accessibilityLabel = composing.isEmpty ? returnLabel : "确定假名"
        let special = composing.isEmpty && (textDocumentProxy.returnKeyType ?? .default) != .default
        returnKey?.keyStyle = special ? .accent : .function
        returnKey?.applyAppearance(isDark: isDark)
        if expanded {
            panelKey?.setTitle(katakana ? "カナ" : "かな", for: .normal)
            panelKey?.accessibilityLabel = "收起候选，返回键盘"
        } else {
            panelKey?.setTitle(panel == .kana ? "123" : (katakana ? "カナ" : "かな"), for: .normal)
            panelKey?.accessibilityLabel = panel == .kana ? "数字键盘" : "假名键盘"
        }
    }

    private var returnLabel: String {
        if !composing.isEmpty { return "確定" }
        switch textDocumentProxy.returnKeyType ?? .default {
        case .go: return "前往"
        case .search, .google, .yahoo: return "搜索"
        case .send: return "发送"
        case .done: return "完成"
        case .next: return "下一项"
        default: return "换行"
        }
    }

    private func deleteButton() -> KeyboardKeyButton {
        let key = button("", id: "delete-key", accessibility: "删除，按住连续删除", modifier: true) { $0.backspace() }
        key.setSymbol("delete.left")
        key.repeatsOnHold = true
        return keyWithAppearance(key)
    }

    private func keyWithAppearance(_ key: KeyboardKeyButton) -> KeyboardKeyButton {
        key.applyAppearance(isDark: isDark)
        return key
    }

    private func literalButton(_ value: String, preview: Bool = true) -> KeyboardKeyButton {
        button(value, id: "literal-\(value)", preview: preview) { $0.commitLiteral(value) }
    }

    private func show(_ next: Panel) {
        setExpanded(false)
        panel = next
        rebuildKeys()
    }

    @objc private func toggleExpanded() {
        guard !composing.isEmpty else { return }
        setExpanded(!expanded)
    }

    private func setExpanded(_ value: Bool) {
        if expanded != value { cancelInteractions() }
        expanded = value
        keyRows.isHidden = value
        expandedScroll.isHidden = !value
        hidePreview()
        updateHeader()
        updateBottomLabels()
        renderCandidates()
    }

    private func updateHeader() {
        let title = composing.isEmpty ? "\(katakana ? "カタカナ" : "ひらがな")  ·  \(board + 1)" : composing
        draftButton.setTitle(title, for: .normal)
        draftButton.accessibilityLabel = composing.isEmpty ? "没有待确定的输入" : "待确定假名：\(composing)，点按原样确定"
        draftButton.accessibilityValue = composing
        draftButton.isEnabled = !composing.isEmpty
        expandButton.setImage(UIImage(systemName: expanded ? "chevron.up" : "chevron.down",
                                     withConfiguration: UIImage.SymbolConfiguration(pointSize: 18, weight: .medium)), for: .normal)
        expandButton.isEnabled = !composing.isEmpty
        expandButton.alpha = composing.isEmpty ? 0.25 : 1
        expandButton.accessibilityLabel = expanded ? "收起候选" : "展开候选"
    }

    private func append(_ text: String) {
        composing += text
        refreshCandidates()
    }

    private func transform(_ operation: (String) -> String) {
        guard !composing.isEmpty else { return }
        composing = operation(composing)
        refreshCandidates()
    }

    private func backspace() {
        if composing.isEmpty { textDocumentProxy.deleteBackward() }
        else { composing.removeLast(); refreshCandidates() }
    }

    private func space() {
        guard !composing.isEmpty else { textDocumentProxy.insertText(" "); return }
        commit(candidates.first?.text ?? composing)
    }

    private func enter() {
        if composing.isEmpty { textDocumentProxy.insertText("\n") }
        else { commit(composing) }
    }

    @objc private func confirmDraft() {
        guard !composing.isEmpty else { return }
        commit(composing)
    }

    private func commitLiteral(_ text: String) { commit(composing + text) }

    private func commit(_ text: String) {
        resetDraft()
        textDocumentProxy.insertText(text)
    }

    private func resetDraft() {
        composing = ""
        expanded = false
        if isViewLoaded {
            keyRows.isHidden = false
            expandedScroll.isHidden = true
            refreshCandidates()
        }
    }

    private func refreshCandidates() {
        candidateGeneration += 1
        candidateWork?.cancel()
        let reading = composing
        candidates = reading.isEmpty ? [] : [Candidate(text: reading, sourceReading: reading)]
        if reading.isEmpty && expanded { setExpanded(false) }
        updateHeader()
        updateBottomLabels()
        renderCandidates()
        candidateScroll.setContentOffset(.zero, animated: false)
        guard !reading.isEmpty else { return }
        let generation = candidateGeneration
        let work = DispatchWorkItem { [weak self] in
            guard let self = self else { return }
            let values = self.engine.candidates(reading: reading, limit: 32)
            DispatchQueue.main.async { [weak self] in
                guard let self = self, self.candidateGeneration == generation, self.composing == reading else { return }
                self.candidates = values
                self.renderCandidates()
            }
        }
        candidateWork = work
        conversionQueue.async(execute: work)
    }

    private func candidateButton(_ candidate: Candidate, index: Int) -> UIButton {
        let key = UIButton(type: .system)
        key.setTitle(candidate.text, for: .normal)
        key.setTitleColor(textColor, for: .normal)
        key.titleLabel?.font = .systemFont(ofSize: 20)
        key.titleLabel?.lineBreakMode = .byTruncatingTail
        key.contentEdgeInsets = UIEdgeInsets(top: 0, left: 16, bottom: 0, right: 16)
        key.accessibilityIdentifier = "candidate-\(index)"
        key.accessibilityLabel = candidate.note.isEmpty ? candidate.text : "\(candidate.text)，\(candidate.note)"
        let generation = candidateGeneration
        key.addAction(UIAction { [weak self] _ in
            guard let self = self, self.candidateGeneration == generation else { return }
            self.commit(candidate.text)
        }, for: .touchUpInside)
        return key
    }

    private func renderCandidates() {
        clear(candidateRow)
        clear(expandedRows)
        if composing.isEmpty {
            for mark in ["、", "。", "？", "！"] {
                let shortcut = UIButton(type: .system)
                shortcut.setTitle(mark, for: .normal)
                shortcut.titleLabel?.font = .systemFont(ofSize: 20)
                shortcut.setTitleColor(textColor.withAlphaComponent(0.7), for: .normal)
                shortcut.widthAnchor.constraint(equalToConstant: 48).isActive = true
                shortcut.accessibilityLabel = mark
                shortcut.addAction(UIAction { [weak self] _ in self?.commitLiteral(mark) }, for: .touchUpInside)
                candidateRow.addArrangedSubview(shortcut)
            }
        }
        for (index, candidate) in candidates.enumerated() {
            let key = candidateButton(candidate, index: index)
            key.setContentCompressionResistancePriority(.required, for: .horizontal)
            candidateRow.addArrangedSubview(key)
            let separator = UIView()
            separator.backgroundColor = textColor.withAlphaComponent(0.12)
            separator.widthAnchor.constraint(equalToConstant: 0.5).isActive = true
            candidateRow.addArrangedSubview(separator)
        }
        guard expanded else { return }
        for start in stride(from: 0, to: candidates.count, by: 3) {
            let line = UIStackView()
            line.axis = .horizontal
            line.distribution = .fillEqually
            line.heightAnchor.constraint(equalToConstant: 46).isActive = true
            for index in start..<min(start + 3, candidates.count) {
                line.addArrangedSubview(candidateButton(candidates[index], index: index))
            }
            while line.arrangedSubviews.count < 3 { line.addArrangedSubview(UIView()) }
            expandedRows.addArrangedSubview(line)
        }
    }

    func gestureRecognizerShouldBegin(_ gestureRecognizer: UIGestureRecognizer) -> Bool {
        composing.isEmpty && !expanded
    }

    @objc private func moveCursor(_ gesture: UILongPressGestureRecognizer) {
        switch gesture.state {
        case .began:
            cursorMode = true
            cursorX = gesture.location(in: view).x
            keyRows.alpha = 0.4
            spaceKey?.cancelInteraction()
            updateBottomLabels()
        case .changed:
            guard cursorMode else { return }
            let x = gesture.location(in: view).x
            let steps = Int((x - cursorX) / 9)
            if steps != 0 {
                textDocumentProxy.adjustTextPosition(byCharacterOffset: steps)
                cursorX += CGFloat(steps) * 9
            }
        case .ended, .cancelled, .failed:
            endCursorMode()
        default: break
        }
    }

    private func endCursorMode() {
        cursorMode = false
        keyRows.alpha = 1
        updateBottomLabels()
    }
}
