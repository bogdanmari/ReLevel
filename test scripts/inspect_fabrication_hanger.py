# -*- coding: utf-8 -*-
"""Read-only RevitPythonShell diagnosis of hanger 7377791. No transactions."""
from __future__ import print_function
import io
import json
import os
import traceback
import uuid
from datetime import datetime
import clr
clr.AddReference("RevitAPI")
from Autodesk.Revit import DB
from Autodesk.Revit.Exceptions import RegenerationFailedException
from System import Int64

FOLDER = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
UNIQUE_ID = "9686ec06-0bc8-4b5f-bcb5-08a764c03d6f-0070937f"
try:
    text_type = unicode
except NameError:
    text_type = str


def read(action):
    try:
        return action()
    except RegenerationFailedException:
        raise
    except Exception as error:
        return {"read_error": text_type(error)}


def xyz(point):
    return [point.X, point.Y, point.Z]


def level_parameter(element):
    p = element.get_Parameter(DB.BuiltInParameter.FABRICATION_LEVEL_PARAM)
    if p is None:
        return None
    return {"readonly": p.IsReadOnly, "has_value": p.HasValue,
            "storage": text_type(p.StorageType),
            "level_id": int(p.AsElementId().Value) if p.HasValue and
            p.StorageType == DB.StorageType.ElementId else None}


def describe(element):
    if element is None:
        return None
    result = {"id": int(element.Id.Value), "unique_id": element.UniqueId,
              "class": element.GetType().FullName, "name": element.Name,
              "category": element.Category.Name if element.Category else None,
              "level_id": int(element.LevelId.Value), "pinned": element.Pinned,
              "group_id": int(element.GroupId.Value),
              "assembly_id": int(element.AssemblyInstanceId.Value),
              "design_option_id": int(element.DesignOption.Id.Value) if element.DesignOption else None,
              "reference_level": read(lambda: level_parameter(element))}
    if isinstance(element, DB.FabricationPart):
        result["origin_ft"] = read(lambda: xyz(element.Origin))
        result["level_offset_ft"] = read(lambda: element.LevelOffset)
    return result


def hosted_data(doc, part):
    info = part.GetHostedInfo()
    if info is None:
        return None
    try:
        return {"host_id": int(info.HostId.Value),
                "host": describe(doc.GetElement(info.HostId))}
    finally:
        info.Dispose()


def attachment(info, index):
    attached = info.GetRodAttachedElementId(index)
    return {"host_element_id": int(attached.HostElementId.Value),
            "link_instance_id": int(attached.LinkInstanceId.Value),
            "linked_element_id": int(attached.LinkedElementId.Value)}


def rod_data(part):
    info = part.GetRodInfo()
    if info is None:
        return None
    try:
        return {"count": info.RodCount,
                "can_be_hosted": info.CanRodsBeHosted,
                "attached_to_structure": info.IsAttachedToStructure,
                "rods": [{"index": i,
                          "length_ft": read(lambda: info.GetRodLength(i)),
                          "end_ft": read(lambda: xyz(info.GetRodEndPosition(i))),
                          "attachment": read(lambda: attachment(info, i))}
                         for i in range(info.RodCount)]}
    finally:
        info.Dispose()


def main():
    doc = __revit__.ActiveUIDocument.Document
    report = {"document": doc.Title, "revit_version": doc.Application.VersionNumber,
              "revit_build": doc.Application.VersionBuild, "read_only": True}
    try:
        part = doc.GetElement(DB.ElementId(Int64(7377791)))
        if not isinstance(part, DB.FabricationPart) or part.UniqueId != UNIQUE_ID:
            raise ValueError("Open the model containing the inspected hanger 7377791.")
        report["hanger"] = describe(part)
        report["hosted_info"] = read(lambda: hosted_data(doc, part))
        report["rod_info"] = read(lambda: rod_data(part))
        report["source_level"] = describe(doc.GetElement(part.LevelId))
    except Exception:
        report["error"] = traceback.format_exc()
    finally:
        folder = os.path.join(FOLDER, "reports")
        if not os.path.isdir(folder):
            os.makedirs(folder)
        path = os.path.join(folder, "fabrication_hanger_{0}_{1}.json".format(
            datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8]))
        with io.open(path, "w", encoding="utf-8") as stream:
            stream.write(text_type(json.dumps(report, ensure_ascii=False, indent=2)))
        print(json.dumps(report, ensure_ascii=True, indent=2))
        print("Report: " + path)


if __name__ == "__main__":
    main()
