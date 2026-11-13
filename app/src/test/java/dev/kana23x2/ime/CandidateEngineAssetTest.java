package dev.kana23x2.ime;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import android.content.Context;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.FileNotFoundException;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.zip.GZIPOutputStream;

import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.annotation.Config;

@RunWith(RobolectricTestRunner.class)
@Config(sdk = 28)
public class CandidateEngineAssetTest {
    private static final String TSV = "かな\t仮名\nがっこう\t学校\n";
    private Context context;

    @Before public void setUp() {
        context = RuntimeEnvironment.getApplication();
        context.getSharedPreferences("candidate_learning", Context.MODE_PRIVATE).edit().clear().commit();
    }

    @Test public void loadsAaptPackagedPlainTsvAndConvertsKana() {
        List<String> opened = new ArrayList<>();
        CandidateEngine engine = new CandidateEngine(context, name -> {
            opened.add(name);
            if (!name.equals("skk_compact.tsv")) throw new FileNotFoundException(name);
            return new ByteArrayInputStream(TSV.getBytes(StandardCharsets.UTF_8));
        });
        assertConverts(engine);
        assertEquals(Arrays.asList("skk_compact.tsv"), opened);
    }

    @Test public void fallsBackToCompressedSourceAssetAndConvertsKana() throws IOException {
        ByteArrayOutputStream bytes = new ByteArrayOutputStream();
        try (GZIPOutputStream gzip = new GZIPOutputStream(bytes)) {
            gzip.write(TSV.getBytes(StandardCharsets.UTF_8));
        }
        List<String> opened = new ArrayList<>();
        CandidateEngine engine = new CandidateEngine(context, name -> {
            opened.add(name);
            if (!name.equals("skk_compact.tsv.gz")) throw new FileNotFoundException(name);
            return new ByteArrayInputStream(bytes.toByteArray());
        });
        assertConverts(engine);
        assertEquals(Arrays.asList("skk_compact.tsv", "skk_compact.tsv.gz"), opened);
    }

    @Test public void missingAssetsDoNotReportReadyAndStillAllowRawKana() {
        CandidateEngine engine = new CandidateEngine(context, name -> {
            throw new FileNotFoundException(name);
        });
        engine.load();
        assertFalse(engine.isReady());
        assertEquals("かな", engine.candidates("かな", "", 1).get(0).text);
    }

    @Test public void invalidGzipIsClosedAndDoesNotReportReady() {
        boolean[] closed = {false};
        CandidateEngine engine = new CandidateEngine(context, name -> {
            if (!name.equals("skk_compact.tsv.gz")) throw new FileNotFoundException(name);
            return new ByteArrayInputStream(new byte[]{1, 2, 3}) {
                @Override public void close() throws IOException {
                    closed[0] = true;
                    super.close();
                }
            };
        });
        engine.load();
        assertFalse(engine.isReady());
        assertTrue(closed[0]);
    }

    @Test public void actualBuildDictionaryLoadsThroughAndroidAssetManager() {
        CandidateEngine engine = new CandidateEngine(context);
        engine.load();
        assertTrue("Build tools must generate and package a readable SKK dictionary", engine.isReady());
        assertTrue(engine.candidates("がっこう", "", 50).stream()
                .anyMatch(candidate -> "学校".equals(candidate.text)));
    }

    private void assertConverts(CandidateEngine engine) {
        engine.load();
        assertTrue(engine.isReady());
        assertEquals("仮名", engine.candidates("かな", "", 1).get(0).text);
        assertEquals("学校", engine.candidates("か\u3099っこう", "", 1).get(0).text);
        // Loading is idempotent, including the asset-opening path.
        engine.load();
    }
}
