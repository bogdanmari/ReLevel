# -*- coding: utf-8 -*-
"""RevitPythonShell: change hanger host level on a model copy; ALWAYS rollback."""
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

FOLDER = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
sys.path.insert(0, FOLDER)
try:
    from inspect_fabrication_hanger import describe, hosted_data, rod_data, xyz, text_type
    from change_room_separator_plane import CaptureFailures
    from probe_area_boundary_levels import rollback
finally:
    sys.path.pop(0)


def eid(value):
    return DB.ElementId(Int64(value))


def capture(doc, ids):
    result = []
    for value in sorted(ids):
        part = doc.GetElement(eid(value))
        if part is None:
            result.append({"id": value, "missing": True})
            continue
        data = describe(part)
        if isinstance(part, DB.FabricationPart):
            data["hosted_info"] = hosted_data(doc, part)
            if part.Category.Id == eid(int(DB.BuiltInCategory.OST_FabricationHangers)):
                data["rod_info"] = rod_data(part)
            data["connectors"] = sorted([
                {"id": c.Id, "origin_ft": xyz(c.Origin),
                 "refs": sorted([[int(r.Owner.Id.Value), r.Id] for r in c.AllRefs])}
                for c in part.ConnectorManager.Connectors], key=lambda item: item["id"])
            data["dependent_ids"] = sorted(int(i.Value) for i in part.GetDependentElements(None))
        result.append(data)
    return result


def main():
    doc = __revit__.ActiveUIDocument.Document
    report = {"status": "NotStarted", "kept": False, "failures": [],
              "document": doc.Title, "revit_version": doc.Application.VersionNumber,
              "revit_build": doc.Application.VersionBuild,
              "scope": "Only host 6866535 FABRICATION_LEVEL_PARAM; no detach; always rollback"}
    folder = os.path.join(FOLDER, "reports")
    if not os.path.isdir(folder):
        os.makedirs(folder)
    path = os.path.join(folder, "fabrication_hanger_probe_{0}_{1}.json".format(
        datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8]))
    try:
        if doc.IsReadOnly or doc.IsModifiable:
            raise ValueError("Requires writable model copy with no open transaction.")
        hanger = doc.GetElement(eid(7377791))
        host = doc.GetElement(eid(6866535))
        for part, unique_id in [(hanger, "9686ec06-0bc8-4b5f-bcb5-08a764c03d6f-0070937f"),
                                (host, "47aafe97-cd1d-413a-ba2e-3b2b44566f1f-0068c371")]:
            if not isinstance(part, DB.FabricationPart) or part.UniqueId != unique_id:
                raise ValueError("Expected hanger and pipe host not found in this document.")
            if part.Pinned or part.GroupId != DB.ElementId.InvalidElementId or part.AssemblyInstanceId != DB.ElementId.InvalidElementId or part.DesignOption:
                raise ValueError("Pinned, grouped, assembled or design-option part is unsupported.")
        info = hosted_data(doc, hanger)
        if info is None or info["host_id"] != 6866535:
            raise ValueError("Hanger host has changed.")
        level = host.get_Parameter(DB.BuiltInParameter.FABRICATION_LEVEL_PARAM)
        if level is None or level.IsReadOnly or not level.HasValue or level.StorageType != DB.StorageType.ElementId:
            raise ValueError("Host Reference Level is unavailable.")
        source = doc.GetElement(level.AsElementId())
        if not isinstance(source, DB.Level) or hanger.LevelId != source.Id:
            raise ValueError("Expected host and hanger on the same existing level.")
        levels = list(DB.FilteredElementCollector(doc).OfClass(DB.Level))
        for item in levels:
            print("{0}: {1} ({2} ft)".format(item.Id.Value, item.Name, item.ProjectElevation))
        answer = Interaction.InputBox("Enter target level ID or exact name.\n"
                                      "Only pipe host level will be set. ALL changes will be rolled back.",
                                      "ReLevel - Fabrication hanger probe", "").strip()
        if not answer:
            report["status"] = "Cancelled"
            return
        targets = [item for item in levels if text_type(item.Id.Value) == answer or item.Name == answer]
        if len(targets) != 1 or targets[0].Id == source.Id:
            raise ValueError("Choose a different existing level.")
        target = targets[0]
        report["source_id"] = int(source.Id.Value)
        report["target_id"] = int(target.Id.Value)
        report["elevation_difference_ft"] = target.ProjectElevation - source.ProjectElevation
        watch = set([7377791, 6866535])
        # Include other hangers of this host and its immediate connector neighbours.
        collector = DB.FilteredElementCollector(doc).OfCategory(DB.BuiltInCategory.OST_FabricationHangers).WhereElementIsNotElementType()
        try:
            for part in collector:
                if isinstance(part, DB.FabricationPart):
                    hosted = part.GetHostedInfo()
                    if hosted is not None:
                        try:
                            if hosted.HostId == host.Id:
                                watch.add(int(part.Id.Value))
                        finally:
                            hosted.Dispose()
        finally:
            collector.Dispose()
        for c in host.ConnectorManager.Connectors:
            for ref in c.AllRefs:
                if not isinstance(ref.Owner, DB.MEPSystem):
                    watch.add(int(ref.Owner.Id.Value))
        report["before"] = capture(doc, watch)
        group = DB.TransactionGroup(doc, "ReLevel hanger host probe - rollback")
        transaction = DB.Transaction(doc, "ReLevel change fabrication host level")
        try:
            if group.Start() != DB.TransactionStatus.Started or transaction.Start() != DB.TransactionStatus.Started:
                raise RuntimeError("Could not start probe.")
            handler = CaptureFailures(report["failures"])
            options = transaction.GetFailureHandlingOptions()
            options.SetFailuresPreprocessor(handler)
            options.SetClearAfterRollback(True)
            options.SetForcedModalHandling(True)
            transaction.SetFailureHandlingOptions(options)
            if not level.Set(target.Id):
                raise RuntimeError("Revit rejected host level.")
            status = transaction.Commit()
            report["status"] = text_type(status)
            if status == DB.TransactionStatus.Committed:
                report["after"] = capture(doc, watch)
                report["hanger_on_target"] = doc.GetElement(eid(7377791)).LevelId == target.Id
        finally:
            try:
                rollback(transaction, "transaction_rollback", report)
            finally:
                try:
                    rollback(group, "group_rollback", report)
                finally:
                    transaction.Dispose()
                    group.Dispose()
    except Exception as error:
        report["status"] = "Error"
        report["error"] = text_type(error)
        report["traceback"] = traceback.format_exc()
    finally:
        with io.open(path, "w", encoding="utf-8") as stream:
            stream.write(text_type(json.dumps(report, ensure_ascii=False, indent=2)))
        print("Status: " + report["status"])
        print("Hanger on target: " + text_type(report.get("hanger_on_target", "not checked")))
        print("Group rollback: " + report.get("group_rollback", "not started"))
        print("Report: " + path)


if __name__ == "__main__":
    main()
