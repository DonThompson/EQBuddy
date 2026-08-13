#!/usr/bin/env python3
"""Promotion shim: rebuild ItemCatalog.json.gz from the items dump.

The build itself is C# (scripts/harvests/itemcatalog-build) so the catalog flows
through the app's OWN item parsers — see items-harvest.py for why. This shim just
lets refresh.py's python-only runner drive it like every other promotion.
"""

import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parent / "itemcatalog-build"

r = subprocess.run(["dotnet", "run", "--project", str(PROJECT), "-c", "Release"])
sys.exit(r.returncode)
