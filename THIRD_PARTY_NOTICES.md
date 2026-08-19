# Third-party notices

CursorPocket distributes an unmodified FFmpeg executable beside the application for local screen, microphone, and webcam recording.

## FFmpeg

- Project: FFmpeg
- Source: https://ffmpeg.org/
- Windows build provider: https://github.com/BtbN/FFmpeg-Builds
- Pinned release: `autobuild-2026-08-17-13-05`
- Artifact: `ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-8.1.zip`
- Archive SHA-256: `BDA492675BDB354AC55F93B96AF2DBB35BABEF7DE264C37D4FF83E022831B19D`
- Executable SHA-256: `E8E106D6F6A4166747FBD7374FBF47FFC4D2DD883520C3558FEEAC0281A2712D`
- License: GNU Lesser General Public License (LGPL), version 2.1 or later

This build has GPL and non-free components disabled. CursorPocket invokes `ffmpeg.exe` as a separate process and does not modify it. The complete license text supplied with the build is installed as `FFMPEG-LICENSE.txt`. FFmpeg source and build scripts are available from the links above.

## Native dependencies

The native application also distributes these NuGet dependencies:

- Microsoft Windows App SDK 2.4.0 — Microsoft Software License Terms; https://github.com/microsoft/WindowsAppSDK
- CommunityToolkit.Mvvm 8.4.2 — MIT; https://github.com/CommunityToolkit/dotnet
- NAudio 2.2.1 — MIT; https://github.com/naudio/NAudio
- System.Drawing.Common 8.0.20 — MIT; https://github.com/dotnet/winforms

Their complete package license and third-party notice files are included under the distributable's `licenses` directory.
