# White Christmas - Local Object Detection in Quest with Gemini API for Image understanding

https://github.com/user-attachments/assets/7d485458-e76c-4ea2-a6ae-177849aaf4bd

## Overview

White Christmas is an open source project that enables object detection and tracking inside Quest headsets, enhanced with Gemini API for real-time image understanding. This project builds upon the [QuestDisplayAccessDemo by trev3d](https://github.com/trev3d/QuestDisplayAccessDemo) to provide developers with advanced object recognition capabilities.

Since Meta's SDK does not currently allow direct access to the passthrough feed, we leverage Android's MediaProjector API as a workaround to capture the display image in near real-time. This solution runs natively within the headset using Google's MLKit on the Android runtime, with no PC, embedded browser, or dev mode required.

## Features

- Display capture from Quest headset using Android MediaProjector API
- Real-time object detection and tracking using Google's MLKit
- Integration with Gemini API for advanced image understanding
- Shoe detection capabilities with a database of known footwear
- Foot measurement validation

## Setup

- Add the 'DisplayCapture' and 'DepthKit' folders to your project.

![Screenshot_1](https://github.com/user-attachments/assets/bf96301b-badf-42fb-a05f-1da018dd33e3)

- Open your player settings and set your Android Target API level to `Android 14.0 (API level 34)`

![image](https://github.com/user-attachments/assets/98791394-e4fa-433d-bac2-c23b30a090a5)

- Make sure you're using custom Main Manifest and Main Gradle Template files

![Screenshot_2](https://github.com/user-attachments/assets/31a7ff38-13dc-4f3b-9d6b-0127e2355521)

- Update your `AndroidManifest.xml` file with these lines:

```
<!--ADD THESE LINES TO YOUR MANIFEST <MANIFEST> SECTION!!!-->
<uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
<uses-permission android:name="android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION" />
<uses-permission android:name="android.permission.INTERNET" />
<!--ADD THESE LINES TO YOUR MANIFEST <MANIFEST> SECTION!!!-->
```

```
<!--ADD THESE LINES TO YOUR MANIFEST <APPLICATION> SECTION!!!-->
<activity android:name="com.trev3d.DisplayCapture.DisplayCaptureRequestActivity" android:exported="false" />
<service android:name="com.trev3d.DisplayCapture.DisplayCaptureNotificationService" android:exported="false" android:foregroundServiceType="mediaProjection" />
<!--ADD THESE LINES TO YOUR MANIFEST <APPLICATION> SECTION!!!-->
```

- Update your `mainTemplate.gradle` file with these lines:

```
/* ADD THESE LINES TO YOUR GRADLE DEPENDENCIES SECTION */
implementation 'androidx.appcompat:appcompat:1.6.1'
implementation 'com.google.mlkit:barcode-scanning:17.3.0'
implementation 'com.google.mlkit:object-detection:17.0.0'
implementation 'com.google.code.gson:gson:2.11.0'
implementation 'com.google.ai.client.generativeai:generativeai:0.2.0'
/* ADD THESE LINES TO YOUR GRADLE DEPENDENCIES SECTION */
```

- Set up your Gemini API key in your Firebase environment

## Object Detection & Gemini API Integration

This project extends the original display access demo with:

1. **Object Detection**: The system can identify and track objects in the Quest's view using Google's MLKit
2. **Shoe Detection**: Specialized detection for footwear with matching against a database
3. **Gemini AI Integration**: Uses Google's Gemini API to analyze and understand images in real-time
4. **Firebase Integration**: Functions to process and store detection results

## ⚠️ Limitations and Known Issues

### Technical Limitations

- This is a workaround, not true camera/passthrough access
- Display capture has several frames of latency
- Virtual elements will obscure physical objects in the image
- Only works on-headset (not through QuestLink)

### Hardware Requirements

- QR code tracking will only work on Quest 3/3S (due to depth estimation features)
- You may need Quest system software v68 or higher

### Performance Considerations

- Display capture and object detection are computationally expensive
- Multiple ML models running simultaneously may impact performance

## Credits

This project builds upon:

- [QuestDisplayAccessDemo by trev3d](https://github.com/trev3d/QuestDisplayAccessDemo)
- [@t-34400's QuestMediaProjection repo](https://github.com/t-34400/QuestMediaProjection)
- [@Gustorvo](https://github.com/Gustorvo)'s texture pointer optimization

## Technical Reference

- Captured view is ~82 degrees in horizontal and vertical FOV on Quest 3
- Capture texture is 1024x1024
- MediaProjection captures frames from the left eye buffer

## Additional Resources

- [Meta Documentation on Media Projection](https://developer.oculus.com/documentation/native/native-media-projection/)
- [Android Media Projection API](https://developer.android.com/media/grow/media-projection)
- [Google MLKit Documentation](https://developers.google.com/ml-kit)
- [Gemini API Documentation](https://ai.google.dev/docs)
