package dev.kana23x2.ime;

/** Run with javac/java; no Android framework or test dependency is needed. */
public final class KanaTransformsRegressionTest {
    private static int assertions;

    public static void main(String[] args) {
        equal("がっこう", KanaTransforms.normalizeReading("か\u3099っこう"));
        equal("がっこう", KanaTransforms.normalizeReading("ガッコウ"));
        equal("がっこう", KanaTransforms.normalizeReading("カ\u3099ッコウ"));
        equal("ぱん", KanaTransforms.normalizeReading("は\u309Aん"));
        equal("ぱん", KanaTransforms.normalizeReading("ハ\u309Aン"));
        equal("ゔぁ", KanaTransforms.normalizeReading("ウ\u3099ァ"));
        equal("わ\u3099", KanaTransforms.normalizeReading("ヷ"));
        equal("か\u309A", KanaTransforms.normalizeReading("カ\u309A"));
        equal("あ\u3099", KanaTransforms.normalizeReading("あ\u3099"));
        equal("𠮷😀", KanaTransforms.normalizeReading("𠮷😀"));
        equal("", KanaTransforms.normalizeReading(null));
        equal("", KanaTransforms.normalizeReading(""));

        String[] readings = {"がっこう", "か\u3099っこう", "パンダ", "ヷ", "ㇰ\u309A", "𠮷"};
        for (String reading : readings) {
            String normalized = KanaTransforms.normalizeReading(reading);
            equal(normalized, KanaTransforms.normalizeReading(normalized));
        }

        equal("ば", KanaTransforms.toggleDakuten("は"));
        equal("は", KanaTransforms.toggleDakuten("ば"));
        equal("ば", KanaTransforms.toggleDakuten("ぱ"));
        equal("ぱ", KanaTransforms.toggleHandakuten("ば"));
        equal("は", KanaTransforms.toggleHandakuten("ぱ"));
        equal("っ", KanaTransforms.toggleSmall("つ"));
        equal("つ", KanaTransforms.toggleSmall("っ"));
        equal("ㇰ\u309A", KanaTransforms.toggleSmall("ク\u309A"));
        equal("𠮷が", KanaTransforms.toggleDakuten("𠮷か"));
        equal("𠮷か", KanaTransforms.toggleDakuten("𠮷か\u3099"));
        System.out.println("KanaTransforms regression checks passed: " + assertions);
    }

    private static void equal(String expected, String actual) {
        assertions++;
        if (!expected.equals(actual)) {
            throw new AssertionError("Expected <" + expected + "> but was <" + actual + ">");
        }
    }
}
