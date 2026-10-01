# -*- coding: utf-8 -*-
"""Run in RevitPythonShell on a model copy. Successful change is kept; use Undo.

Changes only the SketchPlane property of room separator 22043147.
Does not delete the old plane/level, recreate the line, or set its curve.
"""
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
from Microsoft.VisualBasic import Interaction
from System import Int64

ELEMENT_ID = 22043147
PROJECT_SCRIPT_DIR = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
try:
    text_type = unicode
except NameError:
    text_type = str


def number(element_id):
    return int(element_id.Value)


def xyz(point):
    return [point.X, point.Y, point.Z]


def snapshot(line):
    plane = line.SketchPlane
    geometry = plane.GetPlane() if plane else None
    curve = line.GeometryCurve
    return {
        "id": number(line.Id), "unique_id": line.UniqueId,
        "level_id": number(line.LevelId), "owner_view_id": number(line.OwnerViewId),
        "sketch_plane_id": number(plane.Id) if plane else None,
        "plane_origin_ft": xyz(geometry.Origin) if geometry else None,
        "plane_normal": xyz(geometry.Normal) if geometry else None,
        "curve_type": curve.GetType().FullName,
        "start_ft": xyz(curve.GetEndPoint(0)), "end_ft": xyz(curve.GetEndPoint(1)),
        "length_ft": curve.Length,
        "dependent_ids": [number(i) for i in line.GetDependentElements(None)]}


class CaptureFailures(DB.IFailuresPreprocessor):
    __namespace__ = "ReLevelRoomPlane.Run" + uuid.uuid4().hex

    def __init__(self, messages):
        self.messages = messages

    def PreprocessFailures(self, accessor):
        failures = list(accessor.GetFailureMessages())
        for failure in failures:
            self.messages.append({
                "description": failure.GetDescriptionText(),
                "severity": text_type(failure.GetSeverity()),
                "definition_id": text_type(failure.GetFailureDefinitionId().Guid),
                "failing_ids": [number(i) for i in failure.GetFailingElementIds()],
                "additional_ids": [number(i) for i in failure.GetAdditionalElementIds()]})
        # This isolated test must not resolve errors or accept changed constraints silently.
        return (DB.FailureProcessingResult.ProceedWithRollBack if failures
                else DB.FailureProcessingResult.Continue)


def main():
    uidoc = __revit__.ActiveUIDocument
    if uidoc is None:
        raise ValueError("Open a project first.")
    doc = uidoc.Document
    script_file = globals().get("__file__", "")
    folder = (os.path.dirname(os.path.abspath(script_file))
              if script_file and os.path.isfile(script_file) else PROJECT_SCRIPT_DIR)
    reports = os.path.join(folder, "reports")
    if not os.path.isdir(reports):
        os.makedirs(reports)
    path = os.path.join(reports, "room_plane_{0}_{1}_{2}.json".format(
        ELEMENT_ID, datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8]))
    data = {"timestamp_local": datetime.now().isoformat(), "document": doc.Title,
            "revit_version": doc.Application.VersionNumber,
            "revit_build": doc.Application.VersionBuild,
            "status": "NotStarted", "failures": [],
            "scope": "SketchPlane assignment only. Successful change kept. Units: feet."}
    try:
        if doc.IsReadOnly or doc.IsModifiable:
            raise ValueError("Requires a writable document without an open transaction.")
        line = doc.GetElement(DB.ElementId(Int64(ELEMENT_ID)))
        if (not isinstance(line, DB.ModelCurve) or line.Category is None
                or number(line.Category.Id) != int(DB.BuiltInCategory.OST_RoomSeparationLines)):
            raise ValueError("22043147 is not a room separation ModelCurve in this document.")
        if line.Pinned or line.GroupId != DB.ElementId.InvalidElementId or line.AssemblyInstanceId != DB.ElementId.InvalidElementId:
            raise ValueError("Pinned/grouped/assembled line is not supported by this test.")
        data["before"] = snapshot(line)
        levels = list(DB.FilteredElementCollector(doc).OfClass(DB.Level))
        for level in sorted(levels, key=lambda item: item.ProjectElevation):
            print("{0}: {1} ({2} ft)".format(number(level.Id), level.Name, level.ProjectElevation))
        answer = Interaction.InputBox(
            "COPY of model: change SketchPlane for room separator 22043147.\n"
            "Successful change is KEPT (Undo available).\n"
            "Different level elevation may move the line vertically.\n"
            "Enter target level ID or exact name; Cancel makes no changes.",
            "ReLevel - Room Separator SketchPlane", "").strip()
        if not answer:
            data["status"] = "Cancelled"
            return
        targets = [level for level in levels if text_type(number(level.Id)) == answer]
        if not targets:
            targets = [level for level in levels if level.Name == answer]
        if len(targets) != 1:
            raise ValueError("Target level is missing or ambiguous; use its numeric ID.")
        target = targets[0]
        data["target"] = {"id": number(target.Id), "name": target.Name,
                          "project_elevation_ft": target.ProjectElevation}
        if target.Id == line.LevelId:
            raise ValueError("Choose a different level.")
        transaction = DB.Transaction(doc, "ReLevel test: Room Separator SketchPlane")
        handler = CaptureFailures(data["failures"])
        try:
            if transaction.Start() != DB.TransactionStatus.Started:
                raise RuntimeError("Transaction did not start.")
            options = transaction.GetFailureHandlingOptions()
            options.SetFailuresPreprocessor(handler)
            options.SetClearAfterRollback(True)
            options.SetForcedModalHandling(True)
            transaction.SetFailureHandlingOptions(options)
            plane = DB.SketchPlane.Create(doc, target.Id)
            data["created_plane_id"] = number(plane.Id)
            line.SketchPlane = plane
            status = transaction.Commit()
            data["status"] = text_type(status)
        finally:
            # No model reads after an exception/regeneration failure: rollback only.
            if transaction.GetStatus() == DB.TransactionStatus.Started:
                rollback = transaction.RollBack()
                data["rollback_status"] = text_type(rollback)
                if rollback != DB.TransactionStatus.RolledBack:
                    raise RuntimeError("Rollback was not confirmed. Stop and check Revit.")
            if transaction.GetStatus() == DB.TransactionStatus.Pending:
                raise RuntimeError("Failure handling is pending. Stop and check Revit.")
            transaction.Dispose()
        if status == DB.TransactionStatus.Committed:
            data["after"] = snapshot(doc.GetElement(DB.ElementId(Int64(ELEMENT_ID))))
            print("Committed. Inspect the line and rooms. Use Undo to revert.")
        else:
            print("Not committed. See failures in the report.")
    except Exception:
        data["error"] = traceback.format_exc()
        print(data["error"])
    finally:
        with io.open(path, "w", encoding="utf-8") as stream:
            stream.write(text_type(json.dumps(data, ensure_ascii=False, indent=2)))
        print("Status: " + data["status"])
        print("Report: " + path)


if __name__ == "__main__":
    main()
