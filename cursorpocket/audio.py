from __future__ import annotations

import ctypes
import sys
import tempfile
import threading
import time
import wave
from ctypes import wintypes
from dataclasses import dataclass
from pathlib import Path


WAVE_FORMAT_PCM = 1
WAVE_MAPPER = 0xFFFFFFFF
MMSYSERR_NOERROR = 0
CALLBACK_NULL = 0
WHDR_DONE = 0x00000001


class WAVEFORMATEX(ctypes.Structure):
    _fields_ = [
        ("wFormatTag", wintypes.WORD),
        ("nChannels", wintypes.WORD),
        ("nSamplesPerSec", wintypes.DWORD),
        ("nAvgBytesPerSec", wintypes.DWORD),
        ("nBlockAlign", wintypes.WORD),
        ("wBitsPerSample", wintypes.WORD),
        ("cbSize", wintypes.WORD),
    ]


class WAVEHDR(ctypes.Structure):
    pass


WAVEHDR._fields_ = [
    ("lpData", ctypes.c_void_p),
    ("dwBufferLength", wintypes.DWORD),
    ("dwBytesRecorded", wintypes.DWORD),
    ("dwUser", ctypes.c_size_t),
    ("dwFlags", wintypes.DWORD),
    ("dwLoops", wintypes.DWORD),
    ("lpNext", ctypes.POINTER(WAVEHDR)),
    ("reserved", ctypes.c_size_t),
]


@dataclass(frozen=True)
class AudioResult:
    path: Path
    duration_seconds: float
    sample_rate: int
    channels: int


class AudioRecorder:
    """Records mono PCM WAV audio through Windows' built-in winmm API."""

    def __init__(self, sample_rate: int = 16000, channels: int = 1, bits_per_sample: int = 16) -> None:
        self.sample_rate = sample_rate
        self.channels = channels
        self.bits_per_sample = bits_per_sample
        self._thread: threading.Thread | None = None
        self._stop = threading.Event()
        self._ready = threading.Event()
        self._error: Exception | None = None
        self._result: AudioResult | None = None
        self._temp_path: Path | None = None

    @property
    def is_recording(self) -> bool:
        return bool(self._thread and self._thread.is_alive() and self._ready.is_set() and not self._error)

    @staticmethod
    def device_count() -> int:
        if sys.platform != "win32":
            return 0
        return int(ctypes.windll.winmm.waveInGetNumDevs())

    def start(self) -> None:
        if sys.platform != "win32":
            raise RuntimeError("Audio recording is available on Windows only.")
        if self._thread and self._thread.is_alive():
            raise RuntimeError("Audio recording is already in progress.")
        if self.device_count() < 1:
            raise RuntimeError("Windows did not report an available microphone.")
        temporary = tempfile.NamedTemporaryFile(prefix="cursorpocket-audio-", suffix=".wav", delete=False)
        temporary.close()
        self._temp_path = Path(temporary.name)
        self._stop.clear()
        self._ready.clear()
        self._error = None
        self._result = None
        self._thread = threading.Thread(target=self._record, name="CursorPocketAudio", daemon=True)
        self._thread.start()
        if not self._ready.wait(timeout=3.0):
            self._stop.set()
            raise RuntimeError("The microphone did not start in time.")
        if self._error:
            error = self._error
            self._cleanup_temp()
            raise error

    def stop(self) -> AudioResult:
        if not self._thread:
            raise RuntimeError("No audio recording is in progress.")
        self._stop.set()
        self._thread.join(timeout=5.0)
        if self._thread.is_alive():
            raise RuntimeError("The microphone did not stop in time.")
        self._thread = None
        if self._error:
            error = self._error
            self._cleanup_temp()
            raise error
        if not self._result:
            self._cleanup_temp()
            raise RuntimeError("The microphone returned no audio.")
        return self._result

    def cancel(self) -> None:
        if self._thread:
            self._stop.set()
            self._thread.join(timeout=5.0)
            self._thread = None
        self._cleanup_temp()

    def _record(self) -> None:
        assert self._temp_path is not None
        winmm = ctypes.windll.winmm
        winmm.waveInOpen.argtypes = [
            ctypes.POINTER(ctypes.c_void_p),
            wintypes.UINT,
            ctypes.POINTER(WAVEFORMATEX),
            ctypes.c_size_t,
            ctypes.c_size_t,
            wintypes.DWORD,
        ]
        winmm.waveInOpen.restype = wintypes.UINT
        for function_name in ("waveInPrepareHeader", "waveInAddBuffer", "waveInUnprepareHeader"):
            function = getattr(winmm, function_name)
            function.argtypes = [ctypes.c_void_p, ctypes.POINTER(WAVEHDR), wintypes.UINT]
            function.restype = wintypes.UINT
        for function_name in ("waveInStart", "waveInStop", "waveInReset", "waveInClose"):
            function = getattr(winmm, function_name)
            function.argtypes = [ctypes.c_void_p]
            function.restype = wintypes.UINT
        handle = ctypes.c_void_p()
        block_align = self.channels * self.bits_per_sample // 8
        bytes_per_second = self.sample_rate * block_align
        audio_format = WAVEFORMATEX(
            WAVE_FORMAT_PCM,
            self.channels,
            self.sample_rate,
            bytes_per_second,
            block_align,
            self.bits_per_sample,
            0,
        )
        header_size = ctypes.sizeof(WAVEHDR)
        buffer_size = max(block_align, bytes_per_second // 10)
        buffers = [ctypes.create_string_buffer(buffer_size) for _ in range(5)]
        headers = [WAVEHDR() for _ in buffers]
        opened = False
        prepared: list[WAVEHDR] = []
        total_bytes = 0
        wav_file: wave.Wave_write | None = None

        try:
            result = winmm.waveInOpen(
                ctypes.byref(handle),
                ctypes.c_uint(WAVE_MAPPER),
                ctypes.byref(audio_format),
                0,
                0,
                CALLBACK_NULL,
            )
            self._check(result, "open the microphone")
            opened = True
            wav_file = wave.open(str(self._temp_path), "wb")
            wav_file.setnchannels(self.channels)
            wav_file.setsampwidth(self.bits_per_sample // 8)
            wav_file.setframerate(self.sample_rate)

            for buffer, header in zip(buffers, headers):
                header.lpData = ctypes.addressof(buffer)
                header.dwBufferLength = buffer_size
                header.dwBytesRecorded = 0
                header.dwFlags = 0
                self._check(
                    winmm.waveInPrepareHeader(handle, ctypes.byref(header), header_size),
                    "prepare a microphone buffer",
                )
                prepared.append(header)
                self._check(
                    winmm.waveInAddBuffer(handle, ctypes.byref(header), header_size),
                    "queue a microphone buffer",
                )

            self._check(winmm.waveInStart(handle), "start recording")
            self._ready.set()

            while not self._stop.is_set():
                for header in headers:
                    if header.dwFlags & WHDR_DONE:
                        if header.dwBytesRecorded:
                            data = ctypes.string_at(header.lpData, header.dwBytesRecorded)
                            wav_file.writeframesraw(data)
                            total_bytes += int(header.dwBytesRecorded)
                        header.dwBytesRecorded = 0
                        self._check(
                            winmm.waveInAddBuffer(handle, ctypes.byref(header), header_size),
                            "requeue a microphone buffer",
                        )
                time.sleep(0.012)

            winmm.waveInStop(handle)
            winmm.waveInReset(handle)
            time.sleep(0.035)
            for header in headers:
                if header.dwBytesRecorded:
                    data = ctypes.string_at(header.lpData, header.dwBytesRecorded)
                    wav_file.writeframesraw(data)
                    total_bytes += int(header.dwBytesRecorded)

            duration = total_bytes / bytes_per_second if bytes_per_second else 0.0
            self._result = AudioResult(
                path=self._temp_path,
                duration_seconds=duration,
                sample_rate=self.sample_rate,
                channels=self.channels,
            )
        except Exception as error:
            self._error = error
        finally:
            self._ready.set()
            if opened:
                winmm.waveInReset(handle)
                for header in prepared:
                    winmm.waveInUnprepareHeader(handle, ctypes.byref(header), header_size)
                winmm.waveInClose(handle)
            if wav_file:
                wav_file.close()

    def _check(self, result: int, action: str) -> None:
        if result == MMSYSERR_NOERROR:
            return
        buffer = ctypes.create_unicode_buffer(256)
        winmm = ctypes.windll.winmm
        winmm.waveInGetErrorTextW.argtypes = [wintypes.UINT, wintypes.LPWSTR, wintypes.UINT]
        winmm.waveInGetErrorTextW.restype = wintypes.UINT
        winmm.waveInGetErrorTextW(result, buffer, len(buffer))
        detail = buffer.value or f"Windows audio error {result}"
        raise RuntimeError(f"Could not {action}: {detail}")

    def _cleanup_temp(self) -> None:
        if self._temp_path:
            try:
                self._temp_path.unlink(missing_ok=True)
            except OSError:
                pass
            self._temp_path = None
