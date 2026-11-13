package dev.kana23x2.ime;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;

import android.content.ClipData;
import android.content.ClipboardManager;
import android.content.Context;
import android.os.PersistableBundle;
import android.text.InputType;
import android.text.Selection;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.BaseInputConnection;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputConnection;
import android.widget.TextView;

import java.lang.reflect.Field;
import java.lang.reflect.Method;

import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.Robolectric;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.android.controller.ServiceController;
import org.robolectric.annotation.Config;

@RunWith(RobolectricTestRunner.class)
@Config(sdk = 28)
public class KanaImeServiceTest {
    public static class TestIme extends KanaImeService {
        BaseInputConnection connection;
        EditorInfo editor;

        @Override public InputConnection getCurrentInputConnection() { return connection; }
        @Override public EditorInfo getCurrentInputEditorInfo() { return editor; }
    }

    private ServiceController<TestIme> controller;
    private TestIme ime;

    @Before public void setUp() {
        controller = Robolectric.buildService(TestIme.class).create();
        ime = controller.get();
        ime.connection = new BaseInputConnection(new View(ime), true);
        ime.getSharedPreferences("candidate_learning", Context.MODE_PRIVATE).edit().clear().commit();
        ime.getSharedPreferences("clip_history", Context.MODE_PRIVATE).edit().clear().commit();
        start(InputType.TYPE_CLASS_TEXT, 0);
    }

    @After public void tearDown() { controller.destroy(); }

    private void start(int inputType, int options) {
        ime.editor = new EditorInfo();
        ime.editor.inputType = inputType;
        ime.editor.imeOptions = options;
        ime.onStartInput(ime.editor, false);
    }

    private Object call(String name, Class<?>[] types, Object... values) throws Exception {
        Method method = KanaImeService.class.getDeclaredMethod(name, types);
        method.setAccessible(true);
        return method.invoke(ime, values);
    }

    private void type(String text) throws Exception {
        call("inputComposing", new Class<?>[]{String.class}, text);
    }

    private void backspace() throws Exception { call("backspace", new Class<?>[]{}); }
    private String text() { return ime.connection.getEditable().toString(); }

    @Test public void deletingLastComposingKanaRemovesItFromTheEditor() throws Exception {
        type("か");
        backspace();
        assertEquals("", text());
        assertEquals(-1, BaseInputConnection.getComposingSpanStart(ime.connection.getEditable()));
    }

    @Test public void committedSupplementaryCharactersDeleteWithoutBrokenSurrogates() throws Exception {
        for (String symbol : new String[]{"😀", "𠮷"}) {
            ime.connection.getEditable().clear();
            ime.connection.commitText("a" + symbol, 1);
            backspace();
            assertEquals("a", text());
        }
    }

    @Test public void backspaceDeletesSelectionInsteadOfAnAdjacentCharacter() throws Exception {
        ime.connection.commitText("abcd", 1);
        Selection.setSelection(ime.connection.getEditable(), 1, 3);
        backspace();
        assertEquals("ad", text());
    }

    @Test public void cursorMoveEndsCompositionWithoutReinsertingTheOldBuffer() throws Exception {
        type("かな");
        Selection.setSelection(ime.connection.getEditable(), 0);
        ime.onUpdateSelection(2, 2, 0, 0, 0, 2);
        type("あ");
        assertEquals("あかな", text());
    }

    @Test public void recreatingInputViewPreservesActiveComposition() throws Exception {
        type("かな");
        ime.onStartInputView(ime.editor, true);
        type("あ");
        assertEquals("かなあ", text());
    }

    @Test public void prolongedSoundMarkRemainsInTheSameComposingWord() throws Exception {
        View keyboard = ime.onCreateInputView();
        type("コ");
        findText(keyboard, "ー").performClick();
        type("ヒ");
        findText(keyboard, "ー").performClick();
        assertEquals("コーヒー", text());
        assertEquals(0, BaseInputConnection.getComposingSpanStart(ime.connection.getEditable()));
        assertEquals(4, BaseInputConnection.getComposingSpanEnd(ime.connection.getEditable()));
    }

    @Test public void passwordsAndNoPersonalizedLearningDoNotPersistCandidateHistory() throws Exception {
        int[] types = {
            InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD,
            InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_VISIBLE_PASSWORD,
            InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_WEB_PASSWORD,
            InputType.TYPE_CLASS_NUMBER | InputType.TYPE_NUMBER_VARIATION_PASSWORD,
            InputType.TYPE_CLASS_TEXT
        };
        for (int type : types) {
            start(type, type == InputType.TYPE_CLASS_TEXT
                    ? EditorInfo.IME_FLAG_NO_PERSONALIZED_LEARNING : 0);
            call("chooseCandidate", new Class<?>[]{CandidateEngine.Candidate.class},
                    new CandidateEngine.Candidate("秘密", "", "ひみつ"));
            assertFalse(ime.getSharedPreferences("candidate_learning", Context.MODE_PRIVATE)
                    .contains("recent"));
        }
    }

    @Test public void passwordFieldDoesNotDisplayPreviouslyLearnedCandidates() throws Exception {
        ime.getSharedPreferences("candidate_learning", Context.MODE_PRIVATE).edit()
                .putString("recent", "秘密").commit();
        start(InputType.TYPE_CLASS_TEXT | InputType.TYPE_TEXT_VARIATION_PASSWORD, 0);
        ime.onCreateInputView();
        Field row = KanaImeService.class.getDeclaredField("candidateRow");
        row.setAccessible(true);
        assertEquals(0, ((ViewGroup) row.get(ime)).getChildCount());
    }

    @Test public void sensitiveClipboardMarkerIsHonoredBeforeAndroid13() throws Exception {
        ClipData clip = ClipData.newPlainText("password", "secret");
        PersistableBundle extras = new PersistableBundle();
        extras.putBoolean("android.content.extra.IS_SENSITIVE", true);
        clip.getDescription().setExtras(extras);
        ((ClipboardManager) ime.getSystemService(Context.CLIPBOARD_SERVICE)).setPrimaryClip(clip);
        call("capturePrimaryClip", new Class<?>[]{});
        assertFalse(ime.getSharedPreferences("clip_history", Context.MODE_PRIVATE).contains("history"));
    }

    @Test public void clearingClipboardHistoryDoesNotImmediatelyRecaptureCurrentClip() throws Exception {
        ime.onCreateInputView();
        ((ClipboardManager) ime.getSystemService(Context.CLIPBOARD_SERVICE))
                .setPrimaryClip(ClipData.newPlainText("text", "copied text"));
        call("showClipboard", new Class<?>[]{});
        Field root = KanaImeService.class.getDeclaredField("root");
        root.setAccessible(true);
        findText((View) root.get(ime), "清").performClick();
        assertFalse(ime.getSharedPreferences("clip_history", Context.MODE_PRIVATE).contains("history"));
    }

    private TextView findText(View view, String text) {
        if (view instanceof TextView && text.equals(((TextView) view).getText().toString())) {
            return (TextView) view;
        }
        if (view instanceof ViewGroup) {
            ViewGroup group = (ViewGroup) view;
            for (int i = 0; i < group.getChildCount(); i++) {
                TextView found = findText(group.getChildAt(i), text);
                if (found != null) return found;
            }
        }
        return null;
    }
}
