# -*- coding: utf-8 -*-
"""Read-only RevitPythonShell report for ALL Areas on source/target levels.
No view selection, transactions, creation or deletion. Revit 2025.
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

EXAMPLE_ID = 2219718
SOURCE_NAME = "LEVEL LL"
TARGET_NAME = "LEVEL 0"
FALLBACK = r"C:\Users\b.marishchenko\Documents\Programming\ReLevel\test scripts"
try:
    text_type = unicode
except NameError:
    text_type = str


def num(value):
    return int(value.Value)


def read(action):
    try:
        return action()
    except RegenerationFailedException:
        raise
    except Exception as error:
        return {"read_error": text_type(error)}


def xyz(point):
    return [point.X, point.Y, point.Z]


def describe(element):
    if element is None:
        return None
    return {"id": num(element.Id), "name": element.Name,
            "class": element.GetType().FullName,
            "category": element.Category.Name if element.Category else None,
            "level_id": num(element.LevelId), "owner_view_id": num(element.OwnerViewId)}


def parameter(p):
    val = None
    if p.HasValue:
        if p.StorageType == DB.StorageType.ElementId:
            val = num(p.AsElementId())
        elif p.StorageType == DB.StorageType.Double:
            val = p.AsDouble()
        elif p.StorageType == DB.StorageType.Integer:
            val = p.AsInteger()
        elif p.StorageType == DB.StorageType.String:
            val = p.AsString()
    return {"id": num(p.Id), "name": p.Definition.Name, "storage": text_type(p.StorageType),
            "readonly": p.IsReadOnly, "has_value": p.HasValue, "value": val,
            "shared_guid": text_type(p.GUID) if p.IsShared else None}


def curve_data(curve):
    return {"class": curve.GetType().FullName, "bound": curve.IsBound,
            "length_ft": read(lambda: curve.Length),
            "points_ft": read(lambda: [xyz(p) for p in curve.Tessellate()])}


def area_data(doc, area):
    result = describe(area)
    result.update({"unique_id": area.UniqueId, "scheme": describe(area.AreaScheme),
                   "area_sq_ft": area.Area, "pinned": area.Pinned,
                   "group_id": num(area.GroupId), "assembly_id": num(area.AssemblyInstanceId),
                   "design_option": describe(area.DesignOption),
                   "parameters": [read(lambda p=p: parameter(p)) for p in area.Parameters],
                   "point_ft": xyz(area.Location.Point) if isinstance(area.Location, DB.LocationPoint) else None,
                   "dependent_elements": read(lambda: [describe(doc.GetElement(i))
                       for i in area.GetDependentElements(None)])})
    def boundaries():
        options = DB.SpatialElementBoundaryOptions()
        try:
            loops = area.GetBoundarySegments(options)
            if loops is None:
                return None
            return [[{"element": describe(doc.GetElement(segment.ElementId)),
                      "curve": curve_data(segment.GetCurve())} for segment in loop] for loop in loops]
        finally:
            options.Dispose()
    result["boundary_loops"] = read(boundaries)
    return result


def main():
    uidoc = __revit__.ActiveUIDocument
    if uidoc is None:
        raise ValueError("Open the project first.")
    doc = uidoc.Document
    script_file = globals().get("__file__", "")
    folder = os.path.dirname(os.path.abspath(script_file)) if script_file and os.path.isfile(script_file) else FALLBACK
    folder = os.path.join(folder, "reports")
    if not os.path.isdir(folder):
        os.makedirs(folder)
    path = os.path.join(folder, "areas_{0}_{1}.json".format(datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8]))
    data = {"document": doc.Title, "revit_version": doc.Application.VersionNumber,
            "revit_build": doc.Application.VersionBuild, "scope": "Read-only; all Areas on source and target levels",
            "units": "feet / square feet", "example_id": EXAMPLE_ID}
    try:
        levels = list(DB.FilteredElementCollector(doc).OfClass(DB.Level))
        data["levels"] = [dict(describe(l), project_elevation_ft=l.ProjectElevation) for l in levels]
        def resolve(name):
            matches = [l for l in levels if l.Name.strip().lower() == name.lower()]
            if len(matches) != 1:
                raise ValueError("Missing/ambiguous level: " + name)
            return matches[0]
        source, target = resolve(SOURCE_NAME), resolve(TARGET_NAME)
        data["source_level_id"], data["target_level_id"] = num(source.Id), num(target.Id)
        chosen = set([num(source.Id), num(target.Id)])
        plans = [v for v in DB.FilteredElementCollector(doc).OfClass(DB.ViewPlan)
                 if not v.IsTemplate and v.ViewType == DB.ViewType.AreaPlan
                 and v.GenLevel is not None and num(v.GenLevel.Id) in chosen]
        data["area_plans"] = [dict(describe(v), scheme=describe(v.AreaScheme),
            gen_level_id=num(v.GenLevel.Id)) for v in plans]
        # Area has no native class filter; collect SpatialElement then filter managed types.
        areas = [a for a in DB.FilteredElementCollector(doc).OfClass(DB.SpatialElement)
                 if isinstance(a, DB.Area) and (num(a.LevelId) in chosen or num(a.Id) == EXAMPLE_ID)]
        data["example"] = describe(doc.GetElement(DB.ElementId(Int64(EXAMPLE_ID))))
        data["areas"] = [read(lambda a=a: area_data(doc, a)) for a in areas]
        area_ids = set(num(a.Id) for a in areas)
        def tag_data(tag):
            item = describe(tag)
            item["area_id"] = num(tag.Area.Id) if tag.Area is not None else None
            item["parameters"] = [read(lambda p=p: parameter(p)) for p in tag.Parameters]
            return item
        tags = [read(lambda t=t: tag_data(t)) for t in DB.FilteredElementCollector(doc)
                .OfCategory(DB.BuiltInCategory.OST_AreaTags).WhereElementIsNotElementType()]
        data["area_tags"] = [t for t in tags if "read_error" in t or t.get("area_id") in area_ids]
        source_schemes = set(num(a.AreaScheme.Id) for a in areas if a.LevelId == source.Id)
        data["source_schemes_without_target_plan"] = sorted(source_schemes - set(
            num(v.AreaScheme.Id) for v in plans if v.GenLevel.Id == target.Id))
        print("Areas inspected: {0}; area plans: {1}".format(len(areas), len(plans)))
    except Exception:
        data["error"] = traceback.format_exc()
        print(data["error"])
    finally:
        with io.open(path, "w", encoding="utf-8") as stream:
            stream.write(text_type(json.dumps(data, ensure_ascii=False, indent=2)))
        print("Report: " + path)


if __name__ == "__main__":
    main()
