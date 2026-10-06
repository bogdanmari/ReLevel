# -*- coding: utf-8 -*-
"""Read-only RevitPythonShell report. No selection, transactions or deletion."""
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
from System.Collections.Generic import List

ELEMENT_IDS = [2774087, 2774088, 2774089, 2774090, 2774091, 2774092, 2774093,
               3545837, 21941486, 21941487, 21941488, 21941490, 21941491, 21941492,
               21941509, 21941510, 21941512, 21941513, 22043085, 22043089,
               22043099, 22043102, 22043108, 22043179, 22046887, 22047581,
               22051681, 22051682, 22051683, 22051685, 22051686, 22051687,
               22051704, 22051705, 22051707, 22051708]
PROJECT_SCRIPT_DIR = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
# Searches which model elements report these IDs as dependents. May take minutes.
SCAN_REVERSE_DEPENDENCIES = True
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
            "category": read(lambda: element.Category.Name if element.Category else None),
            "category_id": read(lambda: number(element.Category.Id) if element.Category else None),
            "level_id": read(lambda: number(element.LevelId)),
            "owner_view_id": read(lambda: number(element.OwnerViewId))}


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


def related_value(doc, value):
    if isinstance(value, DB.ElementId):
        return {"id": number(value), "element": describe(doc.GetElement(value))}
    if isinstance(value, DB.Element):
        return describe(value)
    return text_type(value) if value is not None else None


def element_data(doc, element):
    result = describe(element)
    for name in ("UniqueId", "Pinned", "ViewSpecific", "IsElementType"):
        if hasattr(element, name):
            result[name] = read(lambda name=name: getattr(element, name))
    result["parameters"] = read(lambda: parameters(doc, element))
    result["type"] = read(lambda: describe(doc.GetElement(element.GetTypeId())))
    for name in ("GroupId", "AssemblyInstanceId", "OwnerViewId", "LevelId",
                 "DesignOption", "Host", "HostId", "SuperComponent",
                 "SketchId", "OwnerId", "CreatedPhaseId", "DemolishedPhaseId"):
        def get_property(name=name):
            if not hasattr(element, name):
                return {"not_exposed": True}
            return related_value(doc, getattr(element, name))
        result[name] = read(get_property)
    result["workset_id"] = read(lambda: element.WorksetId.IntegerValue)
    result["dependent_elements"] = read(lambda: [read(lambda i=i: describe(doc.GetElement(i)))
        for i in element.GetDependentElements(None) if i != element.Id])

    def location():
        loc = element.Location
        if loc is None:
            return None
        data = {"class": loc.GetType().FullName}
        if isinstance(loc, DB.LocationPoint):
            data["point_ft"] = xyz(loc.Point)
        if isinstance(loc, DB.LocationCurve):
            data["curve"] = curve_data(doc, loc.Curve)
        return data
    result["location"] = read(location)

    def bounding_box():
        box = element.get_BoundingBox(None)
        if box is None:
            return None
        return {"min_ft": xyz(box.Min), "max_ft": xyz(box.Max),
                "origin_ft": xyz(box.Transform.Origin),
                "basis": [xyz(box.Transform.BasisX), xyz(box.Transform.BasisY), xyz(box.Transform.BasisZ)]}
    result["bounding_box"] = read(bounding_box)
    if isinstance(element, DB.SketchPlane):
        result["sketch_plane"] = read(lambda: plane_data(doc, element))
    if isinstance(element, DB.Sketch):
        result["sketch_plane"] = read(lambda: plane_data(doc, element.SketchPlane))
        result["profile"] = read(lambda: [[read(lambda curve=curve: curve_data(doc, curve))
            for curve in loop] for loop in element.Profile])
    if isinstance(element, DB.CurveElement):
        result["curve"] = read(lambda: curve_data(doc, element.GeometryCurve))
        result["sketch_plane"] = read(lambda: plane_data(doc, element.SketchPlane))
    if isinstance(element, DB.Dimension):
        result["references"] = read(lambda: [read(lambda ref=ref: reference_data(doc, ref))
            for ref in element.References])
        result["is_locked"] = read(lambda: element.IsLocked)
        result["dimension_curve"] = read(lambda: curve_data(doc, element.Curve))
    if isinstance(element, DB.View):
        result["view_type"] = read(lambda: text_type(element.ViewType))
        result["gen_level"] = read(lambda: describe(element.GenLevel))
    return result


def reverse_dependencies(doc, found_ids, data):
    """Record API dependency edges, not proof of ownership or deletion cascades."""
    reverse = data["reverse_dependencies"]
    reverse.update({"status": "running", "scanned": 0, "matches": [], "errors": []})
    ids = List[DB.ElementId]()
    for value in found_ids:
        ids.Add(DB.ElementId(Int64(value)))
    if not found_ids:
        reverse["status"] = "no_found_ids"
        return
    target_filter = DB.ElementIdSetFilter(ids)
    collector = DB.FilteredElementCollector(doc).WhereElementIsNotElementType()
    try:
        for candidate in collector:
            candidate_id = number(candidate.Id)
            reverse["scanned"] += 1
            try:
                matches = [number(i) for i in candidate.GetDependentElements(target_filter)
                           if number(i) != candidate_id]
                if matches:
                    reverse["matches"].append({"element": read(lambda: describe(candidate)),
                        "requested_dependent_ids": matches,
                        "parameters": read(lambda: parameters(doc, candidate))})
            except RegenerationFailedException:
                raise
            except Exception as error:
                reverse["errors"].append({"id": candidate_id, "error": text_type(error)})
            if reverse["scanned"] % 1000 == 0:
                print("Reverse dependency scan: {0} elements".format(reverse["scanned"]))
        reverse["status"] = "complete"
    finally:
        collector.Dispose()
        target_filter.Dispose()


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
    path = os.path.join(folder, "level0_remaining_{0}_{1}.json".format(
        datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8]))
    data = {"document": doc.Title, "document_path": doc.PathName,
            "project_unique_id": doc.ProjectInformation.UniqueId,
            "revit_version": doc.Application.VersionNumber, "revit_build": doc.Application.VersionBuild,
            "units": "feet", "status": "started", "requested_ids": ELEMENT_IDS,
            "scope": "Read-only active document; no selection, transfer, regeneration or trial deletion. "
                     "Reverse dependencies are API edges, not proven owners or deletion causes. "
                     "Reverse scan excludes element types and linked documents.",
            "elements": [], "missing_ids": [], "reverse_dependencies": {"status": "disabled"}}
    try:
        data["levels"] = [dict(describe(level), project_elevation_ft=level.ProjectElevation)
                          for level in DB.FilteredElementCollector(doc).OfClass(DB.Level)]
        found = []
        for value in ELEMENT_IDS:
            element = doc.GetElement(DB.ElementId(Int64(value)))
            if element is None:
                data["missing_ids"].append(value)
                print("{0}: NOT FOUND in active document".format(value))
            else:
                found.append(value)
                snapshot = read(lambda: element_data(doc, element))
                data["elements"].append({"requested_id": value, "snapshot": snapshot})
                print("{0}: {1} / {2}".format(value, snapshot.get("class", "read error"),
                                              snapshot.get("category", "")))
        if SCAN_REVERSE_DEPENDENCIES:
            print("Searching reverse dependencies. This can take several minutes.")
            reverse_dependencies(doc, found, data)
        data["status"] = "complete"
    except Exception:
        data["status"] = "failed_or_partial"
        data["error"] = traceback.format_exc()
        print(data["error"])
    finally:
        with io.open(path, "w", encoding="utf-8") as stream:
            stream.write(text_type(json.dumps(data, ensure_ascii=False, indent=2)))
        print("Status: " + data["status"])
        print("Report: " + path)


if __name__ == "__main__":
    main()
