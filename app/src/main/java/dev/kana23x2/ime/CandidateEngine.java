package dev.kana23x2.ime;

import android.content.Context;
import android.content.SharedPreferences;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.zip.GZIPInputStream;

public class CandidateEngine {
    public static class Candidate {
        public final String text;
        public final String note;
        public final String sourceReading;
        public Candidate(String text, String note, String sourceReading) {
            this.text = text;
            this.note = note;
            this.sourceReading = sourceReading;
        }
    }

    private final Context context;
    private final SharedPreferences prefs;
    private final TreeMap<String, List<String>> dict = new TreeMap<>();
    private volatile boolean ready = false;

    public CandidateEngine(Context context) {
        this.context = context.getApplicationContext();
        this.prefs = context.getSharedPreferences("candidate_learning", Context.MODE_PRIVATE);
    }

    public boolean isReady() { return ready; }

    public void load() {
        if (ready) return;
        synchronized (this) {
            if (ready) return;
            try (GZIPInputStream gz = new GZIPInputStream(context.getAssets().open("skk_compact.tsv.gz"));
                 BufferedReader br = new BufferedReader(new InputStreamReader(gz, StandardCharsets.UTF_8), 64 * 1024)) {
                String line;
                while ((line = br.readLine()) != null) {
                    String[] parts = line.split("\t");
                    if (parts.length < 2) continue;
                    ArrayList<String> vals = new ArrayList<>(parts.length - 1);
                    for (int i = 1; i < parts.length; i++) {
                        if (!parts[i].isEmpty() && !vals.contains(parts[i])) vals.add(parts[i]);
                    }
                    if (!vals.isEmpty()) dict.put(parts[0], vals);
                }
                ready = true;
            } catch (Exception e) {
                e.printStackTrace();
            }
        }
    }

    private String norm(String reading) {
        return KanaTransforms.toHiragana(reading == null ? "" : reading)
                .replace("\u3099", "")
                .replace("\u309A", "");
    }

    public List<Candidate> candidates(String reading, String previousWord, int limit) {
        if (limit <= 0) return Collections.emptyList();
        String key = norm(reading);
        if (key.isEmpty()) return recentCandidates(limit);

        ArrayList<Candidate> out = new ArrayList<>();
        HashSet<String> seen = new HashSet<>();

        if (ready) {
            List<String> exact = dict.get(key);
            if (exact != null) {
                List<String> ranked = rank(key, exact, previousWord);
                for (String s : ranked) add(out, seen, s, "", key, limit);
            }

            for (String s : okuriCandidates(key)) add(out, seen, s, "活用", key, limit);

            String segmented = segmentedBest(key);
            if (segmented != null && !segmented.equals(key)) add(out, seen, segmented, "文節", key, limit);
        }

        add(out, seen, reading, "かな", key, limit);
        String kata = KanaTransforms.toKatakana(key);
        if (!kata.equals(reading)) add(out, seen, kata, "カナ", key, limit);

        if (ready && key.length() >= 2 && out.size() < limit) {
            for (Candidate c : prefixPredictions(key, previousWord, limit - out.size())) {
                add(out, seen, c.text, c.note, c.sourceReading, limit);
            }
        }
        return out;
    }

    private static void add(List<Candidate> out, Set<String> seen,
                            String text, String note, String sourceReading, int limit) {
        if (text == null || text.isEmpty() || out.size() >= limit || !seen.add(text)) return;
        out.add(new Candidate(text, note, sourceReading));
    }

    private List<String> rank(String reading, List<String> base, String previousWord) {
        ArrayList<String> copy = new ArrayList<>(base);
        final Map<String, Integer> baseIndex = new HashMap<>();
        for (int i = 0; i < base.size(); i++) baseIndex.put(base.get(i), i);
        copy.sort((a, b) -> {
            int sa = usageScore(reading, a, previousWord);
            int sb = usageScore(reading, b, previousWord);
            if (sa != sb) return Integer.compare(sb, sa);
            return Integer.compare(baseIndex.get(a), baseIndex.get(b));
        });
        return copy;
    }

    private int usageScore(String reading, String candidate, String previousWord) {
        int score = prefs.getInt("u|" + reading + "|" + candidate, 0) * 20;
        if (previousWord != null && !previousWord.isEmpty()) {
            score += prefs.getInt("b|" + previousWord + "|" + candidate, 0) * 30;
        }
        return score;
    }

    public void remember(String reading, String candidate, String previousWord) {
        if (candidate == null || candidate.isEmpty()) return;
        String r = norm(reading);
        SharedPreferences.Editor ed = prefs.edit();
        if (!r.isEmpty()) {
            String uk = "u|" + r + "|" + candidate;
            ed.putInt(uk, Math.min(9999, prefs.getInt(uk, 0) + 1));
        }
        if (previousWord != null && !previousWord.isEmpty()) {
            String bk = "b|" + previousWord + "|" + candidate;
            ed.putInt(bk, Math.min(9999, prefs.getInt(bk, 0) + 1));
        }

        ArrayList<String> recent = new ArrayList<>();
        recent.add(candidate);
        String old = prefs.getString("recent", "");
        if (old != null && !old.isEmpty()) {
            for (String x : old.split("\u001F")) {
                if (!x.isEmpty() && !x.equals(candidate)) recent.add(x);
                if (recent.size() >= 24) break;
            }
        }
        ed.putString("recent", String.join("\u001F", recent));
        ed.apply();
    }

    public List<Candidate> recentCandidates(int limit) {
        ArrayList<Candidate> out = new ArrayList<>();
        String old = prefs.getString("recent", "");
        if (old == null || old.isEmpty()) return out;
        for (String x : old.split("\u001F")) {
            if (!x.isEmpty()) out.add(new Candidate(x, "最近", ""));
            if (out.size() >= limit) break;
        }
        return out;
    }

    private List<Candidate> prefixPredictions(String prefix, String previousWord, int limit) {
        ArrayList<Candidate> out = new ArrayList<>();
        if (limit <= 0) return out;
        int scanned = 0;
        for (Map.Entry<String, List<String>> e : dict.tailMap(prefix, true).entrySet()) {
            String r = e.getKey();
            if (!r.startsWith(prefix)) break;
            if (hasOkuriMarker(r)) continue;
            if (++scanned > 80) break;
            List<String> ranked = rank(r, e.getValue(), previousWord);
            if (!ranked.isEmpty()) {
                out.add(new Candidate(ranked.get(0), "予測 " + r, r));
                if (out.size() >= limit) break;
            }
        }
        return out;
    }

    private boolean hasOkuriMarker(String key) {
        if (key.isEmpty()) return false;
        char c = key.charAt(key.length() - 1);
        return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
    }

    private List<String> okuriCandidates(String reading) {
        if (reading.length() < 2) return Collections.emptyList();
        ArrayList<String> out = new ArrayList<>();
        HashSet<String> seen = new HashSet<>();
        String codes = "kgsztdnhbpmrywiaeoucfjvqxl";
        for (int split = 1; split < reading.length(); split++) {
            String stem = reading.substring(0, split);
            String suffix = reading.substring(split);
            for (int ci = 0; ci < codes.length(); ci++) {
                char code = codes.charAt(ci);
                List<String> vals = dict.get(stem + code);
                if (vals == null || !okuriCompatible(code, suffix)) continue;
                int take = Math.min(4, vals.size());
                for (int k = 0; k < take; k++) {
                    String converted = vals.get(k) + suffix;
                    if (seen.add(converted)) out.add(converted);
                    if (out.size() >= 8) return out;
                }
            }
        }
        return out;
    }

    private boolean okuriCompatible(char code, String suffix) {
        if (suffix.isEmpty()) return false;
        switch (code) {
            case 'k':
                if (oneOf(suffix, "く","かない","きます","いた","いて","けば","こう")) return true;
                break;
            case 'g':
                if (oneOf(suffix, "ぐ","がない","ぎます","いだ","いで","げば","ごう")) return true;
                break;
            case 's':
                if (oneOf(suffix, "す","さない","します","した","して","せば","そう")) return true;
                break;
            case 't':
                if (oneOf(suffix, "つ","たない","ちます","った","って","てば","とう")) return true;
                break;
            case 'n':
                if (oneOf(suffix, "ぬ","なない","にます","んだ","んで","ねば","のう")) return true;
                break;
            case 'b':
                if (oneOf(suffix, "ぶ","ばない","びます","んだ","んで","べば","ぼう")) return true;
                break;
            case 'm':
                if (oneOf(suffix, "む","まない","みます","んだ","んで","めば","もう")) return true;
                break;
            case 'r':
                if (oneOf(suffix, "る","らない","ります","った","って","れば","ろう",
                        "ない","ます","た","て","れば","よう")) return true;
                break;
            case 'w':
                if (oneOf(suffix, "う","わない","います","った","って","えば","おう")) return true;
                break;
            case 'i':
                if (oneOf(suffix, "い","くない","かった","くて","ければ","さ","そう")) return true;
                break;
        }
        return kanaInitialCode(suffix.charAt(0)) == code;
    }

    private boolean oneOf(String s, String... vals) {
        for (String v : vals) if (v.equals(s)) return true;
        return false;
    }

    private char kanaInitialCode(char ch) {
        if ("かきくけこがぎぐげご".indexOf(ch) >= 0) return 'k';
        if ("さしすせそざじずぜぞ".indexOf(ch) >= 0) return 's';
        if ("たちつてとだぢづでど".indexOf(ch) >= 0) return 't';
        if ("なにぬねの".indexOf(ch) >= 0) return 'n';
        if ("はひふへほばびぶべぼぱぴぷぺぽ".indexOf(ch) >= 0) return 'h';
        if ("まみむめも".indexOf(ch) >= 0) return 'm';
        if ("やゆよ".indexOf(ch) >= 0) return 'y';
        if ("らりるれろ".indexOf(ch) >= 0) return 'r';
        if ("わを".indexOf(ch) >= 0) return 'w';
        if (ch == 'あ') return 'a';
        if (ch == 'い') return 'i';
        if (ch == 'う') return 'u';
        if (ch == 'え') return 'e';
        if (ch == 'お') return 'o';
        return '?';
    }

    private String segmentedBest(String reading) {
        int n = reading.length();
        if (n < 3 || n > 40) return null;

        double[] score = new double[n + 1];
        String[] text = new String[n + 1];
        for (int i = 0; i <= n; i++) {
            score[i] = -1e30;
            text[i] = null;
        }
        score[0] = 0;
        text[0] = "";

        for (int i = 0; i < n; i++) {
            if (text[i] == null) continue;

            if (score[i] + 0.05 > score[i + 1]) {
                score[i + 1] = score[i] + 0.05;
                text[i + 1] = text[i] + reading.substring(i, i + 1);
            }

            int maxEnd = Math.min(n, i + 12);
            for (int j = i + 1; j <= maxEnd; j++) {
                String sub = reading.substring(i, j);
                List<String> vals = dict.get(sub);
                if (vals == null || vals.isEmpty()) continue;
                int len = j - i;
                if (len == 1) continue;
                String cand = vals.get(0);
                double s = score[i] + len * len + 0.5;
                if (s > score[j]) {
                    score[j] = s;
                    text[j] = text[i] + cand;
                }
            }
        }
        if (text[n] == null || text[n].equals(reading)) return null;
        return text[n];
    }
}
