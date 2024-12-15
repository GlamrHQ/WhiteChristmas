package com.trev3d.DisplayCapture.ObjectDetection;

import static android.content.ContentValues.TAG;

import android.graphics.Bitmap;
import android.util.Log;

import com.google.android.gms.tasks.Task;
import com.google.gson.Gson;
import com.google.mlkit.vision.common.InputImage;
import com.google.mlkit.vision.objects.DetectedObject;
import com.google.mlkit.vision.objects.ObjectDetection;
import com.google.mlkit.vision.objects.ObjectDetector;
import com.google.mlkit.vision.objects.defaults.ObjectDetectorOptions;
import com.unity3d.player.UnityPlayer;
import com.trev3d.DisplayCapture.IDisplayCaptureReceiver;
import com.trev3d.DisplayCapture.DisplayCaptureManager;

import java.io.Serializable;
import java.nio.ByteBuffer;
import java.util.List;

public class ObjectDetector implements IDisplayCaptureReceiver {

    private static class Label implements Serializable {
        public String text;
        public float confidence;
        public int index;

        public Label(com.google.mlkit.vision.objects.DetectedObject.Label label) {
            text = label.getText();
            confidence = label.getConfidence();
            index = label.getIndex();
        }
    }

    private static class BoundingBox implements Serializable {
        public float left;
        public float top;
        public float right;
        public float bottom;

        public BoundingBox(android.graphics.Rect rect) {
            left = rect.left;
            top = rect.top;
            right = rect.right;
            bottom = rect.bottom;
        }
    }

    private static class Result implements Serializable {
        public BoundingBox boundingBox;
        public Label[] labels;
        public long timestamp;
        public int trackingId;

        public Result(DetectedObject detectedObject, long timestamp) {
            boundingBox = new BoundingBox(detectedObject.getBoundingBox());
            this.timestamp = timestamp;

            List<DetectedObject.Label> labels = detectedObject.getLabels();
            this.labels = new Label[labels.size()];
            for (int i = 0; i < labels.size(); i++)
                this.labels[i] = new Label(labels.get(i));

            trackingId = detectedObject.getTrackingId() != null ? detectedObject.getTrackingId() : -1;
        }
    }

    private static class Results implements Serializable {
        public Result[] results;

        public Results(int size) {
            results = new Result[size];
        }
    }

    public static ObjectDetector instance = null;

    private final ObjectDetector objectDetector;
    private final Gson gson;

    private boolean enabled;
    private volatile boolean detectingObjects = false;

    private UnityInterface unityInterface;

    private record UnityInterface(String gameObjectName) {
        private void Call(String functionName) {
            UnityPlayer.UnitySendMessage(gameObjectName, functionName, "");
        }

        public void OnObjectResults(String json) {
            UnityPlayer.UnitySendMessage(gameObjectName, "OnObjectResults", json);
        }
    }

    public ObjectDetector() {
        ObjectDetectorOptions options = new ObjectDetectorOptions.Builder()
                .setDetectorMode(ObjectDetectorOptions.STREAM_MODE)
                .enableMultipleObjects()
                .enableClassification()
                .build();

        objectDetector = ObjectDetection.getClient(options);
        gson = new Gson();
    }

    public static synchronized ObjectDetector getInstance() {
        if (instance == null)
            instance = new ObjectDetector();

        return instance;
    }

    public void setEnabled(boolean enabled) {
        if (this.enabled == enabled)
            return;

        this.enabled = enabled;

        if (this.enabled) {
            DisplayCaptureManager.getInstance().receivers.add(this);
        } else {
            DisplayCaptureManager.getInstance().receivers.remove(this);
        }
    }

    @Override
    public void onNewImage(ByteBuffer byteBuffer, int width, int height, long timestamp) {

        if (detectingObjects)
            return;

        detectingObjects = true;

        var bitmap = Bitmap.createBitmap(
                width,
                height,
                Bitmap.Config.ARGB_8888);

        byteBuffer.rewind();
        bitmap.copyPixelsFromBuffer(byteBuffer);

        InputImage input = InputImage.fromBitmap(bitmap, 0);

        Task<List<DetectedObject>> task = objectDetector.process(input);

        task.addOnCompleteListener(taskResult -> {

            detectingObjects = false;

            if (!taskResult.isSuccessful()) {
                Log.v(TAG, "No object found.");
                return;
            }

            List<DetectedObject> detectedObjects = taskResult.getResult();
            Results results = new Results(detectedObjects.size());

            Log.i(TAG, detectedObjects.size() + " objects found.");

            for (int i = 0; i < detectedObjects.size(); i++) {
                DetectedObject detectedObject = detectedObjects.get(i);
                Result result = new Result(detectedObject, timestamp);

                results.results[i] = result;
            }

            String resultsAsJson = gson.toJson(results);
            Log.i(TAG, "JSON: " + resultsAsJson);
            unityInterface.OnObjectResults(resultsAsJson);
        });
    }

    // called by Unity
    public void setup(String gameObjectName) {
        unityInterface = new UnityInterface(gameObjectName);
    }
}