import Foundation
import XCTest
@testable import KanaCore

final class KanaCoreTests: XCTestCase {
    func testBothBoardsPreserveQwertyPositions() {
        XCTAssertEqual(KanaLayout.keys.count, 23)
        XCTAssertEqual(Set(KanaLayout.keys).count, 23)
        XCTAssertFalse(KanaLayout.keys.contains("Z"))
        XCTAssertEqual(KanaLayout.boards.map(\.count), [23, 23])
        XCTAssertEqual(KanaLayout.kana(index: 0, board: 0, katakana: false), "あ")
        XCTAssertEqual(KanaLayout.kana(index: 19, board: 0, katakana: false), "と")
        XCTAssertEqual(KanaLayout.kana(index: 22, board: 1, katakana: true), "ン")
        XCTAssertEqual(KanaLayout.kana(index: -1, board: 0, katakana: false), "")
        XCTAssertEqual(KanaLayout.kana(index: 0, board: 2, katakana: false), "")
    }

    func testReversibleDiacriticsAndSwitchingMarks() {
        for base in "かきくけこさしすせそたちつてとはひふへほうカキクケコサシスセソタチツテトハヒフヘホウワヰヱヲあ" {
            let text = "前" + String(base)
            XCTAssertEqual(KanaTransforms.toggleDakuten(KanaTransforms.toggleDakuten(text)), text)
        }
        for base in "はひふへほハヒフヘホㇷ" {
            let text = "前" + String(base)
            XCTAssertEqual(KanaTransforms.toggleHandakuten(KanaTransforms.toggleHandakuten(text)), text)
        }
        XCTAssertEqual(KanaTransforms.toggleDakuten("ぱ"), "ば")
        XCTAssertEqual(KanaTransforms.toggleHandakuten("ば"), "ぱ")
        XCTAssertEqual(KanaTransforms.toggleDakuten("は\u{309A}"), "ば")
        XCTAssertEqual(KanaTransforms.toggleDakuten("か\u{3099}"), "か")
        XCTAssertEqual(KanaTransforms.toggleDakuten("👩‍💻"), "👩‍💻")
        XCTAssertEqual(KanaTransforms.toggleDakuten(""), "")
        XCTAssertEqual(KanaTransforms.toggleDakuten("\u{3099}"), "\u{3099}")
    }

    func testSmallKanaAndScriptConversion() {
        for kana in "あいうえおつやゆよわかけアイウエオツヤユヨワカケクシストヌハヒフヘホムラリルレロ" {
            let text = "前" + String(kana)
            XCTAssertNotEqual(KanaTransforms.toggleSmall(text), text)
            XCTAssertEqual(KanaTransforms.toggleSmall(KanaTransforms.toggleSmall(text)), text)
        }
        XCTAssertEqual(KanaTransforms.toggleSmall("漢字"), "漢字")
        XCTAssertEqual(KanaTransforms.toggleSmall("ㇷ\u{309A}"), "フ\u{309A}")
        XCTAssertEqual(KanaTransforms.toKatakana("がぎぐげごぱぴぷぺぽゔ😀"), "ガギグゲゴパピプペポヴ😀")
        XCTAssertEqual(KanaTransforms.toHiragana("ガギグゲゴパピプペポヴ😀"), "がぎぐげごぱぴぷぺぽゔ😀")
    }

    func testCanonicalReadingsNeverDiscardVoicing() {
        XCTAssertEqual(KanaTransforms.normalizeReading("カ\u{3099}"), "が")
        XCTAssertEqual(KanaTransforms.normalizeReading("ハ\u{309A}"), "ぱ")
        XCTAssertEqual(KanaTransforms.normalizeReading("ヷ"), "わ\u{3099}")
        XCTAssertEqual(KanaTransforms.normalizeReading("あ\u{3099}"), "あ\u{3099}")
        XCTAssertNotEqual(KanaTransforms.normalizeReading("か\u{3099}"), "か")
    }

    func testCompositionDeletesWholeUserPerceivedCharacters() {
        var buffer = CompositionBuffer(text: "あか\u{3099}👨‍👩‍👧‍👦🇯🇵")
        buffer.backspace()
        XCTAssertEqual(buffer.text, "あか\u{3099}👨‍👩‍👧‍👦")
        buffer.backspace()
        XCTAssertEqual(buffer.text, "あか\u{3099}")
        buffer.backspace()
        XCTAssertEqual(buffer.text, "あ")
        buffer.append("は")
        buffer.transform(KanaTransforms.toggleHandakuten)
        XCTAssertEqual(buffer.text, "あぱ")
        buffer.clear()
        buffer.backspace()
        XCTAssertEqual(buffer.text, "")
    }

    private func engine(_ entries: [(String, [String])], trailingNewline: Bool = true) throws -> CandidateEngine {
        let sorted = entries.sorted { $0.0.utf8.lexicographicallyPrecedes($1.0.utf8) }
        let text = sorted.map { ([$0.0] + $0.1).joined(separator: "\t") }.joined(separator: "\n")
            + (trailingNewline ? "\n" : "")
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".tsv")
        try text.write(to: url, atomically: true, encoding: .utf8)
        addTeardownBlock { try? FileManager.default.removeItem(at: url) }
        return CandidateEngine(dictionaryURL: url)
    }

    func testDictionaryLookupAtFileBoundariesAndMissingKeys() throws {
        let entries = [("あ", ["亜"]), ("か", ["蚊"]), ("が", ["蛾"]), ("にほん", ["日本", "二本"]), ("ん", ["ン"])]
        let dictionary = try engine(entries, trailingNewline: false)
        XCTAssertTrue(dictionary.dictionaryAvailable)
        for (key, values) in entries {
            XCTAssertEqual(dictionary.candidates(reading: key).first?.text, values.first)
        }
        for key in ["", "ぁ", "い", "ご", "んん", "😀"] {
            let candidates = dictionary.candidates(reading: key)
            XCTAssertTrue(key.isEmpty ? candidates.isEmpty : candidates.contains { $0.text == key })
        }
        let single = try engine([("にほん", ["日本"])], trailingNewline: false)
        XCTAssertEqual(single.candidates(reading: "にほん").first?.text, "日本")
    }

    func testNormalizedVoicedLookupAndPrefixPrediction() throws {
        let dictionary = try engine([("が", ["蛾"]), ("か", ["蚊"]), ("にほん", ["日本"]), ("にほんご", ["日本語"])])
        XCTAssertEqual(dictionary.candidates(reading: "カ\u{3099}").first?.text, "蛾")
        let prediction = dictionary.candidates(reading: "にほ").first { $0.text == "日本" }
        XCTAssertEqual(prediction?.sourceReading, "にほん")
        XCTAssertEqual(prediction?.note, "予測 にほん")
    }

    func testByteSearchFindsUnevenRowsThroughoutDictionary() throws {
        let entries: [(String, [String])] = (0..<513).map { index in
            let key = "あ" + String(format: "%04d", index)
            return (key, [String(repeating: "漢", count: index % 23 + 1) + String(index)])
        }
        let dictionary = try engine(entries)
        for index in stride(from: 0, to: entries.count, by: 7) {
            XCTAssertEqual(dictionary.candidates(reading: entries[index].0).first?.text, entries[index].1.first)
        }
        XCTAssertEqual(dictionary.candidates(reading: entries.last!.0).first?.text, entries.last!.1.first)
    }

    func testKanaFallbacksSurviveFullDictionaryAndDeduplication() throws {
        let values = (0..<30).map { "候補\($0)" } + ["候補0"]
        let dictionary = try engine([("かな", values)])
        let candidates = dictionary.candidates(reading: "かな", limit: 8)
        XCTAssertEqual(candidates.count, 8)
        XCTAssertTrue(candidates.contains { $0.text == "かな" })
        XCTAssertTrue(candidates.contains { $0.text == "カナ" })
        XCTAssertEqual(Set(candidates.map(\.text)).count, candidates.count)
        XCTAssertTrue(dictionary.candidates(reading: "かな", limit: 0).isEmpty)
        XCTAssertTrue(dictionary.candidates(reading: "かな", limit: -1).isEmpty)
        XCTAssertEqual(dictionary.candidates(reading: "かな", limit: 1).count, 1)
    }

    func testOkuriVoicedEndingsAndPhraseConversion() throws {
        let dictionary = try engine([("およg", ["泳"]), ("かk", ["書"]), ("にほん", ["日本"]), ("ご", ["語"]), ("とうきょう", ["東京"])])
        XCTAssertTrue(dictionary.candidates(reading: "およぐ").contains { $0.text == "泳ぐ" })
        XCTAssertTrue(dictionary.candidates(reading: "かいた").contains { $0.text == "書いた" })
        XCTAssertTrue(dictionary.candidates(reading: "にほんとうきょう").contains { $0.text == "日本東京" })
    }

    func testMissingDictionaryStillAllowsKanaInput() {
        let dictionary = CandidateEngine(dictionaryURL: URL(fileURLWithPath: "/nonexistent/kana.tsv"))
        XCTAssertFalse(dictionary.dictionaryAvailable)
        XCTAssertEqual(dictionary.candidates(reading: "かな").map(\.text), ["かな", "カナ"])
    }
}
