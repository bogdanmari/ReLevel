# -*- coding: utf-8 -*-
"""Run the whole file in RevitPythonShell. Reports are UTF-8 JSON.

Select one wall, or leave selection empty to inspect 10925001.
An empty target-level input performs read-only inspection. A supplied target
enables a transfer probe on a COPY of the model, always rolled back.
Compatible syntax for IronPython 2.7 and Python 3; Revit 2025 API.
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
clr.AddReference("RevitAPIUI")
clr.AddReference("Microsoft.VisualBasic")
from Microsoft.VisualBasic import Interaction
from System import Int64
from Autodesk.Revit import DB
from Autodesk.Revit.Exceptions import RegenerationFailedException

DEFAULT_WALL_ID = 10925001
# Participants reported by Revit in the 2026-10-01 probe for this wall.
# Read BEFORE any transaction, including participants outside the first join hop.
FAILURE_PARTICIPANTS = {10925001: (21941484, 21941575, 22051679, 22051757)}
# Fallback for shell editors that do not define __file__.
PROJECT_SCRIPT_DIR = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
try:
    text_type = unicode
except NameError:
    text_type = str


def id_value(element_id):
    return int(element_id.Value)


def eid(number):
    return DB.ElementId(Int64(number))


def xyz(point):
    return [point.X, point.Y, point.Z]


def read(action):
    try:
        return action()
    except RegenerationFailedException:
        raise
    except Exception as error:
        return {"read_error": text_type(error)}


def describe(doc, element_id):
    element = doc.GetElement(element_id)
    if element is None:
        return {"id": id_value(element_id), "exists": False}
    return {"id": id_value(element_id), "class": element.GetType().FullName,
            "name": read(lambda: element.Name),
            "category": read(lambda: element.Category.Name if element.Category else None)}


def parameter_info(doc, parameter):
    storage = parameter.StorageType
    value = None
    if parameter.HasValue:
        if storage == DB.StorageType.ElementId:
            value = describe(doc, parameter.AsElementId())
        elif storage == DB.StorageType.Double:
            value = parameter.AsDouble()
        elif storage == DB.StorageType.Integer:
            value = parameter.AsInteger()
        elif storage == DB.StorageType.String:
            value = parameter.AsString()
    return {"id": id_value(parameter.Id), "name": parameter.Definition.Name,
            "storage": text_type(storage), "readonly": parameter.IsReadOnly,
            "has_value": parameter.HasValue, "value": value,
            "formatted": read(lambda: parameter.AsValueString())}


def snapshot(doc, element):
    data = describe(doc, element.Id)
    data.update({"unique_id": element.UniqueId, "pinned": element.Pinned,
                 "level": describe(doc, element.LevelId),
                 "type": describe(doc, element.GetTypeId()),
                 "group_id": id_value(element.GroupId),
                 "assembly_id": id_value(element.AssemblyInstanceId)})
    data["parameters"] = [read(lambda p=p: parameter_info(doc, p))
                          for p in element.Parameters]
    box = element.get_BoundingBox(None)
    data["bounding_box_ft"] = None if box is None else {
        "min": xyz(box.Min), "max": xyz(box.Max)}
    location = element.Location
    if isinstance(location, DB.LocationCurve):
        curve = location.Curve
        data["curve"] = {"class": curve.GetType().FullName,
                         "start_ft": read(lambda: xyz(curve.GetEndPoint(0))),
                         "end_ft": read(lambda: xyz(curve.GetEndPoint(1))),
                         "length_ft": read(lambda: curve.Length)}
    data["dependent_elements"] = read(lambda: [describe(doc, i)
                                               for i in element.GetDependentElements(None)])
    data["geometry_joins"] = read(lambda: [
        {"element": describe(doc, i), "this_element_cuts_other": read(
            lambda i=i: DB.JoinGeometryUtils.IsCuttingElementInJoin(doc, element, doc.GetElement(i)))}
        for i in DB.JoinGeometryUtils.GetJoinedElements(doc, element)])
    if isinstance(element, DB.Wall):
        data["wall_kind"] = text_type(element.WallType.Kind)
        data["cross_section"] = text_type(element.CrossSection)
        data["ends"] = []
        for end in (0, 1):
            data["ends"].append({
                "end": end,
                "join_allowed": read(lambda end=end: DB.WallUtils.IsWallJoinAllowedAtEnd(element, end)),
                "join_type": read(lambda end=end: text_type(location.get_JoinType(end))),
                "elements_at_join": read(lambda end=end: [describe(doc, e.Id)
                    for e in location.get_ElementsAtJoin(end)])})
    return data


def failing_ids(failure):
    # Document.GetWarnings returns FailureMessage; preprocessing supplies FailureMessageAccessor.
    return (failure.GetFailingElementIds() if hasattr(failure, "GetFailingElementIds")
            else failure.GetFailingElements())


def additional_ids(failure):
    return (failure.GetAdditionalElementIds() if hasattr(failure, "GetAdditionalElementIds")
            else failure.GetAdditionalElements())


def failure_info(failure):
    # Only failure accessor data here; do not inspect document geometry during failure handling.
    return {"description": failure.GetDescriptionText(),
            "severity": text_type(failure.GetSeverity()),
            "definition_id": text_type(failure.GetFailureDefinitionId().Guid),
            "failing_ids": [id_value(i) for i in failing_ids(failure)],
            "additional_ids": [id_value(i) for i in additional_ids(failure)]}


class CaptureFailures(DB.IFailuresPreprocessor):
    # A unique CLR namespace also permits repeated execution under pythonnet.
    __namespace__ = "ReLevelDiagnostics.Run" + uuid.uuid4().hex

    def __init__(self, messages):
        self.messages = messages

    def PreprocessFailures(self, accessor):
        messages = list(accessor.GetFailureMessages())
        self.messages.extend([failure_info(f) for f in messages])
        # Never resolve, delete warnings, unjoin, or accept a failing operation.
        if messages:
            return DB.FailureProcessingResult.ProceedWithRollBack
        return DB.FailureProcessingResult.Continue


def require_parameter(element, name, storage):
    p = element.get_Parameter(name)
    if p is None or not p.HasValue or p.StorageType != storage:
        raise ValueError("Missing parameter: " + text_type(name))
    return p


def probe(doc, wall, source, target, result):
    if doc.IsReadOnly or doc.IsModifiable:
        raise ValueError("Probe requires a writable document without an open transaction.")
    if source.Id == target.Id:
        raise ValueError("Source and target levels must differ.")
    # Match the current wall case's parameter order; no speculative unjoin or alternate strategies.
    pairs = [(DB.BuiltInParameter.WALL_BASE_CONSTRAINT, DB.BuiltInParameter.WALL_BASE_OFFSET),
             (DB.BuiltInParameter.WALL_HEIGHT_TYPE, DB.BuiltInParameter.WALL_TOP_OFFSET)]
    assignments = []
    for level_name, offset_name in pairs:
        p = require_parameter(wall, level_name, DB.StorageType.ElementId)
        if p.AsElementId() != source.Id:
            continue
        offset = require_parameter(wall, offset_name, DB.StorageType.Double)
        if p.IsReadOnly or offset.IsReadOnly or wall.Pinned:
            raise ValueError("Pinned wall or read-only level/offset.")
        assignments.append((p, offset, offset.AsDouble() + source.ProjectElevation - target.ProjectElevation))
    if not assignments:
        raise ValueError("Neither wall constraint references the chosen source level.")
    # This targeted probe does not reproduce the plugin's hosted-family restoration stage.
    hosted = [e for e in DB.FilteredElementCollector(doc).OfClass(DB.FamilyInstance)
              if e.Host is not None and e.Host.Id == wall.Id]
    if hosted:
        raise ValueError("Probe skipped: wall has hosted families. Read-only report is still available.")
    result["source_level"] = describe(doc, source.Id)
    result["target_level"] = describe(doc, target.Id)
    result["new_offsets_ft"] = [v for p, o, v in assignments]
    result["failures"] = []
    handler = CaptureFailures(result["failures"])
    group = DB.TransactionGroup(doc, "ReLevel diagnostic probe - rollback")
    transaction = DB.Transaction(doc, "ReLevel diagnostic wall transfer")
    try:
        if group.Start() != DB.TransactionStatus.Started:
            raise RuntimeError("TransactionGroup did not start.")
        if transaction.Start() != DB.TransactionStatus.Started:
            raise RuntimeError("Transaction did not start.")
        options = transaction.GetFailureHandlingOptions()
        options.SetFailuresPreprocessor(handler)
        options.SetClearAfterRollback(True)
        options.SetForcedModalHandling(True)
        transaction.SetFailureHandlingOptions(options)
        for level_parameter, offset_parameter, value in assignments:
            if not level_parameter.Set(target.Id) or not offset_parameter.Set(value):
                raise RuntimeError("Revit rejected a parameter assignment.")
        result["commit_status"] = text_type(transaction.Commit())
    finally:
        # After regeneration failure do rollback only, never read model state.
        try:
            if transaction.GetStatus() == DB.TransactionStatus.Started:
                result["transaction_rollback"] = text_type(transaction.RollBack())
            if transaction.GetStatus() == DB.TransactionStatus.Pending:
                raise RuntimeError("Failure processing is pending; check Revit before continuing.")
        finally:
            if group.GetStatus() == DB.TransactionStatus.Started:
                status = group.RollBack()
                result["group_rollback"] = text_type(status)
                if status != DB.TransactionStatus.RolledBack:
                    raise RuntimeError("Diagnostic group rollback was not confirmed.")
            transaction.Dispose()
            group.Dispose()


def main():
    uidoc = __revit__.ActiveUIDocument
    if uidoc is None:
        raise ValueError("Open a project first.")
    doc = uidoc.Document
    selected = list(uidoc.Selection.GetElementIds())
    if len(selected) > 1:
        raise ValueError("Select one wall, or clear selection for wall 10925001.")
    wall = doc.GetElement(selected[0] if selected else eid(DEFAULT_WALL_ID))
    if not isinstance(wall, DB.Wall):
        raise ValueError("Selected/default element is not a wall in this document.")
    script_file = globals().get("__file__", "")
    folder = os.path.dirname(os.path.abspath(script_file)) if script_file and os.path.isfile(script_file) else PROJECT_SCRIPT_DIR
    reports = os.path.join(folder, "reports")
    if not os.path.isdir(reports):
        os.makedirs(reports)
    filename = "wall_{0}_{1}_{2}.json".format(id_value(wall.Id), datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8])
    path = os.path.join(reports, filename)
    data = {"timestamp_local": datetime.now().isoformat(), "units": "feet / radians",
            "revit_version": doc.Application.VersionNumber, "revit_build": doc.Application.VersionBuild,
            "document": doc.Title, "document_path": doc.PathName,
            "scope": "Read-only snapshot; optional rolled-back probe. No unjoin."}
    try:
        data["wall"] = snapshot(doc, wall)
        neighbor_ids = set()
        for item in data["wall"].get("geometry_joins", []):
            if isinstance(item, dict) and "element" in item:
                neighbor_ids.add(item["element"]["id"])
        for end in data["wall"].get("ends", []):
            for item in end.get("elements_at_join", []):
                if isinstance(item, dict) and "id" in item:
                    neighbor_ids.add(item["id"])
        neighbor_ids.discard(id_value(wall.Id))
        data["neighbors"] = [read(lambda i=i: snapshot(doc, doc.GetElement(eid(i))))
                             for i in sorted(neighbor_ids)]
        participant_ids = FAILURE_PARTICIPANTS.get(id_value(wall.Id), ())
        data["known_failure_participants"] = [read(lambda i=i:
            snapshot(doc, doc.GetElement(eid(i))) if doc.GetElement(eid(i)) is not None
            else {"id": i, "exists": False}) for i in participant_ids]
        inspected_ids = set(participant_ids) | neighbor_ids | set([id_value(wall.Id)])
        data["existing_warnings"] = read(lambda: [failure_info(f) for f in doc.GetWarnings()
            if inspected_ids.intersection(id_value(i) for i in
                list(failing_ids(f)) + list(additional_ids(f)))])
        levels = list(DB.FilteredElementCollector(doc).OfClass(DB.Level))
        data["levels"] = [{"id": id_value(l.Id), "name": l.Name, "project_elevation_ft": l.ProjectElevation}
                          for l in sorted(levels, key=lambda l: l.ProjectElevation)]
        for l in data["levels"]:
            print("{0}: {1} ({2} ft)".format(l["id"], l["name"], l["project_elevation_ft"]))
        target_text = Interaction.InputBox(
            "Optional probe on a COPY of the model (always rolled back).\n"
            "Enter TARGET level ID from the shell listing.\n"
            "Leave empty / Cancel for read-only report.", "ReLevel wall diagnostics", "")
        if target_text.strip():
            target = doc.GetElement(eid(int(target_text)))
            base = require_parameter(wall, DB.BuiltInParameter.WALL_BASE_CONSTRAINT, DB.StorageType.ElementId)
            source_text = Interaction.InputBox("Enter SOURCE level ID used in ReLevel.",
                                               "ReLevel wall diagnostics", text_type(id_value(base.AsElementId())))
            if source_text.strip():
                source = doc.GetElement(eid(int(source_text)))
                if not isinstance(source, DB.Level) or not isinstance(target, DB.Level):
                    raise ValueError("Source/target must be existing levels.")
                data["probe"] = {}
                probe(doc, wall, source, target, data["probe"])
        # No model reads here: the probe may have encountered regeneration failure.
    except Exception:
        data["error"] = traceback.format_exc()
    finally:
        with io.open(path, "w", encoding="utf-8") as stream:
            stream.write(text_type(json.dumps(data, ensure_ascii=False, indent=2)))
        print("Report: " + path)
        if "error" in data:
            print(data["error"])


main()
