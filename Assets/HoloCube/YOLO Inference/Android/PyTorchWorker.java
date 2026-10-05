package com.holocube.pytorch;

import android.content.Context;
import android.util.Log;
import com.chaquo.python.PyObject;
import com.chaquo.python.Python;
import com.chaquo.python.android.AndroidPlatform;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

/** Owns one background thread. Unity only submits images and polls for results. */
public final class PyTorchWorker {
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final Future<PyObject> startup;
    private Future<float[]> pending;
    private boolean discarded;
    private boolean closed;
    private boolean reportedFrame;

    public PyTorchWorker(Context context, int threads) {
        Context app = context.getApplicationContext();
        startup = executor.submit(() -> {
            synchronized (Python.class) {
                if (!Python.isStarted()) Python.start(new AndroidPlatform(app));
            }
            PyObject runtime = Python.getInstance().getModule("holocube_runtime");
            Log.i("HoloCube", runtime.callAttr("initialize", threads).toString());
            Log.i("HoloCube", "HOLOCUBE_PT_READY: original YOLO26n checkpoint loaded on device");
            return runtime;
        });
    }

    public synchronized boolean isReady() {
        if (closed) throw new IllegalStateException("PyTorch worker is closed");
        if (!startup.isDone()) return false;
        get(startup); // Surface initialization errors to the headset UI.
        return true;
    }

    public synchronized boolean isIdle() {
        if (!isReady()) return false;
        if (pending != null && discarded && pending.isDone()) {
            Future<float[]> finished = pending;
            pending = null;
            get(finished);
        }
        return pending == null;
    }

    public synchronized void submit(byte[] rgba) {
        if (!isIdle()) throw new IllegalStateException("PyTorch is still processing a frame");
        discarded = false;
        pending = executor.submit(() -> {
            long start = System.nanoTime();
            float[] rows = get(startup).callAttr("detect_rgba", (Object) rgba).toJava(float[].class);
            if (!reportedFrame) {
                Log.i("HoloCube", "HOLOCUBE_PT_CAMERA_FRAME: " + rows.length / 6 +
                    " detection rows in " + (System.nanoTime() - start) / 1000000 + " ms");
                reportedFrame = true;
            }
            return rows;
        });
    }

    public synchronized boolean isDone() { return pending != null && pending.isDone(); }

    public synchronized float[] take() {
        if (!isDone()) throw new IllegalStateException("The detection result is not ready");
        Future<float[]> finished = pending;
        pending = null;
        return get(finished);
    }

    public synchronized void discard() { discarded = true; }

    public synchronized void close() {
        closed = true;
        discarded = true;
        executor.shutdown(); // Finish native work without blocking Unity's render thread.
    }

    private static <T> T get(Future<T> future) {
        try { return future.get(); }
        catch (InterruptedException error) {
            Thread.currentThread().interrupt();
            throw new IllegalStateException("PyTorch worker interrupted", error);
        }
        catch (Exception error) {
            Throwable cause = error.getCause() == null ? error : error.getCause();
            throw new IllegalStateException("PyTorch: " + cause.getMessage(), cause);
        }
    }
}
