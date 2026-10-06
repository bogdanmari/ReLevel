# -*- coding: utf-8 -*-
"""Run in RevitPythonShell on a model copy. ALWAYS rolls back, including new plans."""
import sys

FOLDER = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
sys.path.insert(0, FOLDER)
try:
    import probe_area_boundary_levels as probe
finally:
    sys.path.pop(0)

if __name__ == "__main__":
    probe.__revit__ = __revit__
    probe.main(recreate=True)
