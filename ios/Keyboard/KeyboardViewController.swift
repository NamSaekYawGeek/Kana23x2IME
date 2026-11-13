import UIKit
import KanaCore

/// The draft stays in the keyboard until confirmed. No partially supported
/// marked-text APIs or edits to already committed host text are required.
final class KeyboardViewController: UIInputViewController {
    private enum Panel { case kana, numbers, symbols, emoji }
    private var panel: Panel = .kana
    private var board = 0
    private var katakana = false
    private var composing = ""
    private var candidates: [Candidate] = []
    private var candidateGeneration = 0
    private var candidateWork: DispatchWorkItem?
    private let conversionQueue = DispatchQueue(label: "dev.kana23x2.conversion", qos: .userInitiated)
    // Access only on conversionQueue; the large dictionary remains memory mapped.
    private lazy var engine = CandidateEngine(dictionaryURL:
        Bundle.main.url(forResource: "skk_compact", withExtension: "tsv") ??
        Bundle.main.url(forResource: "modern_terms", withExtension: "tsv"))

    private let vertical = UIStackView()
    private let draftButton = UIButton(type: .system)
    private let candidateScroll = UIScrollView()
    private let candidateRow = UIStackView()
    private let toolbar = UIStackView()
    private let keyRows = UIStackView()
    private let bottomRow = UIStackView()
    private var keyboardHeight: NSLayoutConstraint?
    private var symbolPage = 0
    private var emojiPage = 0

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
        view.backgroundColor = .systemGray5
        vertical.axis = .vertical
        vertical.spacing = 4
        vertical.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(vertical)
        NSLayoutConstraint.activate([
            vertical.topAnchor.constraint(equalTo: view.topAnchor, constant: 4),
            vertical.leadingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.leadingAnchor, constant: 4),
            vertical.trailingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.trailingAnchor, constant: -4),
            vertical.bottomAnchor.constraint(equalTo: view.safeAreaLayoutGuide.bottomAnchor, constant: -4)
        ])
        let height = view.heightAnchor.constraint(equalToConstant: desiredHeight)
        height.priority = .init(999)
        height.isActive = true
        keyboardHeight = height

        draftButton.titleLabel?.font = .systemFont(ofSize: 15)
        draftButton.titleLabel?.lineBreakMode = .byTruncatingHead
        draftButton.contentHorizontalAlignment = .left
        draftButton.contentEdgeInsets = UIEdgeInsets(top: 0, left: 8, bottom: 0, right: 8)
        draftButton.addTarget(self, action: #selector(confirmDraft), for: .touchUpInside)
        draftButton.heightAnchor.constraint(equalToConstant: 26).isActive = true
        vertical.addArrangedSubview(draftButton)

        candidateScroll.showsHorizontalScrollIndicator = false
        candidateScroll.heightAnchor.constraint(equalToConstant: 40).isActive = true
        candidateRow.axis = .horizontal
        candidateRow.spacing = 6
        candidateRow.translatesAutoresizingMaskIntoConstraints = false
        candidateScroll.addSubview(candidateRow)
        NSLayoutConstraint.activate([
            candidateRow.topAnchor.constraint(equalTo: candidateScroll.contentLayoutGuide.topAnchor),
            candidateRow.leadingAnchor.constraint(equalTo: candidateScroll.contentLayoutGuide.leadingAnchor),
            candidateRow.trailingAnchor.constraint(equalTo: candidateScroll.contentLayoutGuide.trailingAnchor),
            candidateRow.bottomAnchor.constraint(equalTo: candidateScroll.contentLayoutGuide.bottomAnchor),
            candidateRow.heightAnchor.constraint(equalTo: candidateScroll.frameLayoutGuide.heightAnchor)
        ])
        vertical.addArrangedSubview(candidateScroll)
        toolbar.axis = .horizontal
        toolbar.distribution = .fillEqually
        toolbar.spacing = 3
        toolbar.heightAnchor.constraint(equalToConstant: 32).isActive = true
        vertical.addArrangedSubview(toolbar)
        keyRows.axis = .vertical
        keyRows.distribution = .fillEqually
        keyRows.spacing = 4
        vertical.addArrangedSubview(keyRows)
        bottomRow.axis = .horizontal
        bottomRow.spacing = 4
        bottomRow.heightAnchor.constraint(equalToConstant: 42).isActive = true
        vertical.addArrangedSubview(bottomRow)
        rebuildKeys()
        refreshCandidates()
    }

    private var desiredHeight: CGFloat {
        (traitCollection.verticalSizeClass == .compact ? 270 : 342) + view.safeAreaInsets.bottom
    }

    override func viewSafeAreaInsetsDidChange() {
        super.viewSafeAreaInsetsDidChange()
        keyboardHeight?.constant = desiredHeight
    }

    override func traitCollectionDidChange(_ previousTraitCollection: UITraitCollection?) {
        super.traitCollectionDidChange(previousTraitCollection)
        keyboardHeight?.constant = desiredHeight
        if isViewLoaded { rebuildKeys() }
    }

    override func viewWillDisappear(_ animated: Bool) {
        resetDraft()
        super.viewWillDisappear(animated)
    }

    override func textDidChange(_ textInput: UITextInput?) {
        super.textDidChange(textInput)
        // Our draft never writes to the host. Host notifications therefore
        // invalidate it (field switch, dictation, selection or external edit).
        if !composing.isEmpty { resetDraft() }
        if isViewLoaded { rebuildBottomRow() }
    }

    override func selectionDidChange(_ textInput: UITextInput?) {
        super.selectionDidChange(textInput)
        if !composing.isEmpty { resetDraft() }
    }

    private func clear(_ stack: UIStackView) {
        stack.arrangedSubviews.forEach { stack.removeArrangedSubview($0); $0.removeFromSuperview() }
    }

    private func button(_ title: String, accessibility: String? = nil,
                        modifier: Bool = false, action: @escaping (KeyboardViewController) -> Void) -> UIButton {
        let button = UIButton(type: .system)
        button.setTitle(title, for: .normal)
        button.titleLabel?.font = .systemFont(ofSize: 17, weight: .medium)
        button.titleLabel?.adjustsFontSizeToFitWidth = true
        button.titleLabel?.minimumScaleFactor = 0.65
        button.titleLabel?.numberOfLines = 2
        button.titleLabel?.textAlignment = .center
        button.setTitleColor(.label, for: .normal)
        button.backgroundColor = modifier ? .systemGray3 : .secondarySystemGroupedBackground
        button.layer.cornerRadius = 6
        button.layer.borderWidth = 0.5
        button.layer.borderColor = UIColor.separator.cgColor
        button.accessibilityLabel = accessibility ?? title
        button.addAction(UIAction { [weak self] _ in
            guard let self = self else { return }
            action(self)
        }, for: .touchUpInside)
        return button
    }

    private func kanaButton(_ index: Int) -> UIButton {
        let kana = KanaLayout.kana(index: index, board: board, katakana: katakana)
        let key = KanaLayout.keys[index]
        let b = button("", accessibility: "\(kana), \(key)") { controller in
            controller.append(KanaLayout.kana(index: index, board: controller.board, katakana: controller.katakana))
        }
        let style = NSMutableParagraphStyle()
        style.alignment = .center
        let title = NSMutableAttributedString(string: key + "\n", attributes: [
            .font: UIFont.systemFont(ofSize: 9), .foregroundColor: UIColor.secondaryLabel,
            .paragraphStyle: style
        ])
        title.append(NSAttributedString(string: kana, attributes: [
            .font: UIFont.systemFont(ofSize: 23), .foregroundColor: UIColor.label,
            .paragraphStyle: style
        ]))
        b.setAttributedTitle(title, for: .normal)
        return b
    }

    private func addWeighted(_ elements: [(UIView, CGFloat)], to row: UIStackView) {
        guard let first = elements.first else { return }
        for (view, weight) in elements {
            row.addArrangedSubview(view)
            if view !== first.0 {
                view.widthAnchor.constraint(equalTo: first.0.widthAnchor, multiplier: weight / first.1).isActive = true
            }
        }
    }

    private func row(_ elements: [(UIView, CGFloat)]) {
        let row = UIStackView()
        row.axis = .horizontal
        row.spacing = 3
        addWeighted(elements, to: row)
        keyRows.addArrangedSubview(row)
    }

    private func rebuildKeys() {
        clear(toolbar)
        toolbar.addArrangedSubview(button("あ", accessibility: "假名键盘", modifier: panel == .kana) { $0.show(.kana) })
        toolbar.addArrangedSubview(button("123", accessibility: "数字键盘", modifier: panel == .numbers) { $0.show(.numbers) })
        toolbar.addArrangedSubview(button(panel == .symbols ? "符 ↻" : "符", accessibility: "符号，重复点按翻页", modifier: panel == .symbols) {
            if $0.panel == .symbols { $0.symbolPage = ($0.symbolPage + 1) % $0.symbolPages.count }
            $0.show(.symbols)
        })
        toolbar.addArrangedSubview(button(panel == .emoji ? "😀 ↻" : "😀", accessibility: "表情，重复点按翻页", modifier: panel == .emoji) {
            if $0.panel == .emoji { $0.emojiPage = ($0.emojiPage + 1) % $0.emojiPages.count }
            $0.show(.emoji)
        })
        toolbar.addArrangedSubview(button("ー", accessibility: "长音") { $0.append("ー") })
        toolbar.addArrangedSubview(button("\(katakana ? "カナ" : "かな") \(board + 1)", accessibility: "切换假名板") {
            $0.board ^= 1
            $0.rebuildKeys()
        })
        clear(keyRows)
        switch panel {
        case .kana:
            row((0..<10).map { (kanaButton($0), 1) })
            row([(UIView(), 0.5)] + (10..<19).map { (kanaButton($0), 1) } + [(UIView(), 0.5)])
            let shift = button(katakana ? "⇧\nカナ" : "⇧\nかな", accessibility: "切换平假名与片假名", modifier: true) {
                $0.katakana.toggle(); $0.rebuildKeys()
            }
            let z = button("Z\n板\(board + 1)", accessibility: "Z，切换假名板", modifier: true) {
                $0.board ^= 1; $0.rebuildKeys()
            }
            let x = button("X\n◌゙", accessibility: "X，浊音", modifier: true) { $0.transform(KanaTransforms.toggleDakuten) }
            let c = button(board == 0 ? "C\n小" : "C\n◌゚", accessibility: board == 0 ? "C，小假名" : "C，半浊音", modifier: true) {
                $0.transform($0.board == 0 ? KanaTransforms.toggleSmall : KanaTransforms.toggleHandakuten)
            }
            row([(shift, 1.35), (z, 1), (x, 1), (c, 1)] +
                (19..<23).map { (kanaButton($0), 1) } + [(deleteButton(), 1.35)])
        case .numbers:
            let numbers = [["1", "2", "3"], ["4", "5", "6"], ["7", "8", "9"], [".", "0", "−"]]
            for values in numbers { row(values.map { value in (literalButton(value), 1) }) }
        case .symbols:
            addGrid(symbolPages[symbolPage])
        case .emoji:
            addGrid(emojiPages[emojiPage])
        }
        rebuildBottomRow()
    }

    private func rebuildBottomRow() {
        clear(bottomRow)
        let comma = literalButton("、")
        let period = literalButton("。")
        let space = button(composing.isEmpty ? "空白" : "変換 / 確定", accessibility: composing.isEmpty ? "空格" : "确定第一个候选词") { $0.space() }
        let globe = button("🌐", accessibility: "切换键盘", modifier: true) { _ in }
        globe.removeTarget(nil, action: nil, for: .allEvents)
        globe.addTarget(self, action: #selector(handleInputModeList(from:with:)), for: .allTouchEvents)
        // Always keep an escape route; iOS supplies the actual mode picker.
        let enter = button(returnLabel, accessibility: composing.isEmpty ? "回车" : "确定假名", modifier: true) { $0.enter() }
        var elements: [(UIView, CGFloat)] = [(comma, 0.9), (period, 0.9), (space, 3.2), (globe, 1)]
        if panel != .kana { elements.append((deleteButton(), 1)) }
        elements.append((enter, 1.5))
        addWeighted(elements, to: bottomRow)
    }

    private var returnLabel: String {
        if !composing.isEmpty { return "確定" }
        switch textDocumentProxy.returnKeyType ?? .default {
        case .go: return "前往"
        case .search, .google, .yahoo: return "搜索"
        case .send: return "发送"
        case .done: return "完成"
        case .next: return "下一项"
        default: return "↵"
        }
    }

    private func deleteButton() -> UIButton {
        button("⌫", accessibility: "删除", modifier: true) { $0.backspace() }
    }

    private func literalButton(_ value: String) -> UIButton {
        button(value) { $0.commitLiteral(value) }
    }

    private func addGrid(_ values: [String]) {
        for start in stride(from: 0, to: values.count, by: 10) {
            row(values[start..<min(start + 10, values.count)].map { (literalButton($0), 1) })
        }
    }

    private func show(_ panel: Panel) {
        self.panel = panel
        rebuildKeys()
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
        if composing.isEmpty {
            textDocumentProxy.deleteBackward()
        } else {
            composing.removeLast() // Extended grapheme, including combining marks.
            refreshCandidates()
        }
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

    private func commitLiteral(_ text: String) {
        // Commit both in one host edit so the candidate draft cannot leak into
        // another field between insertions.
        commit(composing + text)
    }

    private func commit(_ text: String) {
        resetDraft() // Invalidate outstanding async results before host callbacks.
        textDocumentProxy.insertText(text)
    }

    private func resetDraft() {
        composing = ""
        if isViewLoaded { refreshCandidates() }
    }

    private func refreshCandidates() {
        candidateGeneration += 1
        candidateWork?.cancel()
        candidates = []
        let reading = composing
        draftButton.setTitle(reading.isEmpty ? "23×2 Kana · 离线假名输入" : "输入中：\(reading)  · 点按确定", for: .normal)
        draftButton.accessibilityLabel = reading.isEmpty ? "没有待确定的输入" : "待确定假名：\(reading)，点按原样确定"
        draftButton.isEnabled = !reading.isEmpty
        rebuildBottomRow()
        renderCandidates([])
        guard !reading.isEmpty else { return }
        let generation = candidateGeneration
        let work = DispatchWorkItem { [weak self] in
            guard let self = self else { return }
            let results = self.engine.candidates(reading: reading, limit: 16)
            DispatchQueue.main.async { [weak self] in
                guard let self = self, self.candidateGeneration == generation,
                      self.composing == reading else { return }
                self.candidates = results
                self.renderCandidates(results)
            }
        }
        candidateWork = work
        conversionQueue.async(execute: work)
    }

    private func renderCandidates(_ values: [Candidate]) {
        clear(candidateRow)
        if values.isEmpty {
            let label = UILabel()
            label.text = composing.isEmpty ? "Z 换板 · X 浊音 · C 小 / 半浊音" : "转换中…（可点按上方假名确定）"
            label.font = .systemFont(ofSize: 13)
            label.textColor = .secondaryLabel
            candidateRow.addArrangedSubview(label)
        } else {
            let expectedGeneration = candidateGeneration
            for candidate in values {
                let chip = button(candidate.text, accessibility: candidate.note.isEmpty ? candidate.text : "\(candidate.text)，\(candidate.note)") { controller in
                    guard controller.candidateGeneration == expectedGeneration else { return }
                    controller.commit(candidate.text)
                }
                chip.contentEdgeInsets = UIEdgeInsets(top: 0, left: 12, bottom: 0, right: 12)
                chip.setContentCompressionResistancePriority(.required, for: .horizontal)
                candidateRow.addArrangedSubview(chip)
            }
        }
        candidateScroll.setContentOffset(.zero, animated: false)
    }
}
