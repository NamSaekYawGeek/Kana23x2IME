import Foundation

public enum KanaTransforms {
    private static let smallPairs: [String: String] = {
        let large = "あいうえおつやゆよわかけアイウエオツヤユヨワカケクシストヌハヒフヘホムラリルレロ"
        let small = "ぁぃぅぇぉっゃゅょゎゕゖァィゥェォッャュョヮヵヶㇰㇱㇲㇳㇴㇵㇶㇷㇸㇹㇺㇻㇼㇽㇾㇿ"
        return Dictionary(uniqueKeysWithValues: zip(large.map(String.init), small.map(String.init)))
    }()
    private static let largePairs = Dictionary(uniqueKeysWithValues: smallPairs.map { ($1, $0) })

    public static func toKatakana(_ text: String) -> String { mapScript(text, range: 0x3041...0x3096, offset: 0x60) }
    public static func toHiragana(_ text: String) -> String { mapScript(text, range: 0x30A1...0x30F6, offset: -0x60) }

    /// Decompose before script conversion so ヷ and decomposed ガ keep their voicing.
    public static func normalizeReading(_ text: String) -> String {
        toHiragana(text.decomposedStringWithCanonicalMapping).precomposedStringWithCanonicalMapping
    }

    private static func mapScript(_ text: String, range: ClosedRange<UInt32>, offset: Int) -> String {
        var result = String.UnicodeScalarView()
        for scalar in text.unicodeScalars {
            if range.contains(scalar.value), let mapped = UnicodeScalar(Int(scalar.value) + offset) {
                result.append(mapped)
            } else {
                result.append(scalar)
            }
        }
        return String(result)
    }

    public static func toggleDakuten(_ text: String) -> String { toggleMark(text, mark: "\u{3099}") }
    public static func toggleHandakuten(_ text: String) -> String { toggleMark(text, mark: "\u{309A}") }

    private static func toggleMark(_ text: String, mark: UnicodeScalar) -> String {
        guard let last = text.last else { return text }
        let scalars = Array(String(last).decomposedStringWithCanonicalMapping.unicodeScalars)
        guard let base = scalars.first, base != "\u{3099}", base != "\u{309A}",
              scalars.count == 1 || (scalars.count == 2 && (scalars[1] == "\u{3099}" || scalars[1] == "\u{309A}"))
        else { return text }
        let currentMark = scalars.count == 2 ? scalars[1] : nil
        let replacement = String(base) + (currentMark == mark ? "" : String(mark))
        return String(text.dropLast()) + replacement.precomposedStringWithCanonicalMapping
    }

    public static func toggleSmall(_ text: String) -> String {
        guard let last = text.last else { return text }
        let tail = String(last)
        if let replacement = smallPairs[tail] ?? largePairs[tail] {
            return String(text.dropLast()) + replacement
        }
        // A combining mark without a precomposed equivalent travels with its base.
        let scalars = Array(tail.unicodeScalars)
        if scalars.count == 2, scalars[1] == "\u{3099}" || scalars[1] == "\u{309A}" {
            let base = String(scalars[0])
            if let replacement = smallPairs[base] ?? largePairs[base] {
                return String(text.dropLast()) + replacement + String(scalars[1])
            }
        }
        return text
    }
}
