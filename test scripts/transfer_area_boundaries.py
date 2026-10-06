# -*- coding: utf-8 -*-
"""RevitPythonShell on a model copy. Successful replacement is KEPT; one Undo reverts it."""
import sys

FOLDER = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
sys.path.insert(0, FOLDER)
try:
    import probe_area_boundary_levels as probe
    # A previous probe may have left an older module cached in this RevitPythonShell session.
    try:
        from importlib import reload
    except ImportError:
        pass  # IronPython 2 provides reload as a built-in.
    probe = reload(probe)
finally:
    sys.path.pop(0)

if __name__ == "__main__":
    probe.__revit__ = __revit__
    probe.main(recreate=True, keep=True)
