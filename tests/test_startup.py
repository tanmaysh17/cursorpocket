from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path

from cursorpocket.startup import startup_command


class StartupCommandTests(unittest.TestCase):
    def test_frozen_app_registers_the_executable_only(self) -> None:
        command = startup_command(
            executable=Path("C:/Program Files/CursorPocket/CursorPocket.exe"),
            frozen=True,
        )

        self.assertEqual(
            command,
            subprocess.list2cmdline(
                [str(Path("C:/Program Files/CursorPocket/CursorPocket.exe").resolve())]
            ),
        )

    def test_source_app_registers_python_and_main_script(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            executable = Path(temp_dir) / "python.exe"
            main_path = Path(temp_dir) / "Cursor Pocket" / "main.py"
            command = startup_command(
                executable=executable,
                main_path=main_path,
                frozen=False,
            )

            self.assertEqual(
                command,
                subprocess.list2cmdline(
                    [str(executable.resolve()), str(main_path.resolve())]
                ),
            )


if __name__ == "__main__":
    unittest.main()
