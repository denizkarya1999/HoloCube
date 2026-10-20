package com.developer27.holocube.transmitter;

import static androidx.test.espresso.Espresso.onView;
import static androidx.test.espresso.Espresso.pressBack;
import static androidx.test.espresso.action.ViewActions.click;
import static androidx.test.espresso.assertion.ViewAssertions.matches;
import static androidx.test.espresso.matcher.ViewMatchers.isDisplayed;
import static androidx.test.espresso.matcher.ViewMatchers.withId;
import static org.hamcrest.Matchers.not;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import android.graphics.RectF;
import android.os.Build;
import android.os.SystemClock;
import android.view.WindowInsets;
import android.view.WindowManager;
import android.widget.ImageView;

import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;

import org.junit.Test;
import org.junit.runner.RunWith;

@RunWith(AndroidJUnit4.class)
public final class TransmitterTest {
    @Test
    public void launchShowsWholeImageInLandscapeAndStaysAwakeAfterRecreation() {
        try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
            assertImageWindow(scenario);
            scenario.recreate();
            assertImageWindow(scenario);
        }
    }

    private void assertImageWindow(ActivityScenario<MainActivity> scenario) {
        onView(withId(R.id.transmitter_image)).check(matches(isDisplayed()));
        onView(withId(R.id.about_button)).check(matches(not(isDisplayed())));
        scenario.onActivity(activity -> {
            assertTrue((activity.getWindow().getAttributes().flags
                    & WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) != 0);
            ImageView image = activity.findViewById(R.id.transmitter_image);
            assertTrue(image.getWidth() > image.getHeight());
            assertEquals(1920, image.getDrawable().getIntrinsicWidth());
            assertEquals(1080, image.getDrawable().getIntrinsicHeight());
            RectF bounds = new RectF(0, 0, 1920, 1080);
            image.getImageMatrix().mapRect(bounds);
            assertTrue(bounds.left >= -1 && bounds.top >= -1);
            assertTrue(bounds.right <= image.getWidth() + 1);
            assertTrue(bounds.bottom <= image.getHeight() + 1);
            assertEquals(16f / 9f, bounds.width() / bounds.height(), 0.001f);
            assertEquals(activity.findViewById(R.id.root).getWidth() / 2f,
                    image.getLeft() + bounds.centerX(), 1f);
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                assertFalse(image.getRootWindowInsets().isVisible(WindowInsets.Type.statusBars()));
                assertFalse(image.getRootWindowInsets().isVisible(WindowInsets.Type.navigationBars()));
            }
        });
    }

    @Test
    public void aboutUsOpensAndBothCloseAndBackRestoreImageOnly() {
        try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
            onView(withId(R.id.transmitter_image)).perform(click());
            onView(withId(R.id.about_button)).perform(click());
            onView(withId(R.id.project_details)).check(matches(isDisplayed()));
            onView(withId(R.id.close_about)).perform(click());
            assertImageWindow(scenario);
            onView(withId(R.id.transmitter_image)).perform(click());
            onView(withId(R.id.about_button)).perform(click());
            pressBack();
            assertImageWindow(scenario);
        }
    }

    @Test
    public void controlsAutoHideAndCanBeReopened() {
        try (ActivityScenario<MainActivity> scenario = ActivityScenario.launch(MainActivity.class)) {
            onView(withId(R.id.transmitter_image)).perform(click());
            onView(withId(R.id.about_button)).check(matches(isDisplayed()));
            SystemClock.sleep(4500);
            onView(withId(R.id.about_button)).check(matches(not(isDisplayed())));
            onView(withId(R.id.transmitter_image)).perform(click());
            onView(withId(R.id.about_button)).check(matches(isDisplayed()));
            onView(withId(R.id.transmitter_image)).perform(click());
            assertImageWindow(scenario);
        }
    }
}
