# Third-party notices

CursorPocket distributes an unmodified FFmpeg executable beside the application for local screen, microphone, and webcam recording, plus an unmodified person-segmentation model used for on-device camera background effects. Nothing is uploaded: both run locally.

## FFmpeg

- Project: FFmpeg
- Source: https://ffmpeg.org/
- Windows build provider: https://github.com/BtbN/FFmpeg-Builds
- Pinned release: `autobuild-2026-08-31-13-27`
- Artifact: `ffmpeg-n8.1.2-50-g1a748fe2cd-win64-lgpl-8.1.zip`
- Archive SHA-256: `F6274BBD9C247F9E90C1BBED066B03ED4A3907CECE2FB91BE6DD352393936365`
- Executable SHA-256: `9C60DA6C0B083110D59084EA39F60AE149AA3E031C3B4BB4F573FAFA1C1E7CEA`
- License: GNU Lesser General Public License (LGPL), version 2.1 or later

This build has GPL and non-free components disabled. CursorPocket invokes `ffmpeg.exe` as a separate process and does not modify it. The complete license text supplied with the build is installed as `FFMPEG-LICENSE.txt`. FFmpeg source and build scripts are available from the links above.

## MediaPipe Selfie Segmenter (ONNX)

- Project: MediaPipe Selfie Segmentation (Google)
- Upstream: https://ai.google.dev/edge/mediapipe/solutions/vision/image_segmenter
- ONNX export: https://huggingface.co/onnx-community/mediapipe_selfie_segmentation
- Artifact: `onnx/model.onnx` → distributed as `selfie_segmenter.onnx`
- SHA-256: `3241AC4AD8AA35BDAF33946776DB29F7C283A413AA0B0DACB9483594B4531AAD`
- License: Apache License 2.0

The model is loaded unmodified and run on-device by ONNX Runtime to produce the
person mask behind camera background blur and background replacement. No camera
frame, mask, or derived data leaves the machine. The complete license text is
installed as `SELFIE-SEGMENTER-LICENSE.txt`, and `tools/fetch_models.ps1` pins
the hash above.

## Native dependencies

The native application also distributes these NuGet dependencies:

- Microsoft Windows App SDK 2.4.0 — Microsoft Software License Terms; https://github.com/microsoft/WindowsAppSDK
- CommunityToolkit.Mvvm 8.4.2 — MIT; https://github.com/CommunityToolkit/dotnet
- NAudio 2.2.1 — MIT; https://github.com/naudio/NAudio
- System.Drawing.Common 8.0.20 — MIT; https://github.com/dotnet/winforms
- Microsoft.ML.OnnxRuntime 1.29.0 — MIT; https://github.com/microsoft/onnxruntime

Their complete package license and third-party notice files are included under the distributable's `licenses` directory.
