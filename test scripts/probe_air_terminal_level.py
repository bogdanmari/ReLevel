# -*- coding: utf-8 -*-
"""RevitPythonShell on a model copy. Level+offset before regeneration; ALWAYS rollback."""
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
from System import Int64, Double

try:
    text_type = unicode
except NameError:
    text_type = str
FOLDER = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
sys.path.insert(0, FOLDER)
try:
    from inspect_transfer_failures import snapshot
    from change_room_separator_plane import CaptureFailures
    from probe_area_boundary_levels import rollback
finally:
    sys.path.pop(0)


def eid(value):
    return DB.ElementId(Int64(value))


def connectors(element):
    manager = element.ConnectorManager if isinstance(element, DB.MEPCurve) else (
        element.MEPModel.ConnectorManager if isinstance(element, DB.FamilyInstance) and element.MEPModel else None)
    return list(manager.Connectors) if manager else []


def data_for(doc, value):
    data = snapshot(doc, value)
    element = doc.GetElement(eid(value))
    if element is None:
        return data
    location = element.Location
    def xyz(p):
        return [p.X, p.Y, p.Z]
    if isinstance(location, DB.LocationPoint):
        data["point_ft"] = xyz(location.Point)
    elif isinstance(location, DB.LocationCurve):
        data["curve_ft"] = [xyz(p) for p in location.Curve.Tessellate()]
    data["connectors"] = [{"id": c.Id, "origin_ft": xyz(c.Origin),
                           "refs": [{"owner_id": int(r.Owner.Id.Value), "connector_id": r.Id} for r in c.AllRefs]}
                          for c in connectors(element)]
    return data


def main():
    doc = __revit__.ActiveUIDocument.Document
    report = {"status": "NotStarted", "kept": False, "failures": [],
              "document": doc.Title, "revit_version": doc.Application.VersionNumber,
              "revit_build": doc.Application.VersionBuild}
    folder = os.path.join(FOLDER, "reports")
    if not os.path.isdir(folder):
        os.makedirs(folder)
    path = os.path.join(folder, "air_terminal_probe_{0}_{1}.json".format(
        datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8]))
    try:
        if doc.IsReadOnly or doc.IsModifiable:
            raise ValueError("Requires writable model copy with no open transaction.")
        terminal = doc.GetElement(eid(5889350))
        if terminal is None or terminal.UniqueId != "875e0b59-3a7f-466d-b408-da01e2aecfba-0059dd46":
            raise ValueError("Expected terminal 5889350 not found in this document.")
        level = terminal.get_Parameter(DB.BuiltInParameter.FAMILY_LEVEL_PARAM)
        offset = terminal.get_Parameter(DB.BuiltInParameter.INSTANCE_ELEVATION_PARAM)
        if terminal.Pinned or level is None or offset is None or level.IsReadOnly or offset.IsReadOnly:
            raise ValueError("Pinned terminal or unavailable level/offset.")
        source = doc.GetElement(level.AsElementId())
        levels = list(DB.FilteredElementCollector(doc).OfClass(DB.Level))
        for item in levels:
            print("{0}: {1}".format(item.Id.Value, item.Name))
        answer = Interaction.InputBox("Enter target level ID or exact name used in the failed transfer.\n"
                                      "All changes will be rolled back.", "ReLevel - Air Terminal", "").strip()
        if not answer:
            report["status"] = "Cancelled"
            return
        targets = [l for l in levels if text_type(l.Id.Value) == answer or l.Name == answer]
        if len(targets) != 1 or targets[0].Id == source.Id:
            raise ValueError("Choose a different existing level.")
        target = targets[0]
        new_offset = offset.AsDouble() + source.ProjectElevation - target.ProjectElevation
        report["target_id"] = int(target.Id.Value)
        report["new_offset_ft"] = new_offset
        watch = set([5889350, 5889122, 5889136])
        for value in list(watch):
            element = doc.GetElement(eid(value))
            if element:
                for connector in connectors(element):
                    for ref in connector.AllRefs:
                        if not isinstance(ref.Owner, DB.MEPSystem):
                            watch.add(int(ref.Owner.Id.Value))
        report["before"] = [data_for(doc, value) for value in sorted(watch)]
        group = DB.TransactionGroup(doc, "ReLevel air terminal probe - rollback")
        transaction = DB.Transaction(doc, "ReLevel air terminal level and offset")
        try:
            if group.Start() != DB.TransactionStatus.Started or transaction.Start() != DB.TransactionStatus.Started:
                raise RuntimeError("Could not start probe.")
            handler = CaptureFailures(report["failures"])
            options = transaction.GetFailureHandlingOptions()
            options.SetFailuresPreprocessor(handler)
            options.SetClearAfterRollback(True)
            options.SetForcedModalHandling(True)
            transaction.SetFailureHandlingOptions(options)
            if not level.Set(target.Id) or not offset.Set(Double(new_offset)):
                raise RuntimeError("Revit rejected level/offset.")
            status = transaction.Commit()
            report["status"] = text_type(status)
            if status == DB.TransactionStatus.Committed:
                report["after"] = [data_for(doc, value) for value in sorted(watch)]
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
        print("Group rollback: " + report.get("group_rollback", "not started"))
        print("Report: " + path)


if __name__ == "__main__":
    main()
