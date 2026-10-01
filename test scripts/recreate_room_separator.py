# -*- coding: utf-8 -*-
"""Diagnostic replacement of 22043147. ALWAYS rolled back, including successful Commit.
Run in RevitPythonShell with change_room_separator_plane.py in the same folder.
Initial probe supports levels at the same elevation; it preserves XYZ.
"""
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
from System import Int32, Int64, Double

try:
    text_type = unicode
except NameError:
    text_type = str
ELEMENT_ID = 22043147
script_file = globals().get("__file__", "")
FOLDER = (os.path.dirname(os.path.abspath(script_file)) if script_file and os.path.isfile(script_file)
          else r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts")
sys.path.insert(0, FOLDER)
try:
    from change_room_separator_plane import snapshot, number, CaptureFailures
finally:
    sys.path.pop(0)


def value(p):
    if not p.HasValue:
        return None
    if p.StorageType == DB.StorageType.ElementId:
        return number(p.AsElementId())
    if p.StorageType == DB.StorageType.Integer:
        return p.AsInteger()
    if p.StorageType == DB.StorageType.Double:
        return p.AsDouble()
    if p.StorageType == DB.StorageType.String:
        return p.AsString()
    return None


def parameters(element):
    return [{"id": number(p.Id), "name": p.Definition.Name,
             "shared_guid": text_type(p.GUID) if p.IsShared else None,
             "storage": text_type(p.StorageType), "readonly": p.IsReadOnly,
             "has_value": p.HasValue, "value": value(p)} for p in element.Parameters]


class CaptureRecreationFailures(CaptureFailures):
    __namespace__ = "ReLevelRoomRecreate.Run" + uuid.uuid4().hex

    def PreprocessFailures(self, accessor):
        # Keep every message in the report before dismissing the known overlap warning.
        result = CaptureFailures.PreprocessFailures(self, accessor)
        failures = list(accessor.GetFailureMessages())
        if failures and all(f.GetSeverity() == DB.FailureSeverity.Warning
                and text_type(f.GetFailureDefinitionId().Guid).lower()
                == "f7b3a015-c3eb-4a3f-b345-c474ec07d43f" for f in failures):
            for failure in failures:
                accessor.DeleteWarning(failure)
            # This is only a diagnostic Commit; the outer group still rolls it back.
            return DB.FailureProcessingResult.Continue
        return result


def copy_parameters(source, target, report):
    destinations = dict((number(p.Id), p) for p in target.Parameters)
    source_parameters = list(source.Parameters)
    # Creation phase must precede demolition phase, regardless of ParameterSet order.
    source_parameters.sort(key=lambda p: 0 if number(p.Id) == int(DB.BuiltInParameter.PHASE_CREATED)
                           else 2 if number(p.Id) == int(DB.BuiltInParameter.PHASE_DEMOLISHED) else 1)
    for p in source_parameters:
        if p.IsReadOnly or not p.HasValue:
            continue
        q = destinations.get(number(p.Id))
        if q is None or q.StorageType != p.StorageType:
            raise ValueError("Missing/incompatible parameter: " + p.Definition.Name)
        old = value(p)
        if value(q) == old and q.HasValue:
            report.append({"id": number(p.Id), "name": p.Definition.Name, "result": "AlreadyEqual"})
            continue
        if q.IsReadOnly:
            raise ValueError("Target parameter is readonly: " + p.Definition.Name)
        if p.StorageType == DB.StorageType.ElementId:
            ok = q.Set(DB.ElementId(Int64(old)))
        elif p.StorageType == DB.StorageType.Integer:
            ok = q.Set(Int32(old))
        elif p.StorageType == DB.StorageType.Double:
            ok = q.Set(Double(old))
        elif p.StorageType == DB.StorageType.String:
            ok = q.Set(old if old is not None else "")
        else:
            raise ValueError("Unsupported parameter storage: " + p.Definition.Name)
        if not ok:
            raise ValueError("Parameter assignment rejected: " + p.Definition.Name)
        report.append({"id": number(p.Id), "name": p.Definition.Name, "result": "Set"})


def main():
    uidoc = __revit__.ActiveUIDocument
    if uidoc is None:
        raise ValueError("Open a project first.")
    doc = uidoc.Document
    reports = os.path.join(FOLDER, "reports")
    if not os.path.isdir(reports):
        os.makedirs(reports)
    path = os.path.join(reports, "room_recreate_{0}_{1}_{2}.json".format(
        ELEMENT_ID, datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8]))
    data = {"status": "NotStarted", "scope": "Diagnostic only; always rolled back; preserves XYZ",
            "document": doc.Title, "revit_version": doc.Application.VersionNumber,
            "revit_build": doc.Application.VersionBuild, "failures": [], "parameter_copy": []}
    try:
        if doc.IsReadOnly or doc.IsModifiable:
            raise ValueError("Requires writable document without an open transaction.")
        source = doc.GetElement(DB.ElementId(Int64(ELEMENT_ID)))
        if not isinstance(source, DB.ModelLine) or number(source.Category.Id) != int(DB.BuiltInCategory.OST_RoomSeparationLines):
            raise ValueError("22043147 is not a Room Separation ModelLine.")
        if source.Pinned or source.GroupId != DB.ElementId.InvalidElementId or source.AssemblyInstanceId != DB.ElementId.InvalidElementId or source.DesignOption is not None:
            raise ValueError("Pinned/grouped/assembled/design-option line is not supported by this probe.")
        data["before"] = snapshot(source)
        data["parameters_before"] = parameters(source)
        levels = list(DB.FilteredElementCollector(doc).OfClass(DB.Level))
        for level in sorted(levels, key=lambda l: l.ProjectElevation):
            print("{0}: {1} ({2} ft)".format(number(level.Id), level.Name, level.ProjectElevation))
        answer = Interaction.InputBox("Diagnostic recreation, ALWAYS ROLLED BACK.\n"
            "Enter target level ID or exact name.\nThis probe supports the SAME elevation only.",
            "ReLevel - recreate Room Separator", "").strip()
        if not answer:
            data["status"] = "Cancelled"
            return
        matches = [l for l in levels if text_type(number(l.Id)) == answer]
        if not matches:
            matches = [l for l in levels if l.Name == answer]
        if len(matches) != 1 or matches[0].Id == source.LevelId:
            raise ValueError("Choose one existing level different from the source.")
        target = matches[0]
        curve = source.GeometryCurve.Clone()
        if any(abs(curve.GetEndPoint(i).Z - target.ProjectElevation) > 1e-8 for i in (0, 1)):
            raise ValueError("This initial probe requires equal elevations; XYZ will not be moved.")
        views = [v for v in DB.FilteredElementCollector(doc).OfClass(DB.ViewPlan)
                 if not v.IsTemplate and v.ViewType == DB.ViewType.FloorPlan
                 and v.GenLevel is not None and v.GenLevel.Id == target.Id
                 and v.get_Parameter(DB.BuiltInParameter.VIEW_PHASE).AsElementId() == source.CreatedPhaseId]
        if not views:
            raise ValueError("A floor plan on the target level with the source creation phase is required.")
        view = sorted(views, key=lambda v: number(v.Id))[0]
        data["target"] = {"id": number(target.Id), "name": target.Name, "view_id": number(view.Id)}
        # Cache identities before deletion; do not dereference deleted elements afterwards.
        identities = dict((number(e.Id), {"id": number(e.Id), "class": e.GetType().FullName,
            "name": e.Name, "category": e.Category.Name if e.Category else None})
            for e in DB.FilteredElementCollector(doc).WhereElementIsNotElementType())
        group = DB.TransactionGroup(doc, "ReLevel diagnostic recreation - rollback")
        transaction = DB.Transaction(doc, "ReLevel diagnostic Room Separator replacement")
        try:
            if group.Start() != DB.TransactionStatus.Started or transaction.Start() != DB.TransactionStatus.Started:
                raise RuntimeError("Could not start diagnostic transaction.")
            handler = CaptureRecreationFailures(data["failures"])
            options = transaction.GetFailureHandlingOptions()
            options.SetFailuresPreprocessor(handler)
            options.SetClearAfterRollback(True)
            options.SetForcedModalHandling(True)
            transaction.SetFailureHandlingOptions(options)
            plane = DB.SketchPlane.Create(doc, target.Id)
            curves = DB.CurveArray()
            curves.Append(curve)
            created = list(doc.Create.NewRoomBoundaryLines(plane, curves, view))
            if len(created) != 1:
                raise RuntimeError("Expected exactly one new Room Separator.")
            replacement = created[0]
            replacement_id = replacement.Id
            copy_parameters(source, replacement, data["parameter_copy"])
            data["deleted_elements"] = [identities.get(number(i), {"id": number(i)})
                                        for i in doc.Delete(source.Id)]
            status = transaction.Commit()
            data["status"] = text_type(status)
            if status == DB.TransactionStatus.Committed:
                replacement = doc.GetElement(replacement_id)
                data["after"] = snapshot(replacement)
                data["parameters_after"] = parameters(replacement)
                after = dict((p["id"], p) for p in data["parameters_after"])
                data["parameter_differences"] = [{"before": p, "after": after.get(p["id"])}
                    for p in data["parameters_before"] if p["id"] not in after
                    or p["value"] != after[p["id"]]["value"] or p["has_value"] != after[p["id"]]["has_value"]]
        finally:
            # On regeneration failure no further model reads; rollback only.
            try:
                if transaction.GetStatus() == DB.TransactionStatus.Started:
                    data["transaction_rollback"] = text_type(transaction.RollBack())
                if transaction.GetStatus() == DB.TransactionStatus.Pending:
                    raise RuntimeError("Pending failure handling; stop and check Revit.")
            finally:
                if group.GetStatus() == DB.TransactionStatus.Started:
                    rollback = group.RollBack()
                    data["group_rollback"] = text_type(rollback)
                    if rollback != DB.TransactionStatus.RolledBack:
                        raise RuntimeError("Group rollback was not confirmed.")
                transaction.Dispose()
                group.Dispose()
    except Exception:
        data["error"] = traceback.format_exc()
        print(data["error"])
    finally:
        with io.open(path, "w", encoding="utf-8") as stream:
            stream.write(text_type(json.dumps(data, ensure_ascii=False, indent=2)))
        print("Probe status: " + data["status"])
        print("Group rollback: " + data.get("group_rollback", "not started"))
        print("Report: " + path)


if __name__ == "__main__":
    main()
