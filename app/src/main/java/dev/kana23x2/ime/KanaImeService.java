package dev.kana23x2.ime;

import android.content.ClipData;
import android.content.ClipDescription;
import android.content.ClipboardManager;
import android.content.Context;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.inputmethodservice.InputMethodService;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.text.InputType;
import android.text.SpannableString;
import android.text.Spanned;
import android.text.style.AbsoluteSizeSpan;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputConnection;
import android.view.inputmethod.InputMethodManager;
import android.widget.FrameLayout;
import android.widget.GridLayout;
import android.widget.HorizontalScrollView;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.LinkedHashSet;
import java.util.List;

public class KanaImeService extends InputMethodService {
    private static final String[] BASE_KEYS = {
            "Q","W","E","R","T","Y","U","I","O","P",
            "A","S","D","F","G","H","J","K","L",
            "V","B","N","M"
    };

    private static final String[] BOARD_1 = {
            "あ","い","う","え","お",
            "か","き","く","け","こ",
            "さ","し","す","せ","そ",
            "た","ち","つ","て","と",
            "や","ゆ","よ"
    };
    private static final String[] BOARD_2 = {
            "な","に","ぬ","ね","の",
            "は","ひ","ふ","へ","ほ",
            "ま","み","む","め","も",
            "ら","り","る","れ","ろ",
            "わ","を","ん"
    };

    private int board = 0;
    private boolean katakana = false;
    private String composing = "";
    private String previousWord = "";
    private boolean sensitiveField = false;

    private CandidateEngine engine;
    private ClipboardManager clipboard;
    private SharedPreferences clipPrefs;
    private ClipboardManager.OnPrimaryClipChangedListener clipListener;
    private final Handler mainHandler = new Handler(Looper.getMainLooper());

    private LinearLayout root;
    private LinearLayout candidateRow;
    private HorizontalScrollView candidateScroll;
    private FrameLayout contentFrame;
    private View keyboardView;
    private TextView stateLabel;
    private final ArrayList<TextView> baseKeyViews = new ArrayList<>();
    private TextView zKey;
    private TextView xKey;
    private TextView cKey;
    private TextView shiftKey;

    private final int BG = Color.rgb(232, 235, 240);
    private final int KEY = Color.rgb(250, 250, 252);
    private final int MOD = Color.rgb(202, 207, 216);
    private final int ACTIVE = Color.rgb(184, 193, 207);
    private final int TEXT = Color.rgb(20, 20, 22);
    private final int MUTED = Color.rgb(105, 110, 118);

    @Override
    public void onCreate() {
        super.onCreate();
        engine = new CandidateEngine(this);
        clipboard = (ClipboardManager) getSystemService(Context.CLIPBOARD_SERVICE);
        clipPrefs = getSharedPreferences("clip_history", Context.MODE_PRIVATE);
        if (clipboard != null) {
            clipListener = this::capturePrimaryClip;
            clipboard.addPrimaryClipChangedListener(clipListener);
        }

        Thread loader = new Thread(() -> {
            engine.load();
            mainHandler.post(this::updateCandidates);
        }, "Kana23-Dictionary");
        loader.setDaemon(true);
        loader.start();
    }

    private int dp(float v) {
        return (int) (v * getResources().getDisplayMetrics().density + 0.5f);
    }

    private GradientDrawable bg(int color, float radiusDp) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(dp(radiusDp));
        d.setStroke(dp(1), Color.rgb(210, 214, 221));
        return d;
    }

    @Override
    public View onCreateInputView() {
        root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(BG);

        candidateScroll = new HorizontalScrollView(this);
        candidateScroll.setHorizontalScrollBarEnabled(false);
        candidateRow = new LinearLayout(this);
        candidateRow.setOrientation(LinearLayout.HORIZONTAL);
        candidateRow.setGravity(Gravity.CENTER_VERTICAL);
        candidateRow.setPadding(dp(4), dp(3), dp(4), dp(3));
        candidateScroll.addView(candidateRow, new HorizontalScrollView.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.MATCH_PARENT));
        root.addView(candidateScroll, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(48)));

        LinearLayout tools = new LinearLayout(this);
        tools.setGravity(Gravity.CENTER_VERTICAL);
        tools.setPadding(dp(4), 0, dp(4), 0);
        tools.addView(toolButton("⌨", v -> showKeyboard()), weighted());
        tools.addView(toolButton("😀", v -> showEmoji()), weighted());
        tools.addView(toolButton("📋", v -> showClipboard()), weighted());
        tools.addView(toolButton("ー", v -> commitLiteral("ー")), weighted());
        tools.addView(toolButton("123", v -> showNumbers()), weighted());
        tools.addView(toolButton("符", v -> showSymbols("和")), weighted());
        stateLabel = toolButton("", v -> {
            board ^= 1;
            refreshKeyboardLabels();
        });
        tools.addView(stateLabel, weighted());
        root.addView(tools, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(47)));

        contentFrame = new FrameLayout(this);
        root.addView(contentFrame, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(294)));

        keyboardView = buildKeyboard();
        showKeyboard();
        updateCandidates();
        return root;
    }

    private LinearLayout.LayoutParams weighted() {
        return new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MATCH_PARENT, 1f);
    }

    private TextView toolButton(String label, View.OnClickListener listener) {
        TextView v = new TextView(this);
        v.setText(label);
        v.setTextSize(17);
        v.setTextColor(TEXT);
        v.setGravity(Gravity.CENTER);
        v.setTypeface(Typeface.DEFAULT_BOLD);
        v.setOnClickListener(listener);
        v.setBackground(bg(BG, 8));
        return v;
    }

    private View buildKeyboard() {
        LinearLayout keyboard = new LinearLayout(this);
        keyboard.setOrientation(LinearLayout.VERTICAL);
        keyboard.setPadding(dp(4), dp(3), dp(4), dp(5));

        baseKeyViews.clear();

        LinearLayout row1 = keyRow();
        for (int i = 0; i < 10; i++) row1.addView(makeBaseKey(i), keyParams(1f));
        keyboard.addView(row1, rowParams(1f));

        LinearLayout row2 = keyRow();
        row2.addView(new SpacePad(this), keyParams(0.5f));
        for (int i = 10; i < 19; i++) row2.addView(makeBaseKey(i), keyParams(1f));
        row2.addView(new SpacePad(this), keyParams(0.5f));
        keyboard.addView(row2, rowParams(1f));

        LinearLayout row3 = keyRow();
        shiftKey = makeActionKey("⇧\nShift", v -> {
            katakana = !katakana;
            refreshKeyboardLabels();
        }, MOD);
        row3.addView(shiftKey, keyParams(1.35f));

        zKey = makeQwertyOperator("Z", "板", v -> {
            board ^= 1;
            refreshKeyboardLabels();
        });
        xKey = makeQwertyOperator("X", "◌゙", v -> toggleDakuten());
        cKey = makeQwertyOperator("C", "小", v -> toggleC());
        row3.addView(zKey, keyParams(1f));
        row3.addView(xKey, keyParams(1f));
        row3.addView(cKey, keyParams(1f));

        for (int i = 19; i < 23; i++) row3.addView(makeBaseKey(i), keyParams(1f));

        row3.addView(makeActionKey("⌫", v -> backspace(), MOD), keyParams(1.35f));
        keyboard.addView(row3, rowParams(1f));

        LinearLayout row4 = keyRow();
        row4.addView(makeActionKey("符", v -> showSymbols("和"), MOD), keyParams(1.05f));
        row4.addView(makeActionKey("123", v -> showNumbers(), MOD), keyParams(1.05f));
        row4.addView(makeActionKey("、。", v -> showSymbols("和"), KEY), keyParams(0.95f));
        row4.addView(makeActionKey("space", v -> spaceOrConvert(), KEY), keyParams(3.25f));
        row4.addView(makeActionKey("🌐", v -> switchIme(), MOD), keyParams(1.05f));
        row4.addView(makeActionKey("换行", v -> enter(), MOD), keyParams(1.45f));
        keyboard.addView(row4, rowParams(1.05f));

        refreshKeyboardLabels();
        return keyboard;
    }

    private LinearLayout keyRow() {
        LinearLayout row = new LinearLayout(this);
        row.setGravity(Gravity.CENTER);
        return row;
    }

    private LinearLayout.LayoutParams rowParams(float weight) {
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, 0, weight);
        p.topMargin = dp(2);
        return p;
    }

    private LinearLayout.LayoutParams keyParams(float weight) {
        LinearLayout.LayoutParams p = new LinearLayout.LayoutParams(
                0, ViewGroup.LayoutParams.MATCH_PARENT, weight);
        p.setMargins(dp(2), dp(2), dp(2), dp(2));
        return p;
    }

    private TextView makeBaseKey(int index) {
        TextView v = new TextView(this);
        v.setGravity(Gravity.CENTER);
        v.setTextColor(TEXT);
        v.setBackground(bg(KEY, 7));
        v.setTag(index);
        v.setOnClickListener(x -> inputKana(index));
        baseKeyViews.add(v);
        return v;
    }

    private TextView makeQwertyOperator(String latin, String op, View.OnClickListener l) {
        TextView v = new TextView(this);
        v.setGravity(Gravity.CENTER);
        v.setTextColor(TEXT);
        v.setBackground(bg(MOD, 7));
        v.setOnClickListener(l);
        setTwoLineLabel(v, latin, op, 10, 22);
        return v;
    }

    private TextView makeActionKey(String label, View.OnClickListener l, int color) {
        TextView v = new TextView(this);
        v.setGravity(Gravity.CENTER);
        v.setTextColor(TEXT);
        v.setTextSize(16);
        v.setTypeface(Typeface.DEFAULT_BOLD);
        v.setText(label);
        v.setBackground(bg(color, 8));
        v.setOnClickListener(l);
        return v;
    }

    private void setTwoLineLabel(TextView v, String top, String main, int topSp, int mainSp) {
        String text = top + "\n" + main;
        SpannableString ss = new SpannableString(text);
        ss.setSpan(new AbsoluteSizeSpan(topSp, true), 0, top.length(),
                Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        ss.setSpan(new AbsoluteSizeSpan(mainSp, true), top.length() + 1, text.length(),
                Spanned.SPAN_EXCLUSIVE_EXCLUSIVE);
        v.setText(ss);
        v.setLineSpacing(0f, 0.92f);
    }

    private String currentKana(int index) {
        String h = (board == 0 ? BOARD_1[index] : BOARD_2[index]);
        return katakana ? KanaTransforms.toKatakana(h) : h;
    }

    private void refreshKeyboardLabels() {
        if (baseKeyViews.size() == 23) {
            for (int i = 0; i < 23; i++) {
                setTwoLineLabel(baseKeyViews.get(i), BASE_KEYS[i], currentKana(i), 9, 24);
            }
        }
        if (zKey != null) {
            setTwoLineLabel(zKey, "Z", board == 0 ? "板¹" : "板²", 9, 18);
            zKey.setBackground(bg(board == 1 ? ACTIVE : MOD, 7));
        }
        if (xKey != null) setTwoLineLabel(xKey, "X", "◌゙", 9, 19);
        if (cKey != null) setTwoLineLabel(cKey, "C", board == 0 ? "小" : "◌゚", 9, 19);
        if (shiftKey != null) {
            shiftKey.setText(katakana ? "⇧\n片假名" : "⇧\n平假名");
            shiftKey.setBackground(bg(katakana ? ACTIVE : MOD, 8));
        }
        if (stateLabel != null) {
            stateLabel.setText((board == 0 ? "①" : "②") + (katakana ? " 片" : " 平"));
            stateLabel.setBackground(bg(board == 1 ? ACTIVE : BG, 8));
        }
    }

    private void showKeyboard() {
        if (contentFrame == null) return;
        contentFrame.removeAllViews();
        if (keyboardView == null) keyboardView = buildKeyboard();
        contentFrame.addView(keyboardView, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
    }

    private void inputKana(int index) {
        showKeyboard();
        composing += currentKana(index);
        syncComposing();
    }

    private void toggleDakuten() {
        if (composing.isEmpty()) return;
        composing = KanaTransforms.toggleDakuten(composing);
        syncComposing();
    }

    private void toggleC() {
        if (composing.isEmpty()) return;
        composing = (board == 0)
                ? KanaTransforms.toggleSmall(composing)
                : KanaTransforms.toggleHandakuten(composing);
        syncComposing();
    }

    private void syncComposing() {
        InputConnection ic = getCurrentInputConnection();
        if (ic == null) return;
        if (composing.isEmpty()) ic.finishComposingText();
        else ic.setComposingText(composing, 1);
        updateCandidates();
    }

    private void backspace() {
        if (!composing.isEmpty()) {
            int cp = composing.codePointBefore(composing.length());
            int cut = Character.charCount(cp);
            composing = composing.substring(0, composing.length() - cut);
            syncComposing();
            return;
        }
        InputConnection ic = getCurrentInputConnection();
        if (ic != null) ic.deleteSurroundingText(1, 0);
    }

    private void spaceOrConvert() {
        if (!composing.isEmpty()) {
            List<CandidateEngine.Candidate> cands = engine.candidates(composing, previousWord, 1);
            if (!cands.isEmpty() && !"かな".equals(cands.get(0).note)) {
                chooseCandidate(cands.get(0));
                return;
            }
            commitCompositionRaw();
        }
        commitLiteral(" ");
    }

    private void enter() {
        if (!composing.isEmpty()) commitCompositionRaw();
        InputConnection ic = getCurrentInputConnection();
        if (ic == null) return;
        EditorInfo info = getCurrentInputEditorInfo();
        int action = info == null ? EditorInfo.IME_ACTION_NONE : (info.imeOptions & EditorInfo.IME_MASK_ACTION);
        if (action != EditorInfo.IME_ACTION_NONE && action != EditorInfo.IME_ACTION_UNSPECIFIED) {
            ic.performEditorAction(action);
        } else {
            ic.sendKeyEvent(new KeyEvent(KeyEvent.ACTION_DOWN, KeyEvent.KEYCODE_ENTER));
            ic.sendKeyEvent(new KeyEvent(KeyEvent.ACTION_UP, KeyEvent.KEYCODE_ENTER));
        }
    }

    private void commitCompositionRaw() {
        if (composing.isEmpty()) return;
        String x = composing;
        InputConnection ic = getCurrentInputConnection();
        if (ic != null) {
            ic.commitText(x, 1);
            previousWord = x;
        }
        composing = "";
        updateCandidates();
    }

    private void commitLiteral(String text) {
        if (text == null || text.isEmpty()) return;
        if (!composing.isEmpty()) commitCompositionRaw();
        InputConnection ic = getCurrentInputConnection();
        if (ic != null) ic.commitText(text, 1);
        if (!text.trim().isEmpty()) previousWord = text;
        updateCandidates();
    }

    private void chooseCandidate(CandidateEngine.Candidate c) {
        InputConnection ic = getCurrentInputConnection();
        if (ic == null || c == null) return;
        String typedReading = composing;
        ic.commitText(c.text, 1);
        engine.remember(c.sourceReading == null || c.sourceReading.isEmpty() ? typedReading : c.sourceReading,
                c.text, previousWord);
        previousWord = c.text;
        composing = "";
        updateCandidates();
    }

    private void updateCandidates() {
        if (candidateRow == null) return;
        candidateRow.removeAllViews();

        if (!engine.isReady() && composing.isEmpty()) {
            TextView loading = candidateChip("词典载入中…", null);
            loading.setTextColor(MUTED);
            candidateRow.addView(loading);
            return;
        }

        List<CandidateEngine.Candidate> list = engine.candidates(composing, previousWord, 14);
        if (list.isEmpty()) {
            TextView hint = candidateChip("23×2 Kana", null);
            hint.setTextColor(MUTED);
            candidateRow.addView(hint);
            return;
        }
        for (CandidateEngine.Candidate c : list) {
            String label = c.text;
            if (c.note != null && !c.note.isEmpty() && ("活用".equals(c.note) || "文節".equals(c.note))) {
                label += "  ·" + c.note;
            }
            candidateRow.addView(candidateChip(label, v -> chooseCandidate(c)));
        }
        candidateScroll.post(() -> candidateScroll.scrollTo(0, 0));
    }

    private TextView candidateChip(String text, View.OnClickListener listener) {
        TextView v = new TextView(this);
        v.setText(text);
        v.setTextSize(18);
        v.setTextColor(TEXT);
        v.setGravity(Gravity.CENTER);
        v.setPadding(dp(12), 0, dp(12), 0);
        v.setBackground(bg(KEY, 9));
        if (listener != null) v.setOnClickListener(listener);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.WRAP_CONTENT, ViewGroup.LayoutParams.MATCH_PARENT);
        lp.setMargins(dp(2), dp(2), dp(2), dp(2));
        v.setLayoutParams(lp);
        return v;
    }

    private void switchIme() {
        if (Build.VERSION.SDK_INT >= 28 && switchToNextInputMethod(false)) return;
        InputMethodManager imm = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
        if (imm != null) imm.showInputMethodPicker();
    }

    private static final String[] EMOJI = {
            "😀","😃","😄","😁","😆","😅","😂","🤣","😊","🙂","🙃","😉",
            "😍","🥰","😘","😋","😎","🤓","🧐","🤔","🤨","😐","😑","🙄",
            "😏","😣","😥","😮","🤐","😯","😪","😫","🥱","😴","🤤","😒",
            "😓","😔","😕","🙁","☹️","😲","🥺","😦","😧","😨","😰","😢",
            "😭","😱","😖","😞","😤","😡","😠","🤬","🤯","😳","🥵","🥶",
            "😶‍🌫️","😵","😵‍💫","🤢","🤮","🤧","😷","🤒","🤕","🤑","🤠","😈",
            "👿","👻","💀","☠️","👽","🤖","💩","😺","😸","😹","😻","😼",
            "👍","👎","👌","🤌","🤏","✌️","🤞","🫰","🤟","🤘","🤙","👈",
            "👉","👆","👇","☝️","✋","🤚","🖐️","🖖","👋","🤝","👏","🙌",
            "🫶","🙏","✍️","💪","🧠","👀","👁️","👄","❤️","🧡","💛","💚",
            "💙","💜","🖤","🤍","🤎","💔","❣️","💕","💞","💓","💗","💖",
            "✨","🔥","💥","💯","✅","❌","⭕","❗","❓","⚠️","🚀","🎉"
    };

    private void showEmoji() {
        showGridPanel("Emoji", Arrays.asList(EMOJI), 8);
    }

    private void showNumbers() {
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(6), dp(6), dp(6), dp(6));

        box.addView(panelHeader("数字", "⌨", v -> showKeyboard()),
                new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(36)));

        GridLayout grid = new GridLayout(this);
        grid.setColumnCount(3);
        String[] keys = {"1","2","3","4","5","6","7","8","9",".","0","⌫"};
        for (String k : keys) {
            TextView b = panelKey(k, v -> {
                if ("⌫".equals(k)) backspace();
                else commitLiteral(k);
            });
            GridLayout.LayoutParams gp = new GridLayout.LayoutParams();
            gp.width = 0;
            gp.height = dp(58);
            gp.columnSpec = GridLayout.spec(GridLayout.UNDEFINED, 1f);
            gp.setMargins(dp(3), dp(3), dp(3), dp(3));
            grid.addView(b, gp);
        }
        box.addView(grid, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        showPanel(box);
    }

    private void showClipboard() {
        capturePrimaryClip();

        LinearLayout outer = new LinearLayout(this);
        outer.setOrientation(LinearLayout.VERTICAL);
        outer.setPadding(dp(5), dp(4), dp(5), dp(4));

        LinearLayout head = new LinearLayout(this);
        head.setGravity(Gravity.CENTER_VERTICAL);
        TextView title = panelHeader("剪贴板", "", null);
        head.addView(title, new LinearLayout.LayoutParams(0, dp(36), 1f));
        head.addView(toolButton("↻", v -> showClipboard()), new LinearLayout.LayoutParams(dp(48), dp(36)));
        head.addView(toolButton("清", v -> {
            clipPrefs.edit().remove("history").apply();
            showClipboard();
        }), new LinearLayout.LayoutParams(dp(48), dp(36)));
        head.addView(toolButton("⌨", v -> showKeyboard()), new LinearLayout.LayoutParams(dp(48), dp(36)));
        outer.addView(head);

        ScrollView scroll = new ScrollView(this);
        LinearLayout list = new LinearLayout(this);
        list.setOrientation(LinearLayout.VERTICAL);
        List<String> clips = getClipHistory();
        if (sensitiveField) {
            TextView warning = new TextView(this);
            warning.setText("密码/敏感输入框：剪贴板读取已停用");
            warning.setTextColor(MUTED);
            warning.setTextSize(15);
            warning.setPadding(dp(12), dp(16), dp(12), dp(16));
            list.addView(warning);
        } else if (clips.isEmpty()) {
            TextView empty = new TextView(this);
            empty.setText("暂无本地剪贴板历史。打开此页时会读取当前系统剪贴板。");
            empty.setTextColor(MUTED);
            empty.setTextSize(15);
            empty.setPadding(dp(12), dp(16), dp(12), dp(16));
            list.addView(empty);
        } else {
            for (String clip : clips) {
                String oneLine = clip.replace("\n", " ↵ ");
                if (oneLine.length() > 90) oneLine = oneLine.substring(0, 90) + "…";
                TextView item = panelKey(oneLine, v -> {
                    commitLiteral(clip);
                    showKeyboard();
                });
                item.setGravity(Gravity.START | Gravity.CENTER_VERTICAL);
                item.setPadding(dp(12), dp(6), dp(12), dp(6));
                LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT, dp(52));
                lp.setMargins(dp(2), dp(2), dp(2), dp(2));
                list.addView(item, lp);
            }
        }
        scroll.addView(list);
        outer.addView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        showPanel(outer);
    }

    private void capturePrimaryClip() {
        if (sensitiveField || clipboard == null || !clipboard.hasPrimaryClip()) return;
        try {
            ClipDescription desc = clipboard.getPrimaryClipDescription();
            if (Build.VERSION.SDK_INT >= 33 && desc != null) {
                boolean sensitive = desc.getExtras() != null &&
                        desc.getExtras().getBoolean(ClipDescription.EXTRA_IS_SENSITIVE, false);
                if (sensitive) return;
            }
            ClipData data = clipboard.getPrimaryClip();
            if (data == null || data.getItemCount() == 0) return;
            CharSequence cs = data.getItemAt(0).coerceToText(this);
            if (cs == null) return;
            String s = cs.toString();
            if (s.isEmpty() || s.length() > 10000) return;
            saveClip(s);
        } catch (SecurityException ignored) {
        }
    }

    private void saveClip(String s) {
        LinkedHashSet<String> set = new LinkedHashSet<>();
        set.add(s);
        set.addAll(getClipHistory());
        ArrayList<String> list = new ArrayList<>(set);
        if (list.size() > 30) list = new ArrayList<>(list.subList(0, 30));
        clipPrefs.edit().putString("history", String.join("\u001E", list)).apply();
    }

    private List<String> getClipHistory() {
        String raw = clipPrefs.getString("history", "");
        ArrayList<String> out = new ArrayList<>();
        if (raw == null || raw.isEmpty()) return out;
        for (String s : raw.split("\u001E", -1)) if (!s.isEmpty()) out.add(s);
        return out;
    }

    private static final String[] SYM_JP = {
            "、","。","・","：","；","！","？","‼","⁉","…","‥","〜","ー","々","〆","〇","※","〒",
            "「","」","『","』","【","】","〔","〕","〈","〉","《","》","〖","〗","〘","〙",
            "（","）","［","］","｛","｝","〝","〟","゛","゜","゙","゚"
    };
    private static final String[] SYM_MATH = {
            "+","−","×","÷","=","≠","≈","≡","<",">","≤","≥","±","∓","∞","√","∛","∜",
            "∑","∏","∫","∬","∭","∮","∂","∇","∆","∈","∉","∋","∌","∩","∪","⊂","⊃","⊆","⊇",
            "⊕","⊗","∧","∨","¬","⇒","⇐","⇔","∀","∃","∄","∴","∵","∝","∠","⊥","∥","⌈","⌉","⌊","⌋"
    };
    private static final String[] SYM_ARROW = {
            "←","↑","→","↓","↔","↕","↖","↗","↘","↙","⇐","⇑","⇒","⇓","⇔","⇕",
            "↩","↪","↶","↷","⟵","⟶","⟷","⟸","⟹","⟺","⇢","⇠","➜","➝","➞","➟","➠","➡"
    };
    private static final String[] SYM_SHAPE = {
            "○","●","◎","◯","◉","◌","◍","□","■","◇","◆","△","▲","▽","▼","▷","▶","◁","◀",
            "☆","★","♠","♣","♥","♦","♡","♢","♧","♤","✓","✔","✕","✖","✚","✦","✧","✩","✪",
            "☀","☁","☂","☃","☄","☎","☑","☒","☕","⚑","⚐","⚙","⚡"
    };
    private static final String[] SYM_UNIT = {
            "¥","￥","$","€","£","₩","₽","₹","₿","¢","°","℃","℉","‰","‱","％","ℓ","㎜","㎝","㎞",
            "㎡","㎢","㎎","㎏","㏄","㏑","㏒","Hz","kHz","MHz","GHz","Ω","µ","Å","©","®","™","℠"
    };
    private static final String[] SYM_GREEK = {
            "Α","Β","Γ","Δ","Ε","Ζ","Η","Θ","Ι","Κ","Λ","Μ","Ν","Ξ","Ο","Π","Ρ","Σ","Τ","Υ","Φ","Χ","Ψ","Ω",
            "α","β","γ","δ","ε","ζ","η","θ","ι","κ","λ","μ","ν","ξ","ο","π","ρ","σ","ς","τ","υ","φ","χ","ψ","ω"
    };
    private static final String[] SYM_IPA = {
            "ɑ","ɐ","ɒ","æ","ɓ","ʙ","β","ɔ","ɕ","ç","ɗ","ɖ","ð","ʤ","ə","ɘ","ɚ","ɛ","ɜ","ɝ","ɞ",
            "ɟ","ʄ","ɡ","ɠ","ɢ","ʛ","ɦ","ɧ","ħ","ɥ","ʜ","ɨ","ɪ","ʝ","ɭ","ɬ","ɫ","ɮ","ʟ","ɱ",
            "ɯ","ɰ","ŋ","ɳ","ɲ","ɴ","ø","œ","ɶ","ɸ","ɹ","ɻ","ɺ","ɾ","ʀ","ʁ","ɽ","ʂ","ʃ","ʈ",
            "θ","ʊ","ʋ","ⱱ","ʌ","ɣ","ɤ","ʍ","χ","ʎ","ʏ","ɥ","ʐ","ʑ","ʒ","ʔ","ʕ","ʡ","ʢ"
    };
    private static final String[] SYM_KANA_EXT = {
            "ぁ","ぃ","ぅ","ぇ","ぉ","っ","ゃ","ゅ","ょ","ゎ","ゕ","ゖ",
            "ァ","ィ","ゥ","ェ","ォ","ッ","ャ","ュ","ョ","ヮ","ヵ","ヶ",
            "ㇰ","ㇱ","ㇲ","ㇳ","ㇴ","ㇵ","ㇶ","ㇷ","ㇸ","ㇹ","ㇺ","ㇻ","ㇼ","ㇽ","ㇾ","ㇿ",
            "ヷ","ヸ","ヹ","ヺ","゙","゚","ヽ","ヾ","ゝ","ゞ"
    };

    private void showSymbols(String category) {
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(4), dp(3), dp(4), dp(3));

        LinearLayout tabs = new LinearLayout(this);
        String[] cats = {"和","数","矢","形","単","希","IPA","假"};
        for (String c : cats) {
            TextView t = toolButton(c, v -> showSymbols(c));
            if (c.equals(category)) t.setBackground(bg(ACTIVE, 7));
            tabs.addView(t, new LinearLayout.LayoutParams(0, dp(38), 1f));
        }
        TextView close = toolButton("⌨", v -> showKeyboard());
        tabs.addView(close, new LinearLayout.LayoutParams(0, dp(38), 1f));
        box.addView(tabs);

        String[] vals;
        switch (category) {
            case "数": vals = SYM_MATH; break;
            case "矢": vals = SYM_ARROW; break;
            case "形": vals = SYM_SHAPE; break;
            case "単": vals = SYM_UNIT; break;
            case "希": vals = SYM_GREEK; break;
            case "IPA": vals = SYM_IPA; break;
            case "假": vals = SYM_KANA_EXT; break;
            case "和":
            default: vals = SYM_JP; break;
        }

        ScrollView scroll = new ScrollView(this);
        scroll.addView(symbolGrid(Arrays.asList(vals), 7));
        box.addView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        showPanel(box);
    }

    private void showGridPanel(String title, List<String> values, int columns) {
        LinearLayout outer = new LinearLayout(this);
        outer.setOrientation(LinearLayout.VERTICAL);
        outer.setPadding(dp(4), dp(3), dp(4), dp(3));
        outer.addView(panelHeader(title, "⌨", v -> showKeyboard()),
                new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, dp(38)));
        ScrollView scroll = new ScrollView(this);
        scroll.addView(symbolGrid(values, columns));
        outer.addView(scroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, 0, 1f));
        showPanel(outer);
    }

    private GridLayout symbolGrid(List<String> values, int columns) {
        GridLayout grid = new GridLayout(this);
        grid.setColumnCount(columns);
        for (String s : values) {
            TextView key = panelKey(s, v -> commitLiteral(s));
            GridLayout.LayoutParams gp = new GridLayout.LayoutParams();
            gp.width = 0;
            gp.height = dp(48);
            gp.columnSpec = GridLayout.spec(GridLayout.UNDEFINED, 1f);
            gp.setMargins(dp(2), dp(2), dp(2), dp(2));
            grid.addView(key, gp);
        }
        return grid;
    }

    private TextView panelHeader(String title, String action, View.OnClickListener listener) {
        TextView v = new TextView(this);
        v.setText(action == null || action.isEmpty() ? title : title + "     " + action);
        v.setTextSize(16);
        v.setTypeface(Typeface.DEFAULT_BOLD);
        v.setGravity(Gravity.CENTER);
        v.setTextColor(TEXT);
        v.setBackground(bg(MOD, 7));
        if (listener != null) v.setOnClickListener(listener);
        return v;
    }

    private TextView panelKey(String s, View.OnClickListener l) {
        TextView v = new TextView(this);
        v.setText(s);
        v.setTextSize(19);
        v.setTextColor(TEXT);
        v.setGravity(Gravity.CENTER);
        v.setBackground(bg(KEY, 7));
        v.setOnClickListener(l);
        return v;
    }

    private void showPanel(View panel) {
        if (contentFrame == null) return;
        contentFrame.removeAllViews();
        contentFrame.addView(panel, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
    }

    private static class SpacePad extends View {
        SpacePad(Context context) { super(context); }
    }

    @Override
    public void onDestroy() {
        if (clipboard != null && clipListener != null) clipboard.removePrimaryClipChangedListener(clipListener);
        super.onDestroy();
    }

    @Override
    public void onStartInput(EditorInfo attribute, boolean restarting) {
        super.onStartInput(attribute, restarting);
        composing = "";
        previousWord = "";
        sensitiveField = isSensitive(attribute);
        updateCandidates();
    }

    @Override
    public void onStartInputView(EditorInfo info, boolean restarting) {
        super.onStartInputView(info, restarting);
        sensitiveField = isSensitive(info);
        composing = "";
        showKeyboard();
        refreshKeyboardLabels();
        updateCandidates();
    }

    @Override
    public void onFinishInput() {
        composing = "";
        super.onFinishInput();
    }

    private boolean isSensitive(EditorInfo info) {
        if (info == null) return false;
        int cls = info.inputType & InputType.TYPE_MASK_CLASS;
        int var = info.inputType & InputType.TYPE_MASK_VARIATION;
        if (cls == InputType.TYPE_CLASS_TEXT) {
            return var == InputType.TYPE_TEXT_VARIATION_PASSWORD ||
                    var == InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD ||
                    var == InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD;
        }
        return cls == InputType.TYPE_CLASS_NUMBER && var == InputType.TYPE_NUMBER_VARIATION_PASSWORD;
    }
}
