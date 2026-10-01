# -*- coding: utf-8 -*-
"""Read-only report for the 26 Model Lines from the deletion warning.
Run the entire file in RevitPythonShell, Revit 2025. No selection required.
No transactions, regeneration, deletion or transfer are performed.
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
from Autodesk.Revit import DB
from Autodesk.Revit.Exceptions import RegenerationFailedException
from System import Int64

ELEMENT_IDS = [2219623, 3124956, 3124992, 3131655,
               3545808, 3545809, 3545810, 3545811, 3545812,
               6407169, 6407170, 6407171, 6407172, 6407173,
               6407174, 6407175, 6407177, 6407178,
               7354251, 7354252, 7354253, 7354254,
               10887049, 10887053, 10887054, 10887055]
PROJECT_SCRIPT_DIR = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
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


def number(value):
    return int(value.Value)


def xyz(point):
    return [point.X, point.Y, point.Z]


def describe(element):
    if element is None:
        return None
    return {"id": number(element.Id), "name": read(lambda: element.Name),
            "class": element.GetType().FullName,
            "category": element.Category.Name if element.Category else None,
            "category_id": number(element.Category.Id) if element.Category else None,
            "level_id": number(element.LevelId),
            "owner_view_id": number(element.OwnerViewId)}


def parameter(doc, p):
    value, referenced = None, None
    if p.HasValue:
        if p.StorageType == DB.StorageType.ElementId:
            value = number(p.AsElementId())
            referenced = read(lambda: describe(doc.GetElement(p.AsElementId())))
        elif p.StorageType == DB.StorageType.Double:
            value = p.AsDouble()
        elif p.StorageType == DB.StorageType.Integer:
            value = p.AsInteger()
        elif p.StorageType == DB.StorageType.String:
            value = p.AsString()
    return {"id": number(p.Id), "name": p.Definition.Name,
            "storage": text_type(p.StorageType), "readonly": p.IsReadOnly,
            "has_value": p.HasValue, "value": value, "referenced_element": referenced,
            "display": read(lambda: p.AsValueString()),
            "data_type": read(lambda: p.Definition.GetDataType().TypeId),
            "shared_guid": text_type(p.GUID) if p.IsShared else None}


def parameters(doc, element):
    return [read(lambda p=p: parameter(doc, p)) for p in element.Parameters]


def reference_data(doc, reference):
    if reference is None:
        return None
    return {"element": describe(doc.GetElement(reference.ElementId)),
            "linked_element_id": number(reference.LinkedElementId),
            "stable": read(lambda: reference.ConvertToStableRepresentation(doc))}


def curve_data(doc, curve):
    result = {"class": curve.GetType().FullName, "is_bound": curve.IsBound,
              "length_ft": read(lambda: curve.Length),
              "points_ft": read(lambda: [xyz(p) for p in curve.Tessellate()]),
              "reference": read(lambda: reference_data(doc, curve.Reference))}
    if curve.IsBound:
        result["endpoints_ft"] = [xyz(curve.GetEndPoint(i)) for i in (0, 1)]
    return result


def plane_data(doc, plane):
    if plane is None:
        return None
    result = describe(plane)
    result["parameters"] = parameters(doc, plane)
    def geometry():
        p = plane.GetPlane()
        return {"origin_ft": xyz(p.Origin), "normal": xyz(p.Normal),
                "x_vector": xyz(p.XVec), "y_vector": xyz(p.YVec)}
    result["geometry"] = read(geometry)
    result["plane_reference"] = read(lambda: reference_data(doc, plane.GetPlaneReference()))
    return result


def line_data(doc, element):
    result = describe(element)
    result.update({"unique_id": element.UniqueId, "pinned": element.Pinned,
                   "group_id": number(element.GroupId),
                   "assembly_id": number(element.AssemblyInstanceId),
                   "design_option": read(lambda: describe(element.DesignOption)),
                   "view_specific": element.ViewSpecific,
                   "parameters": parameters(doc, element),
                   "type": read(lambda: describe(doc.GetElement(element.GetTypeId())))})
    # Keep only dependency IDs; dimensions are outside this investigation.
    result["dependent_ids"] = read(lambda: [number(i) for i in element.GetDependentElements(None)])
    result["owner_view"] = read(lambda: describe(doc.GetElement(element.OwnerViewId)))
    if isinstance(element, DB.CurveElement):
        result["curve"] = read(lambda: curve_data(doc, element.GeometryCurve))
        result["sketch_plane"] = read(lambda: plane_data(doc, element.SketchPlane))
        result["line_style"] = read(lambda: describe(element.LineStyle))
        result["line_style_category"] = read(lambda: {
            "id": number(element.LineStyle.GraphicsStyleCategory.Id),
            "name": element.LineStyle.GraphicsStyleCategory.Name}
            if element.LineStyle is not None else None)
        result["curve_element_type"] = read(lambda: text_type(element.CurveElementType))
    else:
        result["unexpected_type"] = "Requested ID is not a CurveElement."
    return result


def main():
    uidoc = __revit__.ActiveUIDocument
    if uidoc is None:
        raise ValueError("Open the source project first.")
    doc = uidoc.Document
    script_file = globals().get("__file__", "")
    folder = (os.path.dirname(os.path.abspath(script_file))
              if script_file and os.path.isfile(script_file) else PROJECT_SCRIPT_DIR)
    folder = os.path.join(folder, "reports")
    if not os.path.isdir(folder):
        os.makedirs(folder)
    path = os.path.join(folder, "model_lines_{0}_{1}.json".format(
        datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8]))
    data = {"document": doc.Title, "revit_version": doc.Application.VersionNumber,
            "revit_build": doc.Application.VersionBuild, "units": "feet",
            "scope": "Read-only; explicit line IDs in the active document; no dimension inspection",
            "requested_ids": ELEMENT_IDS, "elements": [], "missing_ids": []}
    try:
        data["levels"] = [dict(describe(level), project_elevation_ft=level.ProjectElevation)
                          for level in DB.FilteredElementCollector(doc).OfClass(DB.Level)]
        for value in ELEMENT_IDS:
            element = doc.GetElement(DB.ElementId(Int64(value)))
            if element is None:
                data["missing_ids"].append(value)
            else:
                data["elements"].append({"requested_id": value,
                                         "snapshot": read(lambda: line_data(doc, element))})
        print("Lines inspected: {0}; missing IDs: {1}".format(
            len(data["elements"]), data["missing_ids"]))
    except Exception:
        data["error"] = traceback.format_exc()
        print(data["error"])
    finally:
        with io.open(path, "w", encoding="utf-8") as stream:
            stream.write(text_type(json.dumps(data, ensure_ascii=False, indent=2)))
        print("Report: " + path)


if __name__ == "__main__":
    main()
