# -*- coding: utf-8 -*-
"""RevitPythonShell, Revit 2025: run on a model copy. Successful change stays; Undo reverts it."""
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
from Autodesk.Revit.DB.Architecture import Railing
from Microsoft.VisualBasic import Interaction
from System import Int64

ELEMENT_ID = 18615150
EXPECTED_UNIQUE_ID = "dd3bd4d2-cd27-46f0-825e-724744bdcaf9-011c0b6e"
PROJECT_SCRIPT_DIR = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
BASE = DB.BuiltInParameter.STAIRS_RAILING_BASE_LEVEL_PARAM
OFFSET = DB.BuiltInParameter.STAIRS_RAILING_HEIGHT_OFFSET
try:
    text_type = unicode
except NameError:
    text_type = str


def number(element_id):
    return int(element_id.Value)


def snapshot(railing):
    base = railing.get_Parameter(BASE)
    offset = railing.get_Parameter(OFFSET)
    box = railing.get_BoundingBox(None)
    return {
        "id": number(railing.Id), "host_id": number(railing.HostId),
        "has_host": railing.HasHost, "level_id": number(railing.LevelId),
        "base_level_id": number(base.AsElementId()) if base and base.HasValue else None,
        "base_level_readonly": base.IsReadOnly if base else None,
        "base_offset_ft": offset.AsDouble() if offset and offset.HasValue else None,
        "bbox_ft": {"min": [box.Min.X, box.Min.Y, box.Min.Z],
                    "max": [box.Max.X, box.Max.Y, box.Max.Z]} if box else None,
        "dependent_ids": sorted(number(i) for i in railing.GetDependentElements(None))}


class CaptureFailures(DB.IFailuresPreprocessor):
    __namespace__ = "ReLevelRailing.Run" + uuid.uuid4().hex

    def __init__(self, messages):
        self.messages = messages

    def PreprocessFailures(self, accessor):
        failures = list(accessor.GetFailureMessages())
        for failure in failures:
            self.messages.append({
                "description": failure.GetDescriptionText(),
                "severity": text_type(failure.GetSeverity()),
                "failing_ids": [number(i) for i in failure.GetFailingElementIds()],
                "additional_ids": [number(i) for i in failure.GetAdditionalElementIds()]})
        return (DB.FailureProcessingResult.ProceedWithRollBack if failures
                else DB.FailureProcessingResult.Continue)


def main():
    uidoc = __revit__.ActiveUIDocument
    if uidoc is None:
        raise ValueError("Open a model copy first.")
    doc = uidoc.Document
    script_file = globals().get("__file__", "")
    folder = (os.path.dirname(os.path.abspath(script_file))
              if script_file and os.path.isfile(script_file) else PROJECT_SCRIPT_DIR)
    reports = os.path.join(folder, "reports")
    if not os.path.isdir(reports):
        os.makedirs(reports)
    path = os.path.join(reports, "railing_{0}_{1}_{2}.json".format(
        ELEMENT_ID, datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8]))
    data = {"document": doc.Title, "revit_version": doc.Application.VersionNumber,
            "revit_build": doc.Application.VersionBuild, "status": "NotStarted",
            "stage": "validation", "failures": []}
    try:
        if doc.IsReadOnly or doc.IsModifiable:
            raise ValueError("Requires a writable document without an open transaction.")
        railing = doc.GetElement(DB.ElementId(Int64(ELEMENT_ID)))
        if not isinstance(railing, Railing) or railing.UniqueId != EXPECTED_UNIQUE_ID:
            raise ValueError("Expected railing not found. Check the open model.")
        if not railing.HasHost:
            raise ValueError("No host. Undo the earlier RemoveHost test first.")
        if railing.Pinned or railing.GroupId != DB.ElementId.InvalidElementId or railing.AssemblyInstanceId != DB.ElementId.InvalidElementId:
            raise ValueError("Pinned/grouped/assembled railing is not supported.")
        host_id = railing.HostId
        if doc.GetElement(host_id) is None:
            raise ValueError("Host not found.")
        offset = railing.get_Parameter(OFFSET)
        if offset is None or offset.StorageType != DB.StorageType.Double or not offset.HasValue:
            raise ValueError("Base Offset is unavailable.")
        original_offset = offset.AsDouble()
        data["before"] = snapshot(railing)
        levels = list(DB.FilteredElementCollector(doc).OfClass(DB.Level))
        for level in sorted(levels, key=lambda item: item.ProjectElevation):
            print("{0}: {1} ({2} ft)".format(number(level.Id), level.Name, level.ProjectElevation))
        answer = Interaction.InputBox(
            "MODEL COPY: detach railing 18615150, change Base Level, restore host.\n"
            "Successful change stays in the model; Undo reverts it.\n"
            "Enter target level ID or exact name. Cancel makes no changes.",
            "ReLevel - Railing rehost test", "").strip()
        if not answer:
            data["status"] = "Cancelled"
            return
        targets = [level for level in levels if text_type(number(level.Id)) == answer]
        if not targets:
            targets = [level for level in levels if level.Name == answer]
        if len(targets) != 1:
            raise ValueError("Target missing or ambiguous. Use its numeric ID.")
        target = targets[0]
        if target.Id == railing.LevelId:
            raise ValueError("Choose a different level.")
        data["target"] = {"id": number(target.Id), "name": target.Name}
        tx = DB.Transaction(doc, "ReLevel test: railing level and host")
        handler = CaptureFailures(data["failures"])
        try:
            if tx.Start() != DB.TransactionStatus.Started:
                raise RuntimeError("Transaction did not start.")
            data["status"] = "Started"
            options = tx.GetFailureHandlingOptions()
            options.SetFailuresPreprocessor(handler)
            options.SetClearAfterRollback(True)
            options.SetForcedModalHandling(True)
            tx.SetFailureHandlingOptions(options)

            data["stage"] = "remove_host"
            railing.RemoveHost()
            doc.Regenerate()
            data["detached"] = snapshot(railing)
            base = railing.get_Parameter(BASE)
            if base is None or base.StorageType != DB.StorageType.ElementId or base.IsReadOnly:
                raise ValueError("Base Level is still unavailable for writing.")
            data["stage"] = "set_level"
            if not base.Set(target.Id):
                raise RuntimeError("Revit rejected Base Level.")
            doc.Regenerate()
            data["new_level_without_host"] = snapshot(railing)

            data["stage"] = "restore_host"
            railing.HostId = host_id
            doc.Regenerate()
            offset = railing.get_Parameter(OFFSET)
            if offset is None or offset.StorageType != DB.StorageType.Double or not offset.HasValue:
                raise ValueError("Base Offset is unavailable after rehosting.")
            if abs(offset.AsDouble() - original_offset) > 1e-9:
                if offset.IsReadOnly or not offset.Set(original_offset):
                    raise RuntimeError("Cannot restore original Base Offset.")
            doc.Regenerate()
            data["restored_before_commit"] = snapshot(railing)
            if not railing.HasHost or railing.HostId != host_id:
                raise RuntimeError("Original host was not restored.")
            # No Z correction: this test checks whether rehosting restores placement.
            data["stage"] = "commit"
            status = tx.Commit()
            data["status"] = text_type(status)
        finally:
            # After any exception (including regeneration failure), only roll back.
            if tx.GetStatus() == DB.TransactionStatus.Started:
                rollback = tx.RollBack()
                data["status"] = text_type(rollback)
                if rollback != DB.TransactionStatus.RolledBack:
                    raise RuntimeError("Rollback not confirmed. Stop and check Revit.")
            if tx.GetStatus() == DB.TransactionStatus.Pending:
                raise RuntimeError("Failure handling pending. Stop and check Revit.")
            tx.Dispose()
        if status == DB.TransactionStatus.Committed:
            data["stage"] = "read_committed_result"
            data["after"] = snapshot(doc.GetElement(DB.ElementId(Int64(ELEMENT_ID))))
            data["target_level_retained"] = data["after"]["level_id"] == number(target.Id)
            print("Committed. Target LevelId retained: {0}".format(data["target_level_retained"]))
            print("Inspect geometry and dependencies. A committed test is not proof of success.")
            print("Use Undo to revert. The script does not delete levels or hosts.")
        else:
            print("Not committed. See failures in report.")
    except Exception:
        data["error"] = traceback.format_exc()
        print(data["error"])
    finally:
        print("Transaction status: " + data["status"])
        with io.open(path, "w", encoding="utf-8") as stream:
            stream.write(text_type(json.dumps(data, ensure_ascii=False, indent=2)))
        print("Report: " + path)


if __name__ == "__main__":
    main()
