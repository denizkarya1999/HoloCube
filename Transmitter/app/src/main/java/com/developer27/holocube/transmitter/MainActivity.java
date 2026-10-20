package com.developer27.holocube.transmitter;

import android.content.Intent;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.View;
import android.view.accessibility.AccessibilityManager;

public final class MainActivity extends FullscreenActivity {
    private static final long CONTROLS_TIMEOUT_MS = 4000;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private View aboutButton;
    private final Runnable hideControls = () -> aboutButton.setVisibility(View.GONE);

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);
        protectContentFromCutout(findViewById(R.id.root));
        aboutButton = findViewById(R.id.about_button);
        findViewById(R.id.transmitter_image).setOnClickListener(view -> toggleControls());
        aboutButton.setOnClickListener(view -> {
            hideControlsNow();
            startActivity(new Intent(this, AboutActivity.class));
        });
    }

    private void toggleControls() {
        handler.removeCallbacks(hideControls);
        if (aboutButton.getVisibility() == View.VISIBLE) {
            hideControlsNow();
            return;
        }
        aboutButton.setVisibility(View.VISIBLE);
        AccessibilityManager accessibility = getSystemService(AccessibilityManager.class);
        // Screen-reader users can take as long as they need to reach the button.
        if (accessibility == null || !accessibility.isTouchExplorationEnabled()) {
            handler.postDelayed(hideControls, CONTROLS_TIMEOUT_MS);
        }
    }

    private void hideControlsNow() {
        handler.removeCallbacks(hideControls);
        aboutButton.setVisibility(View.GONE);
    }

    @Override
    protected void onPause() {
        hideControlsNow();
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        handler.removeCallbacks(hideControls);
        super.onDestroy();
    }
}
