package dev.kana23x2.ime;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Typeface;
import android.os.Bundle;
import android.provider.Settings;
import android.view.Gravity;
import android.view.ViewGroup;
import android.view.inputmethod.InputMethodManager;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;

public class MainActivity extends Activity {
    private int dp(float v) {
        return (int) (v * getResources().getDisplayMetrics().density + 0.5f);
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        ScrollView scroll = new ScrollView(this);
        LinearLayout box = new LinearLayout(this);
        box.setOrientation(LinearLayout.VERTICAL);
        box.setPadding(dp(24), dp(24), dp(24), dp(32));
        scroll.addView(box, new ScrollView.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.WRAP_CONTENT));

        TextView title = new TextView(this);
        title.setText("23×2 Kana");
        title.setTextSize(32);
        title.setTypeface(Typeface.DEFAULT_BOLD);
        box.addView(title);

        TextView intro = new TextView(this);
        intro.setText(
                "\nAndroid 系统级日文输入法测试版。\n\n" +
                "第1板：あ・か・さ・た・や\n" +
                "第2板：な・は・ま・ら・わ\n\n" +
                "Z：切板\n" +
                "X：浊点（可逆）\n" +
                "C：第1板小写 / 第2板半浊点（可逆）\n" +
                "Shift：平假名 / 片假名\n\n" +
                "候选引擎离线运行；包含 SKK 大词典、生僻 JIS3/4 汉字补充、专有名词补充、" +
                "本地候选学习、前缀推荐和现代词种子。没有 INTERNET 权限。");
        intro.setTextSize(17);
        intro.setLineSpacing(0f, 1.18f);
        box.addView(intro);

        Button enable = new Button(this);
        enable.setText("① 启用 23×2 Kana");
        enable.setOnClickListener(v -> {
            Intent i = new Intent(Settings.ACTION_INPUT_METHOD_SETTINGS);
            startActivity(i);
        });
        box.addView(enable, new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(56)));

        Button choose = new Button(this);
        choose.setText("② 选择当前输入法");
        choose.setOnClickListener(v -> {
            InputMethodManager imm = (InputMethodManager) getSystemService(INPUT_METHOD_SERVICE);
            if (imm != null) imm.showInputMethodPicker();
        });
        LinearLayout.LayoutParams cp = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, dp(56));
        cp.topMargin = dp(12);
        box.addView(choose, cp);

        TextView note = new TextView(this);
        note.setText(
                "\n测试重点：\n" +
                "• 23×2 直接假名是否比罗马字更顺手\n" +
                "• 候选词是否够用，尤其表外字/非常见读音/专名\n" +
                "• X、C 误触后的可逆切换是否舒服\n" +
                "• Emoji、剪贴板、数字盘、符号页是否足够当日常主力输入法\n\n" +
                "注意：0.1 的句子转换是轻量本地引擎，并不是 Mozc/Gboard 等级的完整形态素模型。");
        note.setTextSize(15);
        note.setGravity(Gravity.START);
        box.addView(note);

        setContentView(scroll);
    }
}
