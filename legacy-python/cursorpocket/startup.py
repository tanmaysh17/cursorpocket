from __future__ import annotations

import subprocess
import sys
from pathlib import Path

try:
    import winreg
except ImportError:  # pragma: no cover - Windows-only module
    winreg = None  # type: ignore[assignment]


RUN_KEY = r"Software\Microsoft\Windows\CurrentVersion\Run"
VALUE_NAME = "CursorPocket"


def startup_command(
    executable: str | Path | None = None,
    main_path: str | Path | None = None,
    frozen: bool | None = None,
) -> str:
    executable_path = Path(executable or sys.executable).resolve()
    is_frozen = bool(getattr(sys, "frozen", False)) if frozen is None else frozen
    if is_frozen:
        return subprocess.list2cmdline([str(executable_path)])
    pythonw = executable_path.with_name("pythonw.exe")
    runner = pythonw if pythonw.exists() else executable_path
    script = Path(main_path or Path(__file__).resolve().parents[1] / "main.py").resolve()
    return subprocess.list2cmdline([str(runner), str(script)])


class StartupManager:
    def is_enabled(self) -> bool:
        if winreg is None:
            return False
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as key:
                value, _kind = winreg.QueryValueEx(key, VALUE_NAME)
            return bool(value)
        except OSError:
            return False

    def set_enabled(self, enabled: bool) -> None:
        if winreg is None:
            return
        with winreg.CreateKey(winreg.HKEY_CURRENT_USER, RUN_KEY) as key:
            if enabled:
                winreg.SetValueEx(
                    key,
                    VALUE_NAME,
                    0,
                    winreg.REG_SZ,
                    startup_command(),
                )
                return
            try:
                winreg.DeleteValue(key, VALUE_NAME)
            except FileNotFoundError:
                pass
