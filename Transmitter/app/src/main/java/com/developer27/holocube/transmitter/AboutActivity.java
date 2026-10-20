package com.developer27.holocube.transmitter;

import android.os.Bundle;

public final class AboutActivity extends FullscreenActivity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_about);
        protectContentFromCutout(findViewById(R.id.root));
        findViewById(R.id.close_about).setOnClickListener(view -> finish());
    }
}
