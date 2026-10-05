package com.holocube.capture;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.net.Uri;
import android.os.Environment;
import android.os.ParcelFileDescriptor;
import android.provider.MediaStore;
import android.util.Log;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.text.SimpleDateFormat;
import java.util.Date;
import java.util.Locale;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;

/**
 * Encodes the Quest passthrough camera image stream to an H.264 MP4 in Movies/HoloCube.
 * Frames are supplied as bottom-first RGBA bytes and converted to flexible YUV 4:2:0
 * on a worker thread so encoding does not block Unity's render/update loop.
 */
public final class QuestCameraVideoRecorder {
    private static final String TAG = "HoloCubeVideoRecorder";
    private static final int QUEUE_CAPACITY = 3;
    private static final long END_OF_STREAM_TIMEOUT_NS = TimeUnit.SECONDS.toNanos(5);

    private volatile String status = "IDLE";
    private volatile boolean stopRequested;
    private volatile ArrayBlockingQueue<Frame> frameQueue;
    private volatile Thread encoderThread;

    private Context context;
    private Uri mediaUri;
    private String displayName;
    private int width;
    private int height;
    private long startTimeNs;
    private volatile long lastPresentationTimeUs;
    private MediaCodec encoder;
    private MediaMuxer muxer;
    private ParcelFileDescriptor outputFileDescriptor;
    private boolean encoderStarted;
    private boolean muxerStarted;
    private int videoTrack = -1;
    private int encodedSamples;

    public synchronized String startRecording(Activity activity, int width, int height, int frameRate, int bitRate) {
        Thread previousThread = encoderThread;
        if (previousThread != null && previousThread.isAlive())
            throw new IllegalStateException("The previous video is still being saved.");

        this.context = activity.getApplicationContext();
        this.mediaUri = null;
        this.displayName = null;
        this.width = width;
        this.height = height;
        this.encodedSamples = 0;
        this.lastPresentationTimeUs = 0;
        this.videoTrack = -1;
        this.encoderStarted = false;
        this.muxerStarted = false;
        this.stopRequested = false;
        this.frameQueue = new ArrayBlockingQueue<>(QUEUE_CAPACITY);

        try {
            String timestamp = new SimpleDateFormat("yyyyMMdd_HHmmss_SSS", Locale.US).format(new Date());
            displayName = "HoloCube_" + timestamp + ".mp4";

            ContentValues values = new ContentValues();
            values.put(MediaStore.MediaColumns.DISPLAY_NAME, displayName);
            values.put(MediaStore.MediaColumns.MIME_TYPE, "video/mp4");
            values.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_MOVIES + "/HoloCube");
            values.put(MediaStore.MediaColumns.IS_PENDING, 1);
            ContentResolver resolver = context.getContentResolver();
            mediaUri = resolver.insert(MediaStore.Video.Media.EXTERNAL_CONTENT_URI, values);
            if (mediaUri == null) throw new IOException("Could not create a video in Movies/HoloCube.");

            outputFileDescriptor = resolver.openFileDescriptor(mediaUri, "w");
            if (outputFileDescriptor == null) throw new IOException("Could not open the output video.");

            MediaFormat format = MediaFormat.createVideoFormat(MediaFormat.MIMETYPE_VIDEO_AVC, width, height);
            format.setInteger(MediaFormat.KEY_COLOR_FORMAT, MediaCodecInfo.CodecCapabilities.COLOR_FormatYUV420Flexible);
            format.setInteger(MediaFormat.KEY_BIT_RATE, bitRate);
            format.setInteger(MediaFormat.KEY_FRAME_RATE, frameRate);
            format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL, 1);

            encoder = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_VIDEO_AVC);
            encoder.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            encoder.start();
            encoderStarted = true;
            muxer = new MediaMuxer(outputFileDescriptor.getFileDescriptor(), MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);

            startTimeNs = System.nanoTime();
            status = "RECORDING";
            encoderThread = new Thread(this::encodeFrames, "HoloCubeVideoEncoder");
            encoderThread.start();
            Log.i(TAG, "HOLOCUBE_VIDEO_STARTED: Movies/HoloCube/" + displayName);
            return displayName;
        } catch (Exception error) {
            Log.e(TAG, "Could not start video recording", error);
            releaseResources();
            deletePendingItem();
            status = "ERROR:" + shortMessage(error);
            throw new IllegalStateException("Could not start video recording: " + shortMessage(error), error);
        }
    }

    public void enqueueFrame(byte[] rgba) {
        if (!"RECORDING".equals(status) || rgba == null) return;
        long presentationTimeUs = Math.max(1L, (System.nanoTime() - startTimeNs) / 1000L);
        long previous = lastPresentationTimeUs;
        if (presentationTimeUs <= previous) presentationTimeUs = previous + 1L;
        lastPresentationTimeUs = presentationTimeUs;

        ArrayBlockingQueue<Frame> queue = frameQueue;
        if (queue != null) queue.offer(new Frame(rgba, presentationTimeUs));
    }

    public synchronized void stopRecording() {
        if (!"RECORDING".equals(status)) return;
        status = "SAVING";
        stopRequested = true;
        Log.i(TAG, "HOLOCUBE_VIDEO_SAVING: " + displayName);
    }

    public void stopRecordingAndWait(int timeoutMillis) {
        stopRecording();
        Thread thread = encoderThread;
        if (thread == null || thread == Thread.currentThread()) return;
        try {
            thread.join(Math.max(0, timeoutMillis));
        } catch (InterruptedException error) {
            Thread.currentThread().interrupt();
        }
    }

    public String getStatus() {
        return status;
    }

    public void dispose() {
        stopRecordingAndWait(4500);
    }

    private void encodeFrames() {
        Throwable failure = null;
        try {
            ArrayBlockingQueue<Frame> queue = frameQueue;
            while (!stopRequested || (queue != null && !queue.isEmpty())) {
                Frame frame = queue == null ? null : queue.poll(100, TimeUnit.MILLISECONDS);
                if (frame != null) encodeFrame(frame);
            }
            sendEndOfStream();
        } catch (Throwable error) {
            failure = error;
            Log.e(TAG, "Video encoding failed", error);
        }

        boolean published = false;
        try {
            if (failure != null) throw new IOException(shortMessage(failure), failure);
            if (encodedSamples < 1 || !muxerStarted)
                throw new IOException("No camera frames were captured.");

            if (encoderStarted) {
                encoder.stop();
                encoderStarted = false;
            }
            if (muxerStarted) {
                muxer.stop();
                muxerStarted = false;
            }
            releaseResources();

            ContentValues values = new ContentValues();
            values.put(MediaStore.MediaColumns.IS_PENDING, 0);
            int updated = context.getContentResolver().update(mediaUri, values, null, null);
            if (updated < 1) throw new IOException("Could not publish the saved video.");
            published = true;
            status = "SAVED:" + displayName;
            Log.i(TAG, "HOLOCUBE_VIDEO_SAVED: Movies/HoloCube/" + displayName
                + " (" + encodedSamples + " frames)");
            mediaUri = null;
        } catch (Throwable error) {
            Log.e(TAG, "Could not finish video recording", error);
            releaseResources();
            status = "ERROR:" + shortMessage(error);
        } finally {
            frameQueue = null;
            if (!published) deletePendingItem();
        }
    }

    private void encodeFrame(Frame frame) throws Exception {
        if (frame.rgba.length < width * height * 4)
            throw new IOException("The camera frame has an unexpected size.");

        // Release pending output before requesting input: a full output queue can
        // prevent the codec from returning another input buffer.
        drainOutput(false);
        int inputIndex = encoder.dequeueInputBuffer(10000);
        if (inputIndex < 0) return; // Keep the app responsive by dropping frames if the encoder is busy.

        Image inputImage = encoder.getInputImage(inputIndex);
        if (inputImage == null)
            throw new IOException("The video encoder did not provide a YUV input image.");

        try {
            copyRgbaToYuv420(frame.rgba, inputImage);
        } finally {
            inputImage.close();
        }

        encoder.queueInputBuffer(inputIndex, 0, width * height * 3 / 2, frame.presentationTimeUs, 0);
        drainOutput(false);
    }

    private void copyRgbaToYuv420(byte[] rgba, Image image) throws IOException {
        Image.Plane[] planes = image.getPlanes();
        if (planes == null || planes.length < 3)
            throw new IOException("The video encoder returned an invalid YUV image.");

        ByteBuffer yBuffer = planes[0].getBuffer();
        ByteBuffer uBuffer = planes[1].getBuffer();
        ByteBuffer vBuffer = planes[2].getBuffer();
        int yBase = yBuffer.position();
        int uBase = uBuffer.position();
        int vBase = vBuffer.position();
        int yRowStride = planes[0].getRowStride();
        int uRowStride = planes[1].getRowStride();
        int vRowStride = planes[2].getRowStride();
        int yPixelStride = planes[0].getPixelStride();
        int uPixelStride = planes[1].getPixelStride();
        int vPixelStride = planes[2].getPixelStride();

        for (int y = 0; y < height; y++) {
            int sourceY = height - 1 - y; // Unity readback rows start at the bottom.
            for (int x = 0; x < width; x++) {
                int p = (sourceY * width + x) * 4;
                int red = rgba[p] & 0xff;
                int green = rgba[p + 1] & 0xff;
                int blue = rgba[p + 2] & 0xff;
                int luma = clamp(((66 * red + 129 * green + 25 * blue + 128) >> 8) + 16);
                yBuffer.put(yBase + y * yRowStride + x * yPixelStride, (byte) luma);
            }
        }

        for (int y = 0; y < height; y += 2) {
            int chromaY = y / 2;
            for (int x = 0; x < width; x += 2) {
                int uSum = 0;
                int vSum = 0;
                for (int dy = 0; dy < 2; dy++) {
                    int sourceRow = height - 1 - (y + dy);
                    for (int dx = 0; dx < 2; dx++) {
                        int p = (sourceRow * width + x + dx) * 4;
                        int red = rgba[p] & 0xff;
                        int green = rgba[p + 1] & 0xff;
                        int blue = rgba[p + 2] & 0xff;
                        uSum += clamp(((-38 * red - 74 * green + 112 * blue + 128) >> 8) + 128);
                        vSum += clamp(((112 * red - 94 * green - 18 * blue + 128) >> 8) + 128);
                    }
                }
                int chromaX = x / 2;
                uBuffer.put(uBase + chromaY * uRowStride + chromaX * uPixelStride, (byte) (uSum / 4));
                vBuffer.put(vBase + chromaY * vRowStride + chromaX * vPixelStride, (byte) (vSum / 4));
            }
        }
    }

    private static int clamp(int value) {
        return Math.max(0, Math.min(255, value));
    }

    private void sendEndOfStream() throws Exception {
        long deadline = System.nanoTime() + END_OF_STREAM_TIMEOUT_NS;
        int inputIndex = -1;
        while (inputIndex < 0 && System.nanoTime() < deadline) {
            drainOutput(false);
            inputIndex = encoder.dequeueInputBuffer(10000);
        }
        if (inputIndex < 0) throw new IOException("The video encoder did not finish its input.");

        long endTime = Math.max(lastPresentationTimeUs + 1L, (System.nanoTime() - startTimeNs) / 1000L);
        encoder.queueInputBuffer(inputIndex, 0, 0, endTime, MediaCodec.BUFFER_FLAG_END_OF_STREAM);

        boolean reachedEnd = false;
        while (!reachedEnd && System.nanoTime() < deadline)
            reachedEnd = drainOutput(true);
        if (!reachedEnd) throw new IOException("The video encoder did not finish saving the MP4.");
    }

    private boolean drainOutput(boolean waitForData) throws Exception {
        MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
        while (true) {
            int outputIndex = encoder.dequeueOutputBuffer(info, waitForData ? 10000 : 0);
            if (outputIndex == MediaCodec.INFO_TRY_AGAIN_LATER) return false;

            if (outputIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                if (muxerStarted) throw new IOException("The video encoder changed its format twice.");
                videoTrack = muxer.addTrack(encoder.getOutputFormat());
                muxer.start();
                muxerStarted = true;
                continue;
            }

            if (outputIndex == MediaCodec.INFO_OUTPUT_BUFFERS_CHANGED) continue;
            if (outputIndex < 0) continue;

            boolean endOfStream = (info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
            try {
                if (info.size > 0
                    && muxerStarted
                    && (info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0) {
                    ByteBuffer output = encoder.getOutputBuffer(outputIndex);
                    if (output != null) {
                        output.position(info.offset);
                        output.limit(info.offset + info.size);
                        muxer.writeSampleData(videoTrack, output, info);
                        encodedSamples++;
                    }
                }
            } finally {
                encoder.releaseOutputBuffer(outputIndex, false);
            }

            if (endOfStream) return true;
        }
    }

    private void releaseResources() {
        if (encoder != null) {
            if (encoderStarted) {
                try {
                    encoder.stop();
                } catch (Exception ignored) { }
                encoderStarted = false;
            }
            try {
                encoder.release();
            } catch (Exception ignored) { }
            encoder = null;
        }

        if (muxer != null) {
            if (muxerStarted) {
                try {
                    muxer.stop();
                } catch (Exception ignored) { }
                muxerStarted = false;
            }
            try {
                muxer.release();
            } catch (Exception ignored) { }
            muxer = null;
        }

        if (outputFileDescriptor != null) {
            try {
                outputFileDescriptor.close();
            } catch (IOException ignored) { }
            outputFileDescriptor = null;
        }
    }

    private void deletePendingItem() {
        Uri uri = mediaUri;
        mediaUri = null;
        if (uri == null || context == null) return;
        try {
            context.getContentResolver().delete(uri, null, null);
        } catch (Exception error) {
            Log.w(TAG, "Could not remove an incomplete MP4.", error);
        }
    }

    private static String shortMessage(Throwable error) {
        String message = error.getMessage();
        if (message == null || message.trim().isEmpty()) message = error.getClass().getSimpleName();
        return message.replace('\n', ' ').replace('\r', ' ');
    }

    private static final class Frame {
        final byte[] rgba;
        final long presentationTimeUs;

        Frame(byte[] rgba, long presentationTimeUs) {
            this.rgba = rgba;
            this.presentationTimeUs = presentationTimeUs;
        }
    }
}
