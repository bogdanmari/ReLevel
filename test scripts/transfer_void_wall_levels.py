# -*- coding: utf-8 -*-
"""RevitPythonShell 2025, MODEL COPY. Successful transfer is KEPT. Use Undo to revert. No level deletion."""
from __future__ import print_function
import io
import json
import os
import traceback
import uuid
from datetime import datetime
import clr
clr.AddReference("RevitAPI")
clr.AddReference("Microsoft.VisualBasic")
from Autodesk.Revit import DB
from Autodesk.Revit.Exceptions import RegenerationFailedException
from Microsoft.VisualBasic import Interaction
from System import Int64

WALLS = {21941510: "88bc9681-2e36-4137-aa95-77fded689072-014ecd06",
         22051705: "31e3c14c-2ce9-4f79-904f-20ec7d474e74-01507b79"}
WATCH_IDS = [21941484, 21941486, 21941487, 21941488, 21941490, 21941491,
             21941492, 21941509, 21941510, 21941512, 21941513,
             22051679, 22051681, 22051682, 22051683, 22051685, 22051686,
             22051687, 22051704, 22051705, 22051707, 22051708]
PROJECT_SCRIPT_DIR = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
try:
    text_type = unicode
except NameError:
    text_type = str


def eid(value):
    return DB.ElementId(Int64(value))


def num(value):
    return int(value.Value)


def read(action):
    try:
        return action()
    except RegenerationFailedException:
        raise
    except Exception as error:
        return {"read_error": text_type(error)}


def xyz(p):
    return [p.X, p.Y, p.Z]


def geometry_summary(element):
    solids, curves = [], []
    def visit(items):
        if items is None:
            return
        for item in items:
            if isinstance(item, DB.GeometryInstance):
                visit(item.GetInstanceGeometry())
            elif isinstance(item, DB.Solid):
                solids.append({"volume_ft3": item.Volume, "area_ft2": item.SurfaceArea,
                               "faces": item.Faces.Size, "edges": item.Edges.Size})
            elif isinstance(item, DB.Curve):
                curves.append([xyz(p) for p in item.Tessellate()])
    options = DB.Options()
    options.IncludeNonVisibleObjects = True
    try:
        visit(element.get_Geometry(options))
    finally:
        options.Dispose()
    return {"solids": solids, "curves_ft": curves}


def snapshot(doc, value):
    e = doc.GetElement(eid(value))
    if e is None:
        return {"missing": True}
    result = {"id": value, "class": e.GetType().FullName,
              "name": read(lambda: e.Name), "level_id": read(lambda: num(e.LevelId))}
    def box_data():
        box = e.get_BoundingBox(None)
        if box is None:
            return None
        return [xyz(box.Transform.OfPoint(box.Min)), xyz(box.Transform.OfPoint(box.Max))]
    def location_data():
        loc = e.Location
        if isinstance(loc, DB.LocationPoint):
            return {"point_ft": xyz(loc.Point), "rotation": loc.Rotation}
        if isinstance(loc, DB.LocationCurve):
            return {"curve_ft": [xyz(p) for p in loc.Curve.Tessellate()]}
        return None
    result["bbox_ft"] = read(box_data)
    result["location"] = read(location_data)
    result["geometry"] = read(lambda: geometry_summary(e))
    result["dependent_ids"] = read(lambda: sorted(num(i) for i in e.GetDependentElements(None)))
    result["constraints"] = {}
    for name in ("WALL_BASE_CONSTRAINT", "WALL_BASE_OFFSET", "WALL_HEIGHT_TYPE", "WALL_TOP_OFFSET"):
        def parameter_value(name=name):
            p = e.get_Parameter(getattr(DB.BuiltInParameter, name))
            if p is None:
                return None
            return {"value": num(p.AsElementId()) if p.StorageType == DB.StorageType.ElementId
                    else p.AsDouble(), "readonly": p.IsReadOnly}
        result["constraints"][name] = read(parameter_value)
    if isinstance(e, DB.Family):
        result["is_in_place"] = read(lambda: e.IsInPlace)
        result["placement_type"] = read(lambda: text_type(e.FamilyPlacementType))
    return result


def differences(a, b, path=""):
    if isinstance(a, dict) and isinstance(b, dict):
        result = []
        for key in sorted(set(a) | set(b)):
            result.extend(differences(a.get(key), b.get(key), path + "/" + key))
        return result
    if isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
        result = []
        for i, (x, y) in enumerate(zip(a, b)):
            result.extend(differences(x, y, path + "/" + str(i)))
        return result
    if isinstance(a, float) and isinstance(b, float) and abs(a - b) <= 1e-7:
        return []
    return [] if a == b else [path]


class CaptureFailures(DB.IFailuresPreprocessor):
    __namespace__ = "ReLevelVoidWallTransfer.Run" + uuid.uuid4().hex

    def __init__(self, result):
        self.result = result
        self.corrupted = False

    def PreprocessFailures(self, accessor):
        failures = list(accessor.GetFailureMessages())
        for f in failures:
            self.corrupted |= f.GetSeverity() == DB.FailureSeverity.DocumentCorruption
            self.result["failures"].append({"message": f.GetDescriptionText(),
                "severity": text_type(f.GetSeverity()),
                "ids": [num(i) for i in f.GetFailingElementIds()],
                "additional_ids": [num(i) for i in f.GetAdditionalElementIds()]})
        return (DB.FailureProcessingResult.ProceedWithRollBack if failures
                else DB.FailureProcessingResult.Continue)


def require_status(actual, expected):
    if actual != expected:
        raise RuntimeError("Transaction status {0}; expected {1}".format(actual, expected))


def probe(doc, target, data):
    watch = set(WATCH_IDS)
    assignments = []
    for value, unique_id in sorted(WALLS.items()):
        wall = doc.GetElement(eid(value))
        if not isinstance(wall, DB.Wall) or wall.UniqueId != unique_id:
            raise ValueError("Expected wall {0} not found in this document.".format(value))
        base = wall.get_Parameter(DB.BuiltInParameter.WALL_BASE_CONSTRAINT)
        offset = wall.get_Parameter(DB.BuiltInParameter.WALL_BASE_OFFSET)
        if wall.Pinned or base is None or offset is None or base.IsReadOnly or offset.IsReadOnly:
            raise ValueError("Wall {0}: pinned or unavailable base parameters.".format(value))
        source = doc.GetElement(base.AsElementId())
        if not isinstance(source, DB.Level) or num(source.Id) != 30 or source.Id == target.Id:
            raise ValueError("Expected LEVEL 0 (30) base and a different target.")
        assignments.append((value, offset.AsDouble() + source.ProjectElevation - target.ProjectElevation))
    for value in WATCH_IDS:
        element = doc.GetElement(eid(value))
        if element is not None:
            watch.update(num(i) for i in element.GetDependentElements(None))
            if isinstance(element, DB.Wall):
                watch.update(num(i) for i in DB.JoinGeometryUtils.GetJoinedElements(doc, element))
    data["before"] = {str(i): read(lambda i=i: snapshot(doc, i)) for i in sorted(watch)}
    data["assignments"] = [{"wall_id": i, "new_base_offset_ft": v} for i, v in assignments]
    modified, added, deleted = set(), set(), set()
    def changed(sender, args):
        if args.GetDocument().Equals(doc):
            modified.update(num(i) for i in args.GetModifiedElementIds())
            added.update(num(i) for i in args.GetAddedElementIds())
            deleted.update(num(i) for i in args.GetDeletedElementIds())
    group = DB.TransactionGroup(doc, "ReLevel: transfer internal wall bases")
    tx = DB.Transaction(doc, "ReLevel: internal wall base levels")
    handler = CaptureFailures(data)
    try:
        require_status(group.Start(), DB.TransactionStatus.Started)
        require_status(tx.Start(), DB.TransactionStatus.Started)
        options = tx.GetFailureHandlingOptions()
        options.SetFailuresPreprocessor(handler)
        options.SetClearAfterRollback(True)
        options.SetForcedModalHandling(True)
        tx.SetFailureHandlingOptions(options)
        for value, new_offset in assignments:
            wall = doc.GetElement(eid(value))
            if not wall.get_Parameter(DB.BuiltInParameter.WALL_BASE_CONSTRAINT).Set(target.Id):
                raise RuntimeError("Base level assignment rejected: {0}".format(value))
            if not wall.get_Parameter(DB.BuiltInParameter.WALL_BASE_OFFSET).Set(new_offset):
                raise RuntimeError("Base offset assignment rejected: {0}".format(value))
        doc.Application.DocumentChanged += changed
        try:
            status = tx.Commit()
            data["commit_status"] = text_type(status)
        finally:
            doc.Application.DocumentChanged -= changed
        if handler.corrupted:
            raise RuntimeError("Document corruption reported; stop model reads and roll back.")
        if status == DB.TransactionStatus.Committed:
            data["changed_ids"] = {"modified": sorted(modified), "added": sorted(added), "deleted": sorted(deleted)}
            data["modified_without_before_snapshot"] = sorted(modified - watch)
            data["after"] = {str(i): read(lambda i=i: snapshot(doc, i))
                             for i in sorted(watch | modified | added | deleted)}
            data["differences"] = {str(i): differences(data["before"][str(i)], data["after"][str(i)])
                                   for i in sorted(watch)}
            print("Committed inside temporary group. Comparing {0} watched objects.".format(len(watch)))
            for i, paths in sorted(data["differences"].items()):
                if paths:
                    print("{0}: {1}".format(i, ", ".join(paths[:12])))
            # Keep only a fully committed transfer with a successfully captured report.
            # An exception before Assimilate still rolls the entire group back below.
            group_status = group.Assimilate()
            data["group_assimilate"] = text_type(group_status)
            require_status(group_status, DB.TransactionStatus.Committed)
            data["kept"] = True
            print("TRANSFER KEPT. Both walls can be reverted with one Undo.")
            print("Now inspect LEVEL 0 deletion on the model copy. No levels were deleted by this script.")
    finally:
        # No model reads here, including after a regeneration failure.
        try:
            if tx.GetStatus() == DB.TransactionStatus.Started:
                rollback = tx.RollBack()
                data["transaction_rollback"] = text_type(rollback)
                require_status(rollback, DB.TransactionStatus.RolledBack)
            if tx.GetStatus() == DB.TransactionStatus.Pending:
                raise RuntimeError("Pending transaction. Stop and check Revit.")
        finally:
            try:
                if group.GetStatus() == DB.TransactionStatus.Started:
                    rollback = group.RollBack()
                    data["group_rollback"] = text_type(rollback)
                    require_status(rollback, DB.TransactionStatus.RolledBack)
            finally:
                tx.Dispose()
                group.Dispose()


def main():
    uidoc = __revit__.ActiveUIDocument
    if uidoc is None:
        raise ValueError("Open a MODEL COPY first.")
    doc = uidoc.Document
    if doc.IsReadOnly or doc.IsModifiable:
        raise ValueError("Requires a writable document without an open transaction.")
    levels = list(DB.FilteredElementCollector(doc).OfClass(DB.Level))
    for level in sorted(levels, key=lambda item: item.ProjectElevation):
        print("{0}: {1} ({2} ft)".format(num(level.Id), level.Name, level.ProjectElevation))
    answer = Interaction.InputBox("MODEL COPY: change bases of 21941510 and 22051705.\n"
        "Successful transfer is KEPT in the model. Use Undo to revert.\nEnter target level ID or exact name; Cancel exits.",
        "ReLevel - KEEP internal wall transfer", "1519130").strip()
    if not answer:
        return
    targets = [level for level in levels if str(num(level.Id)) == answer]
    if not targets:
        targets = [level for level in levels if level.Name == answer]
    if len(targets) != 1:
        raise ValueError("Target missing or ambiguous; use its ID.")
    script_file = globals().get("__file__", "")
    folder = (os.path.dirname(os.path.abspath(script_file))
              if script_file and os.path.isfile(script_file) else PROJECT_SCRIPT_DIR)
    folder = os.path.join(folder, "reports")
    if not os.path.isdir(folder):
        os.makedirs(folder)
    path = os.path.join(folder, "void_wall_transfer_{0}_{1}.json".format(
        datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8]))
    data = {"document": doc.Title, "revit_version": doc.Application.VersionNumber,
            "revit_build": doc.Application.VersionBuild, "target_id": num(targets[0].Id),
            "target_name": targets[0].Name, "failures": [], "commit_status": "NotStarted", "kept": False,
            "scope": "Base-level changes only; successful group is assimilated and KEPT. Undo reverts both walls. "
                     "Geometry summary and location are not a complete shape/reference comparison. "
                     "DocumentChanged IDs indicate modifications, not necessarily movement."}
    try:
        probe(doc, targets[0], data)
    except Exception:
        data["error"] = traceback.format_exc()
        print(data["error"])
    finally:
        print("Commit: {0}; kept: {1}; group rollback: {2}".format(data["commit_status"], data.get("kept", False), data.get("group_rollback", "not performed")))
        with io.open(path, "w", encoding="utf-8") as stream:
            stream.write(text_type(json.dumps(data, ensure_ascii=False, indent=2)))
        print("Report: " + path)


if __name__ == "__main__":
    main()
