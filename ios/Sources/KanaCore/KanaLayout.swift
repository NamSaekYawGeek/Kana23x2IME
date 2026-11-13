public enum KanaLayout {
    public static let keys = [
        "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P",
        "A", "S", "D", "F", "G", "H", "J", "K", "L", "V", "B", "N", "M"
    ]
    public static let boards = [
        Array("あいうえおかきくけこさしすせそたちつてとやゆよ").map(String.init),
        Array("なにぬねのはひふへほまみむめもらりるれろわをん").map(String.init)
    ]

    public static func kana(index: Int, board: Int, katakana: Bool) -> String {
        guard boards.indices.contains(board), keys.indices.contains(index) else { return "" }
        let kana = boards[board][index]
        return katakana ? KanaTransforms.toKatakana(kana) : kana
    }
}
