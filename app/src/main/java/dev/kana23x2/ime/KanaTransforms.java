package dev.kana23x2.ime;

import java.util.HashMap;
import java.util.Map;

public final class KanaTransforms {
    private KanaTransforms() {}

    private static final Map<String, String> DAKUTEN = new HashMap<>();
    private static final Map<String, String> UNDAKUTEN = new HashMap<>();
    private static final Map<String, String> HANDAKUTEN = new HashMap<>();
    private static final Map<String, String> UNHANDAKUTEN = new HashMap<>();
    private static final Map<String, String> SMALL = new HashMap<>();
    private static final Map<String, String> LARGE = new HashMap<>();

    static {
        pairDakuten("かきくけこ", "がぎぐげご");
        pairDakuten("さしすせそ", "ざじずぜぞ");
        pairDakuten("たちつてと", "だぢづでど");
        pairDakuten("はひふへほ", "ばびぶべぼ");
        pairDakuten("カキクケコ", "ガギグゲゴ");
        pairDakuten("サシスセソ", "ザジズゼゾ");
        pairDakuten("タチツテト", "ダヂヅデド");
        pairDakuten("ハヒフヘホ", "バビブベボ");
        putDakuten("う", "ゔ");
        putDakuten("ウ", "ヴ");
        putDakuten("ワ", "ヷ");
        putDakuten("ヰ", "ヸ");
        putDakuten("ヱ", "ヹ");
        putDakuten("ヲ", "ヺ");

        pairHandakuten("はひふへほ", "ぱぴぷぺぽ");
        pairHandakuten("ハヒフヘホ", "パピプペポ");

        pairSmall("あ", "ぁ"); pairSmall("い", "ぃ"); pairSmall("う", "ぅ");
        pairSmall("え", "ぇ"); pairSmall("お", "ぉ"); pairSmall("つ", "っ");
        pairSmall("や", "ゃ"); pairSmall("ゆ", "ゅ"); pairSmall("よ", "ょ");
        pairSmall("わ", "ゎ"); pairSmall("か", "ゕ"); pairSmall("け", "ゖ");

        pairSmall("ア", "ァ"); pairSmall("イ", "ィ"); pairSmall("ウ", "ゥ");
        pairSmall("エ", "ェ"); pairSmall("オ", "ォ"); pairSmall("ツ", "ッ");
        pairSmall("ヤ", "ャ"); pairSmall("ユ", "ュ"); pairSmall("ヨ", "ョ");
        pairSmall("ワ", "ヮ"); pairSmall("カ", "ヵ"); pairSmall("ケ", "ヶ");

        pairSmall("ク", "ㇰ");
        pairSmall("シ", "ㇱ");
        pairSmall("ス", "ㇲ");
        pairSmall("ト", "ㇳ");
        pairSmall("ヌ", "ㇴ");
        pairSmall("ハ", "ㇵ");
        pairSmall("ヒ", "ㇶ");
        pairSmall("フ", "ㇷ");
        pairSmall("ヘ", "ㇸ");
        pairSmall("ホ", "ㇹ");
        pairSmall("ム", "ㇺ");
        pairSmall("ラ", "ㇻ");
        pairSmall("リ", "ㇼ");
        pairSmall("ル", "ㇽ");
        pairSmall("レ", "ㇾ");
        pairSmall("ロ", "ㇿ");
    }

    private static void pairDakuten(String plain, String voiced) {
        int n = Math.min(plain.length(), voiced.length());
        for (int i = 0; i < n; i++) putDakuten(String.valueOf(plain.charAt(i)), String.valueOf(voiced.charAt(i)));
    }

    private static void putDakuten(String plain, String voiced) {
        DAKUTEN.put(plain, voiced);
        UNDAKUTEN.put(voiced, plain);
    }

    private static void pairHandakuten(String plain, String half) {
        int n = Math.min(plain.length(), half.length());
        for (int i = 0; i < n; i++) {
            String a = String.valueOf(plain.charAt(i));
            String b = String.valueOf(half.charAt(i));
            HANDAKUTEN.put(a, b);
            UNHANDAKUTEN.put(b, a);
        }
    }

    private static void pairSmall(String large, String small) {
        SMALL.put(large, small);
        LARGE.put(small, large);
    }

    public static String toKatakana(String text) {
        if (text == null || text.isEmpty()) return "";
        StringBuilder out = new StringBuilder();
        text.codePoints().forEach(cp -> {
            if (cp >= 0x3041 && cp <= 0x3096) out.appendCodePoint(cp + 0x60);
            else out.appendCodePoint(cp);
        });
        return out.toString();
    }

    public static String toHiragana(String text) {
        if (text == null || text.isEmpty()) return "";
        StringBuilder out = new StringBuilder();
        text.codePoints().forEach(cp -> {
            if (cp >= 0x30A1 && cp <= 0x30F6) out.appendCodePoint(cp - 0x60);
            else out.appendCodePoint(cp);
        });
        return out.toString();
    }

    private static final class Tail {
        final String prefix;
        final String base;
        final String mark;
        Tail(String prefix, String base, String mark) { this.prefix = prefix; this.base = base; this.mark = mark; }
    }

    private static Tail splitTail(String s) {
        if (s == null || s.isEmpty()) return null;
        String mark = "";
        int end = s.length();
        int lastCp = s.codePointBefore(end);
        if (lastCp == 0x3099 || lastCp == 0x309A) {
            mark = new String(Character.toChars(lastCp));
            end -= Character.charCount(lastCp);
            if (end <= 0) return new Tail("", "", mark);
        }
        int baseCp = s.codePointBefore(end);
        int baseStart = end - Character.charCount(baseCp);
        return new Tail(s.substring(0, baseStart), s.substring(baseStart, end), mark);
    }

    public static String toggleDakuten(String text) {
        Tail t = splitTail(text);
        if (t == null || t.base.isEmpty()) return text;
        if ("\u3099".equals(t.mark)) return t.prefix + t.base;
        if ("\u309A".equals(t.mark)) return t.prefix + t.base + "\u3099";

        String plain = UNDAKUTEN.get(t.base);
        if (plain != null) return t.prefix + plain;

        String halfPlain = UNHANDAKUTEN.get(t.base);
        if (halfPlain != null) {
            String voiced = DAKUTEN.get(halfPlain);
            return t.prefix + (voiced != null ? voiced : halfPlain + "\u3099");
        }

        String voiced = DAKUTEN.get(t.base);
        if (voiced != null) return t.prefix + voiced;
        return t.prefix + t.base + "\u3099";
    }

    public static String toggleHandakuten(String text) {
        Tail t = splitTail(text);
        if (t == null || t.base.isEmpty()) return text;
        if ("\u309A".equals(t.mark)) return t.prefix + t.base;
        if ("\u3099".equals(t.mark)) return t.prefix + t.base + "\u309A";

        String plain = UNHANDAKUTEN.get(t.base);
        if (plain != null) return t.prefix + plain;

        String voicedPlain = UNDAKUTEN.get(t.base);
        if (voicedPlain != null) {
            String half = HANDAKUTEN.get(voicedPlain);
            return t.prefix + (half != null ? half : voicedPlain + "\u309A");
        }

        String half = HANDAKUTEN.get(t.base);
        if (half != null) return t.prefix + half;
        return t.prefix + t.base + "\u309A";
    }

    public static String toggleSmall(String text) {
        Tail t = splitTail(text);
        if (t == null || t.base.isEmpty()) return text;
        String converted = SMALL.get(t.base);
        if (converted == null) converted = LARGE.get(t.base);
        if (converted == null) return text;
        return t.prefix + converted + t.mark;
    }
}
