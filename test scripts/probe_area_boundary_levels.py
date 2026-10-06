# -*- coding: utf-8 -*-
"""RevitPythonShell on a model copy. All changes are ALWAYS rolled back."""
from __future__ import print_function
import io
import json
import os
import sys
import traceback
import uuid
from datetime import datetime
import clr
clr.AddReference("RevitAPI")
clr.AddReference("Microsoft.VisualBasic")
from Autodesk.Revit import DB
from Microsoft.VisualBasic import Interaction
from System import Int64

try:
    text_type = unicode
except NameError:
    text_type = str

IDS = [2774087, 2774088, 2774089, 2774090, 2774091, 2774092, 2774093, 3545837]
FOLDER = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
sys.path.insert(0, FOLDER)
try:
    # Import only helpers; the other script's main is guarded and is not executed.
    from change_room_separator_plane import snapshot, number, CaptureFailures
finally:
    sys.path.pop(0)


def area_snapshots(doc, level_ids):
    result = []
    options = DB.SpatialElementBoundaryOptions()
    collector = DB.FilteredElementCollector(doc).OfCategory(DB.BuiltInCategory.OST_Areas).WhereElementIsNotElementType()
    try:
        for area in collector:
            if not isinstance(area, DB.Area) or number(area.LevelId) not in level_ids:
                continue
            loops = area.GetBoundarySegments(options)
            result.append({"id": number(area.Id), "level_id": number(area.LevelId),
                           "scheme_id": number(area.AreaScheme.Id), "name": area.Name,
                           "area_ft2": area.Area,
                           "boundary_ids": [[number(s.ElementId) for s in loop] for loop in loops] if loops else []})
    finally:
        collector.Dispose()
        options.Dispose()
    return result


def rollback(transaction, key, report):
    if transaction.GetStatus() == DB.TransactionStatus.Started:
        status = transaction.RollBack()
        report[key] = text_type(status)
        if status != DB.TransactionStatus.RolledBack:
            raise RuntimeError("Rollback failed: " + key + "=" + text_type(status))
    elif transaction.GetStatus() == DB.TransactionStatus.Pending:
        raise RuntimeError("Transaction is pending: " + key)


def main(recreate=False, keep=False):
    if keep and not recreate:
        raise ValueError("Keeping changes is supported only for boundary recreation.")
    uidoc = __revit__.ActiveUIDocument
    if uidoc is None:
        raise ValueError("Open a project copy first.")
    doc = uidoc.Document
    reports = os.path.join(FOLDER, "reports")
    if not os.path.isdir(reports):
        os.makedirs(reports)
    prefix = "area_boundary_transfer" if keep else "area_boundary_recreate" if recreate else "area_boundary_probe"
    path = os.path.join(reports, "{0}_{1}_{2}.json".format(prefix,
        datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8]))
    data = {"status": "NotStarted", "document": doc.Title,
            "revit_version": doc.Application.VersionNumber, "revit_build": doc.Application.VersionBuild,
            "scope": ("Recreate boundaries in original schemes; preserve XYZ; " if recreate else "SketchPlane assignment only; ")
                     + ("successful changes KEPT; units feet" if keep else "all changes rolled back; units feet"),
            "kept": False,
            "failures": [], "requested_ids": IDS}
    try:
        if doc.IsReadOnly or doc.IsModifiable:
            raise ValueError("Requires writable document with no open transaction.")
        lines = [doc.GetElement(DB.ElementId(Int64(i))) for i in IDS]
        for line in lines:
            if not isinstance(line, DB.ModelLine) or number(line.Category.Id) != int(DB.BuiltInCategory.OST_AreaSchemeLines):
                raise ValueError("An expected Area Boundary ModelLine is missing or has another category.")
            if number(line.LevelId) != 30:
                raise ValueError("Expected source level ID 30 for all eight lines.")
            if line.Pinned or line.GroupId != DB.ElementId.InvalidElementId or line.AssemblyInstanceId != DB.ElementId.InvalidElementId or line.DesignOption is not None:
                raise ValueError("Pinned/grouped/assembled/design-option lines are not supported by this probe.")
        data["before"] = [snapshot(line) for line in lines]
        levels = list(DB.FilteredElementCollector(doc).OfClass(DB.Level))
        for level in sorted(levels, key=lambda l: l.Name):
            print("{0}: {1} ({2} ft)".format(number(level.Id), level.Name, level.ProjectElevation))
        answer = Interaction.InputBox("Enter target level ID or exact name.\n" +
            ("Successful replacement and new area plans WILL BE KEPT. One Undo reverts them.\n" if keep
             else "ALL changes will be rolled back.\n") + "Use a model copy.",
            "ReLevel - Area Boundary probe", "1518530").strip()
        if not answer:
            data["status"] = "Cancelled"
            return
        matches = [level for level in levels if text_type(number(level.Id)) == answer or level.Name == answer]
        if len(matches) != 1 or number(matches[0].Id) == 30:
            raise ValueError("Choose one existing level different from LEVEL 0.")
        target = matches[0]
        data["target"] = {"id": number(target.Id), "name": target.Name, "elevation_ft": target.ProjectElevation}
        schemes = {}
        if recreate:
            for line in lines:
                if any(abs(line.GeometryCurve.GetEndPoint(i).Z - target.ProjectElevation) > 1e-8 for i in (0, 1)):
                    raise ValueError("Recreation probe requires target at the same elevation; XYZ must remain unchanged.")
            # Determine membership from the same reverse dependency relation as the diagnostic report.
            for scheme in DB.FilteredElementCollector(doc).OfClass(DB.AreaScheme):
                for dependent in scheme.GetDependentElements(None):
                    key = number(dependent)
                    if key in IDS:
                        if key in schemes:
                            raise ValueError("Ambiguous AreaScheme for line " + str(key))
                        schemes[key] = scheme.Id
            if set(schemes) != set(IDS):
                raise ValueError("Could not determine a unique AreaScheme for every line.")
            data["schemes"] = dict((str(key), number(value)) for key, value in schemes.items())
        watched_levels = [30, number(target.Id)]
        data["areas_before"] = area_snapshots(doc, watched_levels)
        group = DB.TransactionGroup(doc, "ReLevel Area Boundary probe - rollback")
        transaction = DB.Transaction(doc, "ReLevel Area Boundary SketchPlane probe")
        try:
            if group.Start() != DB.TransactionStatus.Started or transaction.Start() != DB.TransactionStatus.Started:
                raise RuntimeError("Could not start transaction.")
            handler = CaptureFailures(data["failures"])
            options = transaction.GetFailureHandlingOptions()
            options.SetFailuresPreprocessor(handler)
            options.SetClearAfterRollback(True)
            options.SetForcedModalHandling(True)
            transaction.SetFailureHandlingOptions(options)
            plane = DB.SketchPlane.Create(doc, target.Id)
            replacements = []
            views = {}
            if recreate:
                sys.path.insert(0, FOLDER)
                try:
                    from recreate_room_separator import copy_parameters, parameters
                finally:
                    sys.path.pop(0)
                data["replacements"] = []
                data["created_view_ids"] = []
                for view in DB.FilteredElementCollector(doc).OfClass(DB.ViewPlan):
                    if not view.IsTemplate and view.ViewType == DB.ViewType.AreaPlan and view.GenLevel.Id == target.Id:
                        views.setdefault(number(view.AreaScheme.Id), view)
            for line in lines:
                data["last_attempted_id"] = number(line.Id)
                if not recreate:
                    line.SketchPlane = plane
                    continue
                scheme_id = schemes[number(line.Id)]
                key = number(scheme_id)
                if key not in views:
                    views[key] = DB.ViewPlan.CreateAreaPlan(doc, scheme_id, target.Id)
                    data["created_view_ids"].append(number(views[key].Id))
                curve = line.GeometryCurve.Clone()
                try:
                    replacement = doc.Create.NewAreaBoundaryLine(plane, curve, views[key])
                finally:
                    curve.Dispose()
                item = {"old_id": number(line.Id), "new_id": number(replacement.Id),
                        "scheme_id": key, "view_id": number(views[key].Id),
                        "parameters_before": parameters(line), "parameter_copy": []}
                data["replacements"].append(item)
                copy_parameters(line, replacement, item["parameter_copy"])
                replacements.append(replacement.Id)
            if recreate:
                data["deleted_ids"] = []
                for old_id in IDS:
                    data["deleted_ids"].extend(number(i) for i in doc.Delete(DB.ElementId(Int64(old_id))))
            status = transaction.Commit()
            data["status"] = text_type(status)
            # After any failed Commit/regeneration, perform only rollback; no more model reads.
            if status == DB.TransactionStatus.Committed:
                after_ids = replacements if recreate else [DB.ElementId(Int64(i)) for i in IDS]
                data["after"] = [snapshot(doc.GetElement(i)) for i in after_ids]
                if recreate:
                    for item in data["replacements"]:
                        item["parameters_after"] = parameters(doc.GetElement(DB.ElementId(Int64(item["new_id"]))))
                data["areas_after"] = area_snapshots(doc, watched_levels)
                if keep:
                    group_status = group.Assimilate()
                    data["group_assimilate"] = text_type(group_status)
                    if group_status != DB.TransactionStatus.Committed:
                        raise RuntimeError("Could not assimilate replacement group.")
                    data["kept"] = True
        finally:
            try:
                rollback(transaction, "transaction_rollback", data)
            finally:
                try:
                    rollback(group, "group_rollback", data)
                finally:
                    transaction.Dispose()
                    group.Dispose()
    except Exception as error:
        data["status"] = "Error"
        data["error"] = text_type(error)
        data["traceback"] = traceback.format_exc()
    finally:
        with io.open(path, "w", encoding="utf-8") as stream:
            stream.write(text_type(json.dumps(data, ensure_ascii=False, indent=2)))
        print("Status: " + data["status"])
        print("TRANSFER KEPT" if data["kept"] else "No transfer kept")
        print("Group rollback: " + data.get("group_rollback", "not started"))
        print("Report: " + path)


if __name__ == "__main__":
    main()
