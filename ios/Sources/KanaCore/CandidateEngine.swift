import Foundation

public struct Candidate: Equatable {
    public let text: String
    public let sourceReading: String
    public let note: String

    public init(text: String, sourceReading: String, note: String = "") {
        self.text = text
        self.sourceReading = sourceReading
        self.note = note
    }
}

/// Queries the sorted, uncompressed UTF-8 TSV produced by build_dictionary.py.
/// The file is memory-mapped; no dictionary-sized tree, strings, or line-offset index
/// is created inside the keyboard extension's limited memory budget.
public final class CandidateEngine {
    private let data: Data
    public var dictionaryAvailable: Bool { !data.isEmpty }

    public init(dictionaryURL: URL? = nil) {
        if let url = dictionaryURL, let contents = try? Data(contentsOf: url, options: .mappedIfSafe) {
            data = contents
        } else {
            data = Data()
        }
    }

    public func candidates(reading: String, limit: Int = 24) -> [Candidate] {
        guard limit > 0, !reading.isEmpty else { return [] }
        let key = KanaTransforms.normalizeReading(reading)
        var result: [Candidate] = []
        var seen: Set<String> = []
        func add(_ text: String, reading source: String, note: String = "", cap: Int) {
            guard result.count < cap, !text.isEmpty, seen.insert(text).inserted else { return }
            result.append(Candidate(text: text, sourceReading: source, note: note))
        }

        // Always leave room for the original spelling and the alternate script.
        let katakana = KanaTransforms.toKatakana(key)
        let fallbackCount = katakana == reading ? 1 : 2
        let conversionLimit = max(0, limit - fallbackCount)
        for text in exact(key) { add(text, reading: key, cap: conversionLimit) }
        if result.count < conversionLimit {
            for text in okuriCandidates(key) { add(text, reading: key, note: "活用", cap: conversionLimit) }
        }
        if result.count < conversionLimit, let segmented = segmentedBest(key) {
            add(segmented, reading: key, note: "文節", cap: conversionLimit)
        }
        add(reading, reading: key, note: "かな", cap: limit)
        add(katakana, reading: key, note: "カナ", cap: limit)

        if key.count >= 2, result.count < limit {
            var position = lowerBound(Array(key.utf8))
            var scanned = 0
            while position < data.count, scanned < 80, result.count < limit {
                let row = line(at: position)
                position = row.next
                scanned += 1
                guard row.key.hasPrefix(key) else { break }
                guard row.key != key, !hasOkuriMarker(row.key), let first = row.values.first else { continue }
                add(first, reading: row.key, note: "予測 " + row.key, cap: limit)
            }
        }
        return result
    }

    private func exact(_ key: String) -> [String] {
        let position = lowerBound(Array(key.utf8))
        guard position < data.count else { return [] }
        let row = line(at: position)
        return row.key == key ? row.values : []
    }

    private func line(at start: Int) -> (key: String, values: [String], next: Int) {
        var end = start
        while end < data.count, data[end] != 10 { end += 1 }
        let row = String(decoding: data[start..<end], as: UTF8.self)
        let fields = row.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
        return (fields.first ?? "", fields.dropFirst().filter { !$0.isEmpty }, min(end + 1, data.count))
    }

    /// Binary search over bytes, realigning each probe to a complete TSV row.
    private func lowerBound(_ key: [UInt8]) -> Int {
        var low = 0
        var high = data.count
        while low < high {
            var start = low + (high - low) / 2
            while start > low, data[start - 1] != 10 { start -= 1 }
            var endOfKey = start
            while endOfKey < data.count, data[endOfKey] != 9, data[endOfKey] != 10 { endOfKey += 1 }
            if data[start..<endOfKey].lexicographicallyPrecedes(key) {
                var end = endOfKey
                while end < data.count, data[end] != 10 { end += 1 }
                low = min(end + 1, data.count)
            } else {
                high = start
            }
        }
        return low
    }

    private func hasOkuriMarker(_ key: String) -> Bool {
        guard let last = key.utf8.last else { return false }
        return (65...90).contains(last) || (97...122).contains(last)
    }

    private func okuriCandidates(_ reading: String) -> [String] {
        let letters = Array(reading)
        guard letters.count >= 2, letters.count <= 40 else { return [] }
        var result: [String] = []
        var seen: Set<String> = []
        for split in 1..<letters.count {
            let stem = String(letters[..<split])
            let suffix = String(letters[split...])
            for code in "kgsztdnhbpmrywiaeoucfjvqxl" where okuriCompatible(code, suffix: suffix) {
                for word in exact(stem + String(code)).prefix(4) {
                    let candidate = word + suffix
                    if seen.insert(candidate).inserted { result.append(candidate) }
                    if result.count >= 8 { return result }
                }
            }
        }
        return result
    }

    private static let endings: [Character: Set<String>] = [
        "k": ["く", "かない", "きます", "いた", "いて", "けば", "こう"],
        "g": ["ぐ", "がない", "ぎます", "いだ", "いで", "げば", "ごう"],
        "s": ["す", "さない", "します", "した", "して", "せば", "そう"],
        "t": ["つ", "たない", "ちます", "った", "って", "てば", "とう"],
        "n": ["ぬ", "なない", "にます", "んだ", "んで", "ねば", "のう"],
        "b": ["ぶ", "ばない", "びます", "んだ", "んで", "べば", "ぼう"],
        "m": ["む", "まない", "みます", "んだ", "んで", "めば", "もう"],
        "r": ["る", "らない", "ります", "った", "って", "れば", "ろう", "ない", "ます", "た", "て", "よう"],
        "w": ["う", "わない", "います", "った", "って", "えば", "おう"],
        "i": ["い", "くない", "かった", "くて", "ければ", "さ", "そう"]
    ]

    private func okuriCompatible(_ code: Character, suffix: String) -> Bool {
        if Self.endings[code]?.contains(suffix) == true { return true }
        guard let first = suffix.first else { return false }
        let rows: [(String, Character)] = [
            ("かきくけこ", "k"), ("がぎぐげご", "g"), ("さしすせそ", "s"), ("ざじずぜぞ", "z"),
            ("たちつてと", "t"), ("だぢづでど", "d"), ("なにぬねの", "n"), ("はひふへほ", "h"),
            ("ばびぶべぼ", "b"), ("ぱぴぷぺぽ", "p"), ("まみむめも", "m"), ("やゆよ", "y"),
            ("らりるれろ", "r"), ("わを", "w"), ("あ", "a"), ("い", "i"), ("う", "u"), ("え", "e"), ("お", "o")
        ]
        return rows.first(where: { $0.0.contains(first) })?.1 == code
    }

    private func segmentedBest(_ reading: String) -> String? {
        let letters = Array(reading)
        guard (3...40).contains(letters.count) else { return nil }
        var scores = Array(repeating: -Double.infinity, count: letters.count + 1)
        var words = Array<String?>(repeating: nil, count: letters.count + 1)
        scores[0] = 0
        words[0] = ""
        for start in letters.indices {
            guard let prefix = words[start] else { continue }
            if scores[start] + 0.05 > scores[start + 1] {
                scores[start + 1] = scores[start] + 0.05
                words[start + 1] = prefix + String(letters[start])
            }
            guard start + 2 <= letters.count else { continue }
            for end in (start + 2)...min(letters.count, start + 12) {
                guard let word = exact(String(letters[start..<end])).first else { continue }
                let score = scores[start] + Double((end - start) * (end - start)) + 0.5
                if score > scores[end] {
                    scores[end] = score
                    words[end] = prefix + word
                }
            }
        }
        guard let best = words[letters.count], best != reading else { return nil }
        return best
    }
}
