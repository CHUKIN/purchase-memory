#!/usr/bin/env python3
"""Launcher for the OCR comparison tool (package ocr_compare/, see ocr_compare/README.md)."""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

from ocr_compare.cli import main  # noqa: E402

if __name__ == "__main__":
    main()
