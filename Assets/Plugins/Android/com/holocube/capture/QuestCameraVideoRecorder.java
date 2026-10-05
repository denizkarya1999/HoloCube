package com.holocube.capture;

import android.Manifest;
import android.app.Activity;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.content.pm.PackageManager;
import android.media.AudioFormat;
import android.media.AudioManager;
import android.media.AudioRecord;
import android.media.AudioRecordingConfiguration;
import android.media.AudioTimestamp;
import android.media.Image;
import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.media.MediaRecorder;
import android.net.Uri;
import android.os.Environment;
import android.os.ParcelFileDescriptor;
import android.provider.MediaStore;
import android.util.Log;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.text.SimpleDateFormat;
import java.util.ArrayDeque;
import java.util.Date;
import java.util.Locale;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.TimeUnit;

/**
 * Records the Quest camera and headset microphone to an H.264/AAC MP4 in Movies/HoloCube.
 * Frames are supplied as bottom-first RGBA bytes and converted to flexible YUV 4:2:0
 * on a worker thread so encoding does not block Unity's render/update loop.
 */
public final class QuestCameraVideoRecorder {
    private static final String TAG = "HoloCubeVideoRecorder";
    private static final int QUEUE_CAPACITY = 3;
    // Leave cleanup time within Unity's 4.5-second lifecycle wait.
    private static final long END_OF_STREAM_TIMEOUT_NS = TimeUnit.SECONDS.toNanos(4);
    private static final long STARTUP_TIMEOUT_NS = TimeUnit.SECONDS.toNanos(5);
    private static final int AUDIO_SAMPLE_RATE = 48000;
    private static final int AUDIO_BYTES_PER_FRAME = 2; // Mono PCM16.
    private static final int AUDIO_READ_BYTES = 4096;
    private static final int AUDIO_BUFFER_BYTES = AUDIO_SAMPLE_RATE * AUDIO_BYTES_PER_FRAME;
    private static final int MAX_PENDING_SAMPLE_BYTES = 2 * 1024 * 1024;

    private volatile String status = "IDLE";
    private volatile boolean stopRequested;
    private volatile ArrayBlockingQueue<Frame> frameQueue;
    private volatile Thread encoderThread;
    private volatile long stopTimeNs;
    private volatile Throwable microphoneFailure;
    private final Object microphoneLock = new Object();
    private final ArrayDeque<PcmFrame> audioQueue = new ArrayDeque<>();
    private final ArrayDeque<EncodedSample> pendingSamples = new ArrayDeque<>();

    private Context context;
    private Uri mediaUri;
    private String displayName;
    private int width;
    private int height;
    private long startTimeNs;
    private volatile long lastPresentationTimeUs;
    private MediaCodec encoder;
    private MediaCodec audioEncoder;
    private AudioRecord microphone;
    private AudioManager audioManager;
    private ByteBuffer microphoneBuffer;
    private final AudioTimestamp audioTimestamp = new AudioTimestamp();
    private long audioStartTimeNs = -1;
    private long audioFramesCaptured;
    private long nextMicrophoneCheckNs;
    private long lastAudioInputEndTimeUs;
    private long lastWrittenVideoTimeUs = -1;
    private long lastWrittenAudioTimeUs = -1;
    private int pendingAudioBytes;
    private int pendingSampleBytes;
    private PcmFrame currentAudioFrame;
    private MediaMuxer muxer;
    private ParcelFileDescriptor outputFileDescriptor;
    private boolean encoderStarted;
    private boolean audioEncoderStarted;
    private boolean muxerStarted;
    private int videoTrack = -1;
    private int audioTrack = -1;
    private int encodedSamples;
    private int encodedAudioSamples;

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
        this.encodedAudioSamples = 0;
        this.lastPresentationTimeUs = 0;
        this.videoTrack = -1;
        this.audioTrack = -1;
        this.encoderStarted = false;
        this.audioEncoderStarted = false;
        this.muxerStarted = false;
        this.stopRequested = false;
        this.stopTimeNs = 0;
        this.microphoneFailure = null;
        this.audioStartTimeNs = -1;
        this.audioFramesCaptured = 0;
        this.nextMicrophoneCheckNs = 0;
        this.lastAudioInputEndTimeUs = 0;
        this.lastWrittenVideoTimeUs = -1;
        this.lastWrittenAudioTimeUs = -1;
        this.pendingAudioBytes = 0;
        this.pendingSampleBytes = 0;
        this.currentAudioFrame = null;
        this.audioQueue.clear();
        this.pendingSamples.clear();
        this.frameQueue = new ArrayBlockingQueue<>(QUEUE_CAPACITY);

        try {
            if (context.checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED)
                throw new SecurityException("Microphone permission is required to record video with voice.");
            configureMicrophone();
            configureAudioEncoder();
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
            try {
                microphone.startRecording();
                if (microphone.getRecordingState() != AudioRecord.RECORDSTATE_RECORDING)
                    throw new IOException("Microphone did not start recording.");
            } catch (Exception error) {
                throw new IOException("Microphone could not start: " + shortMessage(error), error);
            }
            status = "RECORDING";
            encoderThread = new Thread(this::encodeFrames, "HoloCubeVideoEncoder");
            encoderThread.start();
            Log.i(TAG, "HOLOCUBE_VIDEO_STARTED: Movies/HoloCube/" + displayName + " (microphone AAC enabled)");
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
        if (!"RECORDING".equals(status) || stopTimeNs != 0 || rgba == null) return;
        long presentationTimeUs = Math.max(1L, (System.nanoTime() - startTimeNs) / 1000L);
        long previous = lastPresentationTimeUs;
        if (presentationTimeUs <= previous) presentationTimeUs = previous + 1L;
        lastPresentationTimeUs = presentationTimeUs;

        ArrayBlockingQueue<Frame> queue = frameQueue;
        if (queue != null) queue.offer(new Frame(rgba, presentationTimeUs));
    }

    public synchronized void stopRecording() {
        if (!"RECORDING".equals(status)) return;
        stopTimeNs = System.nanoTime();
        status = "SAVING";
        // Freeze the microphone now, before queued video frames are encoded. Only
        // PCM captured at or before this release/focus-loss cutoff is retained.
        synchronized (microphoneLock) {
            try {
                captureMicrophoneLocked(true);
            } catch (Exception error) {
                microphoneFailure = error;
            } finally {
                stopMicrophoneLocked();
            }
        }
        stopRequested = true;
        Log.i(TAG, "HOLOCUBE_VIDEO_SAVING: " + displayName);
    }

    public void stopRecordingAndWait(int timeoutMillis) {
        stopRecording();
        Thread thread = encoderThread;
        if (thread == null || thread == Thread.currentThread() || timeoutMillis <= 0) return;
        try {
            thread.join(timeoutMillis);
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
            while (!stopRequested || (queue != null && !queue.isEmpty()) || hasPendingAudio()) {
                if (microphoneFailure != null)
                    throw new IOException(shortMessage(microphoneFailure), microphoneFailure);
                synchronized (microphoneLock) {
                    if (stopTimeNs == 0) captureMicrophoneLocked(false);
                }
                encodeAvailableAudio();
                Frame frame = queue == null ? null : queue.poll(stopRequested ? 0 : 5, TimeUnit.MILLISECONDS);
                if (frame != null) encodeFrame(frame);
                drainOutput(encoder, false);
                drainOutput(audioEncoder, true);
                if (!muxerStarted && System.nanoTime() - startTimeNs > STARTUP_TIMEOUT_NS)
                    throw new IOException("The audio and video encoders did not become ready.");
                if (stopRequested && System.nanoTime() - stopTimeNs > END_OF_STREAM_TIMEOUT_NS)
                    throw new IOException("The audio and video encoders could not finish saving.");
            }
            if (microphoneFailure != null)
                throw new IOException(shortMessage(microphoneFailure), microphoneFailure);
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
            if (encodedAudioSamples < 1)
                throw new IOException(audioFramesCaptured < 1
                    ? "Microphone did not capture any audio. Hold B a little longer."
                    : "The audio encoder did not produce any samples.");

            if (encoderStarted) {
                encoder.stop();
                encoderStarted = false;
            }
            if (audioEncoderStarted) {
                audioEncoder.stop();
                audioEncoderStarted = false;
            }
            if (muxerStarted) {
                muxer.stop();
                muxerStarted = false;
            }
            releaseResources();
            if (microphoneFailure != null)
                throw new IOException(shortMessage(microphoneFailure), microphoneFailure);

            ContentValues values = new ContentValues();
            values.put(MediaStore.MediaColumns.IS_PENDING, 0);
            int updated = context.getContentResolver().update(mediaUri, values, null, null);
            if (updated < 1) throw new IOException("Could not publish the saved video.");
            published = true;
            setTerminalStatus("SAVED:" + displayName);
            Log.i(TAG, "HOLOCUBE_VIDEO_SAVED: Movies/HoloCube/" + displayName
                + " (" + encodedSamples + " video frames, " + encodedAudioSamples + " AAC samples)");
            mediaUri = null;
        } catch (Throwable error) {
            Log.e(TAG, "Could not finish video recording", error);
            releaseResources();
            setTerminalStatus("ERROR:" + shortMessage(error));
        } finally {
            frameQueue = null;
            if (!published) deletePendingItem();
        }
    }

    private synchronized void setTerminalStatus(String value) {
        status = value;
    }

    private void configureMicrophone() throws IOException {
        try {
            audioManager = (AudioManager) context.getSystemService(Context.AUDIO_SERVICE);
            if (audioManager != null && audioManager.isMicrophoneMute())
                throw new IOException("Microphone is muted. Unmute it before recording.");
            int minimumBuffer = AudioRecord.getMinBufferSize(AUDIO_SAMPLE_RATE,
                AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
            if (minimumBuffer <= 0) throw new IOException("Microphone audio format is unavailable.");
            microphone = new AudioRecord.Builder()
                .setAudioSource(MediaRecorder.AudioSource.MIC)
                .setAudioFormat(new AudioFormat.Builder().setSampleRate(AUDIO_SAMPLE_RATE)
                    .setChannelMask(AudioFormat.CHANNEL_IN_MONO).setEncoding(AudioFormat.ENCODING_PCM_16BIT).build())
                .setBufferSizeInBytes(Math.max(AUDIO_BUFFER_BYTES, minimumBuffer * 4))
                .setPrivacySensitive(true)
                .build();
            if (microphone.getState() != AudioRecord.STATE_INITIALIZED)
                throw new IOException("Microphone is unavailable.");
            microphoneBuffer = ByteBuffer.allocateDirect(AUDIO_READ_BYTES);
        } catch (Exception error) {
            throw new IOException("Microphone could not be prepared: " + shortMessage(error), error);
        }
    }

    private void configureAudioEncoder() throws IOException {
        try {
            MediaFormat format = MediaFormat.createAudioFormat(MediaFormat.MIMETYPE_AUDIO_AAC, AUDIO_SAMPLE_RATE, 1);
            format.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC);
            format.setInteger(MediaFormat.KEY_BIT_RATE, 96000);
            format.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, AUDIO_READ_BYTES);
            audioEncoder = MediaCodec.createEncoderByType(MediaFormat.MIMETYPE_AUDIO_AAC);
            audioEncoder.configure(format, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
            audioEncoder.start();
            audioEncoderStarted = true;
        } catch (Exception error) {
            throw new IOException("The audio encoder could not start: " + shortMessage(error), error);
        }
    }

    // Called only with microphoneLock held; AudioRecord reads are never blocking.
    private void captureMicrophoneLocked(boolean finishing) throws IOException {
        try {
            readMicrophoneLocked(finishing);
        } catch (IOException error) {
            throw error;
        } catch (Exception error) {
            throw new IOException("Microphone capture failed: " + shortMessage(error), error);
        }
    }

    private void readMicrophoneLocked(boolean finishing) throws IOException {
        if (microphone == null || microphone.getRecordingState() != AudioRecord.RECORDSTATE_RECORDING) {
            if (finishing && audioFramesCaptured > 0) return;
            throw new IOException("Microphone stopped unexpectedly.");
        }
        long now = System.nanoTime();
        // Focus loss may silence the microphone as Unity asks us to stop. Keep
        // the voice already captured instead of treating this normal stop as failure.
        if (!finishing && now >= nextMicrophoneCheckNs) {
            nextMicrophoneCheckNs = now + TimeUnit.MILLISECONDS.toNanos(250);
            AudioRecordingConfiguration configuration = microphone.getActiveRecordingConfiguration();
            if ((audioManager != null && audioManager.isMicrophoneMute())
                || (configuration != null && configuration.isClientSilenced()))
                throw new IOException("Microphone is muted or in use by another app.");
        }
        int maximumReads = finishing ? AUDIO_BUFFER_BYTES / AUDIO_READ_BYTES + 2 : 8;
        for (int i = 0; i < maximumReads; i++) {
            microphoneBuffer.clear();
            int bytesRead = microphone.read(microphoneBuffer, AUDIO_READ_BYTES, AudioRecord.READ_NON_BLOCKING);
            if (bytesRead < 0) {
                if (finishing && audioFramesCaptured > 0) {
                    Log.w(TAG, "Microphone tail was unavailable during stop (" + bytesRead + "); saving captured voice.");
                    return;
                }
                throw new IOException("Microphone read failed (" + bytesRead + ").");
            }
            if (bytesRead == 0) return;
            if (audioStartTimeNs < 0) {
                if (microphone.getTimestamp(audioTimestamp, AudioTimestamp.TIMEBASE_MONOTONIC) == AudioRecord.SUCCESS)
                    audioStartTimeNs = audioTimestamp.nanoTime
                        - audioTimestamp.framePosition * 1000000000L / AUDIO_SAMPLE_RATE;
                else
                    // Use the start-call clock if hardware timing is not ready;
                    // anchoring to read time would shift delayed/backlogged PCM.
                    audioStartTimeNs = startTimeNs;
                audioStartTimeNs = Math.max(startTimeNs, audioStartTimeNs);
            }
            long firstFrame = audioFramesCaptured;
            int frameCount = bytesRead / AUDIO_BYTES_PER_FRAME;
            audioFramesCaptured += frameCount;
            long cutoff = stopTimeNs;
            if (cutoff != 0) {
                long allowedFrames = Math.max(0L, (cutoff - audioStartTimeNs) * AUDIO_SAMPLE_RATE / 1000000000L);
                frameCount = (int) Math.min(frameCount, Math.max(0L, allowedFrames - firstFrame));
            }
            int keptBytes = frameCount * AUDIO_BYTES_PER_FRAME;
            if (keptBytes > 0) {
                if (pendingAudioBytes + keptBytes > AUDIO_BUFFER_BYTES)
                    throw new IOException("Audio encoding could not keep up with recording.");
                byte[] pcm = new byte[keptBytes];
                microphoneBuffer.position(0);
                microphoneBuffer.get(pcm);
                long timeUs = (audioStartTimeNs - startTimeNs) / 1000L
                    + firstFrame * 1000000L / AUDIO_SAMPLE_RATE;
                audioQueue.add(new PcmFrame(pcm, timeUs));
                pendingAudioBytes += keptBytes;
            }
            if (keptBytes < bytesRead) return;
        }
    }

    private boolean hasPendingAudio() {
        synchronized (microphoneLock) {
            return currentAudioFrame != null || !audioQueue.isEmpty();
        }
    }

    private void encodeAvailableAudio() throws Exception {
        for (int i = 0; i < 16; i++) {
            if (currentAudioFrame == null) {
                synchronized (microphoneLock) {
                    currentAudioFrame = audioQueue.poll();
                    if (currentAudioFrame != null) pendingAudioBytes -= currentAudioFrame.pcm.length;
                }
                if (currentAudioFrame == null) return;
            }
            drainOutput(audioEncoder, true);
            int inputIndex = audioEncoder.dequeueInputBuffer(0);
            if (inputIndex < 0) return;
            ByteBuffer input = audioEncoder.getInputBuffer(inputIndex);
            if (input == null) throw new IOException("The audio encoder did not provide an input buffer.");
            input.clear();
            int count = Math.min(input.remaining(), currentAudioFrame.pcm.length - currentAudioFrame.offset);
            count -= count % AUDIO_BYTES_PER_FRAME;
            if (count == 0) throw new IOException("The audio encoder input buffer is too small.");
            input.put(currentAudioFrame.pcm, currentAudioFrame.offset, count);
            long timeUs = currentAudioFrame.timeUs
                + (currentAudioFrame.offset / AUDIO_BYTES_PER_FRAME) * 1000000L / AUDIO_SAMPLE_RATE;
            audioEncoder.queueInputBuffer(inputIndex, 0, count, timeUs, 0);
            lastAudioInputEndTimeUs = timeUs + (count / AUDIO_BYTES_PER_FRAME) * 1000000L / AUDIO_SAMPLE_RATE;
            currentAudioFrame.offset += count;
            if (currentAudioFrame.offset == currentAudioFrame.pcm.length) currentAudioFrame = null;
        }
    }

    private void stopMicrophoneLocked() {
        if (microphone == null) return;
        try {
            if (microphone.getRecordingState() == AudioRecord.RECORDSTATE_RECORDING) microphone.stop();
        } catch (Exception error) {
            microphoneFailure = new IOException("Microphone could not stop cleanly.", error);
            Log.w(TAG, "Microphone could not stop cleanly", error);
        }
    }

    private void encodeFrame(Frame frame) throws Exception {
        if (frame.rgba.length < width * height * 4)
            throw new IOException("The camera frame has an unexpected size.");

        // Release pending output before requesting input: a full output queue can
        // prevent the codec from returning another input buffer.
        drainOutput(encoder, false);
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
        drainOutput(encoder, false);
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
        long deadline = stopTimeNs + END_OF_STREAM_TIMEOUT_NS;
        boolean videoInputEnded = false;
        boolean audioInputEnded = false;
        boolean videoOutputEnded = false;
        boolean audioOutputEnded = false;
        while ((!videoOutputEnded || !audioOutputEnded) && System.nanoTime() < deadline) {
            if (!videoOutputEnded) videoOutputEnded = drainOutput(encoder, false);
            if (!audioOutputEnded) audioOutputEnded = drainOutput(audioEncoder, true);
            if (!videoInputEnded) {
                int index = encoder.dequeueInputBuffer(0);
                if (index >= 0) {
                    long endTimeUs = Math.max(lastPresentationTimeUs + 1L, (stopTimeNs - startTimeNs) / 1000L);
                    encoder.queueInputBuffer(index, 0, 0, endTimeUs, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                    videoInputEnded = true;
                }
            }
            if (!audioInputEnded) {
                int index = audioEncoder.dequeueInputBuffer(0);
                if (index >= 0) {
                    audioEncoder.queueInputBuffer(index, 0, 0, lastAudioInputEndTimeUs,
                        MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                    audioInputEnded = true;
                }
            }
            if (!videoOutputEnded || !audioOutputEnded) Thread.sleep(2);
        }
        if (!videoOutputEnded || !audioOutputEnded)
            throw new IOException("The audio and video encoders did not finish saving the MP4.");
    }

    private boolean drainOutput(MediaCodec codec, boolean audio) throws Exception {
        MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
        while (true) {
            int outputIndex = codec.dequeueOutputBuffer(info, 0);
            if (outputIndex == MediaCodec.INFO_TRY_AGAIN_LATER) return false;

            if (outputIndex == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                if ((audio ? audioTrack : videoTrack) >= 0 || muxerStarted)
                    throw new IOException("The " + (audio ? "audio" : "video") + " encoder changed its format twice.");
                int track = muxer.addTrack(codec.getOutputFormat());
                if (audio) audioTrack = track;
                else videoTrack = track;
                if (videoTrack >= 0 && audioTrack >= 0) {
                    muxer.start();
                    muxerStarted = true;
                    while (!pendingSamples.isEmpty()) {
                        EncodedSample sample = pendingSamples.remove();
                        writeSample(sample.audio, sample.bytes, sample.info);
                    }
                    pendingSampleBytes = 0;
                }
                continue;
            }

            if (outputIndex == MediaCodec.INFO_OUTPUT_BUFFERS_CHANGED) continue;
            if (outputIndex < 0) continue;

            boolean endOfStream = (info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
            try {
                if (info.size > 0 && (info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0) {
                    ByteBuffer output = codec.getOutputBuffer(outputIndex);
                    if (output == null) throw new IOException("The encoder returned an empty output buffer.");
                    output.position(info.offset);
                    output.limit(info.offset + info.size);
                    if (muxerStarted) writeSample(audio, output, info);
                    else {
                        // Never retain codec buffers while waiting for the other
                        // track's format: that can prevent either codec progressing.
                        if (pendingSampleBytes + info.size > MAX_PENDING_SAMPLE_BYTES)
                            throw new IOException("The audio and video tracks could not start together.");
                        ByteBuffer bytes = ByteBuffer.allocate(info.size);
                        bytes.put(output).flip();
                        MediaCodec.BufferInfo copy = new MediaCodec.BufferInfo();
                        copy.set(0, info.size, info.presentationTimeUs, info.flags);
                        pendingSamples.add(new EncodedSample(audio, bytes, copy));
                        pendingSampleBytes += info.size;
                    }
                }
            } finally {
                codec.releaseOutputBuffer(outputIndex, false);
            }

            if (endOfStream) return true;
        }
    }

    private void writeSample(boolean audio, ByteBuffer bytes, MediaCodec.BufferInfo info) {
        long previousTimeUs = audio ? lastWrittenAudioTimeUs : lastWrittenVideoTimeUs;
        MediaCodec.BufferInfo sampleInfo = new MediaCodec.BufferInfo();
        sampleInfo.set(info.offset, info.size, Math.max(previousTimeUs + 1L, info.presentationTimeUs), info.flags);
        muxer.writeSampleData(audio ? audioTrack : videoTrack, bytes, sampleInfo);
        if (audio) {
            lastWrittenAudioTimeUs = sampleInfo.presentationTimeUs;
            encodedAudioSamples++;
        } else {
            lastWrittenVideoTimeUs = sampleInfo.presentationTimeUs;
            encodedSamples++;
        }
    }

    private void releaseResources() {
        synchronized (microphoneLock) {
            stopMicrophoneLocked();
            if (microphone != null) {
                try {
                    microphone.release();
                } catch (Exception ignored) { }
                microphone = null;
            }
            audioQueue.clear();
            pendingAudioBytes = 0;
            microphoneBuffer = null;
        }
        currentAudioFrame = null;
        pendingSamples.clear();
        pendingSampleBytes = 0;
        if (audioEncoder != null) {
            if (audioEncoderStarted) {
                try {
                    audioEncoder.stop();
                } catch (Exception ignored) { }
                audioEncoderStarted = false;
            }
            try {
                audioEncoder.release();
            } catch (Exception ignored) { }
            audioEncoder = null;
        }
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

    private static final class PcmFrame {
        final byte[] pcm;
        final long timeUs;
        int offset;

        PcmFrame(byte[] pcm, long timeUs) {
            this.pcm = pcm;
            this.timeUs = timeUs;
        }
    }

    private static final class EncodedSample {
        final boolean audio;
        final ByteBuffer bytes;
        final MediaCodec.BufferInfo info;

        EncodedSample(boolean audio, ByteBuffer bytes, MediaCodec.BufferInfo info) {
            this.audio = audio;
            this.bytes = bytes;
            this.info = info;
        }
    }
}
