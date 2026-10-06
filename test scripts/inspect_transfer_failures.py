# -*- coding: utf-8 -*-
"""RevitPythonShell, Revit 2025+. Optional probe uses loaded ReLevel, then rolls back."""
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
from Autodesk.Revit.Exceptions import RegenerationFailedException
from Microsoft.VisualBasic import Interaction
from System import AppDomain, Array, Object, Int64
from System.Collections.Generic import List
from System.Reflection import BindingFlags

ELEMENT_IDS = [
    8253107, 8253108, 8253109, 8253110, 18657790, 18659055,
    22043123, 22043124, 22043125, 22043126, 22043127, 22043128,
    22043129, 22043130, 22043131, 22043132,
    22050973, 22050974, 22050975, 22050976, 22050981, 22051026,
    22051027, 22051081, 22051137, 22051161, 22051162, 22051628,
    22051677, 22051678, 22051966, 22052822, 22052823, 22052824,
    22052825, 22052826, 22052827, 22052828, 22052829, 22052830,
    22052831, 22059011, 22059018, 22059020, 22059021, 22059022,
    22059023, 22059024, 22059027, 22059028, 22059029, 22059030,
    22059031, 22059034, 22059035, 22059036, 22059037, 22059058,
    22059066, 22059067, 22059068, 22059069, 22059071, 22059073,
    22059074, 22059075, 22059076, 22059079, 22059089, 22059095,
    22059098, 22059101, 22059105, 22059106, 22059107, 22059108,
    22061432, 22061433, 22061434, 22061435, 22061436, 22061437,
    22061438, 22061439, 22061440, 22061441]
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


def eid(value):
    return DB.ElementId(Int64(value))


def ids(values):
    return [int(value.Value) for value in values]


def parameter_data(parameter):
    storage = parameter.StorageType
    value = None
    if storage == DB.StorageType.ElementId:
        value = int(parameter.AsElementId().Value)
    elif storage == DB.StorageType.Double:
        value = parameter.AsDouble()
    elif storage == DB.StorageType.Integer:
        value = parameter.AsInteger()
    elif storage == DB.StorageType.String:
        value = parameter.AsString()
    return {"id": int(parameter.Id.Value), "name": parameter.Definition.Name,
            "storage": str(storage), "value": value, "readonly": parameter.IsReadOnly}


def snapshot(doc, value):
    element = doc.GetElement(eid(value))
    if element is None:
        return {"id": value, "missing": True}
    return {"id": value, "class": element.GetType().FullName,
            "category": read(lambda: element.Category.Name),
            "name": read(lambda: element.Name), "level": int(element.LevelId.Value),
            "pinned": element.Pinned, "group": int(element.GroupId.Value),
            "parameters": [read(lambda p=p: parameter_data(p)) for p in element.Parameters],
            "joined": read(lambda: ids(DB.JoinGeometryUtils.GetJoinedElements(doc, element))),
            "dependents": read(lambda: ids(element.GetDependentElements(None)))}


def prop(instance, name):
    return instance.GetType().GetProperty(name).GetValue(instance, None)


def choose_level(levels, title):
    value = Interaction.InputBox("Level ID or exact name; Cancel = no probe", title, "").strip()
    if not value:
        return None
    matches = [level for level in levels
               if str(level.Id.Value) == value or level.Name.lower() == value.lower()]
    if len(matches) != 1:
        raise ValueError("Level not found or ambiguous: " + value)
    return matches[0].Id


def main():
    doc = __revit__.ActiveUIDocument.Document
    script_file = globals().get("__file__", "")
    folder = (os.path.dirname(os.path.abspath(script_file))
              if script_file and os.path.isfile(script_file) else PROJECT_SCRIPT_DIR)
    folder = os.path.join(folder, "reports")
    if not os.path.isdir(folder):
        os.makedirs(folder)
    path = os.path.join(folder, "transfer_failures_{0}_{1}.json".format(
        datetime.now().strftime("%Y%m%d_%H%M%S"), uuid.uuid4().hex[:8]))
    data = {"document": doc.Title, "revit_version": doc.Application.VersionNumber,
            "revit_build": doc.Application.VersionBuild, "requested_ids": ELEMENT_IDS,
            "units": "Revit internal units", "elements": [], "probe": []}
    try:
        levels = list(DB.FilteredElementCollector(doc).OfClass(DB.Level))
        data["levels"] = [{"id": int(l.Id.Value), "name": l.Name,
                           "project_elevation_ft": l.ProjectElevation} for l in levels]
        for level in data["levels"]:
            print("{id}: {name} ({project_elevation_ft} ft)".format(**level))
        for value in ELEMENT_IDS:
            data["elements"].append(read(lambda v=value: snapshot(doc, v)))
        data["warnings"] = [{"guid": str(f.GetFailureDefinitionId().Guid),
                             "text": f.GetDescriptionText(),
                             "ids": ids(f.GetFailingElements()),
                             "additional_ids": ids(f.GetAdditionalElements())}
                            for f in doc.GetWarnings()
                            if set(ids(f.GetFailingElements()) + ids(f.GetAdditionalElements()))
                            .intersection(ELEMENT_IDS)]
        source = choose_level(levels, "Source level used in ReLevel")
        target = choose_level(levels, "Target level used in ReLevel") if source else None
        if source is None or target is None:
            data["probe_status"] = "Not requested; read-only"
            return
        if source == target:
            raise ValueError("Source and target must differ.")
        assemblies = [a for a in AppDomain.CurrentDomain.GetAssemblies()
                      if a.GetName().Name == "ReLevel.Revit"]
        if len(assemblies) != 1:
            raise ValueError("Open and close ReLevel first. Exactly one loaded ReLevel assembly is required.")
        assembly = assemblies[0]
        data["plugin"] = {"path": assembly.Location, "identity": assembly.FullName,
                          "module_version_id": str(assembly.ManifestModule.ModuleVersionId)}
        data["source_id"], data["target_id"] = int(source.Value), int(target.Value)
        service_type = assembly.GetType("ReLevel.Revit.Transfer.ElementTransferService", True)
        flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
        constructors = service_type.GetConstructors(flags)
        service = constructors[0].Invoke(Array[Object]([doc]))
        execute = service_type.GetMethod("Execute", flags)
        # Each successful transaction remains until the end of this batch, matching the plugin.
        # The surrounding group ALWAYS rolls back, including after an exception/stopped report.
        group = DB.TransactionGroup(doc, "ReLevel diagnostic probe - rollback")
        try:
            if group.Start() != DB.TransactionStatus.Started:
                raise RuntimeError("Could not start diagnostic transaction group.")
            for value in ELEMENT_IDS:
                requested = List[DB.ElementId]()
                requested.Add(eid(value))
                report = execute.Invoke(service, Array[Object]([requested, target, source, None]))
                for item in prop(report, "Items"):
                    row = {"id": int(prop(item, "Id")), "status": str(prop(item, "Status")),
                           "reason": text_type(prop(item, "Reason"))}
                    data["probe"].append(row)
                    print("{id}: {status}: {reason}".format(**row))
                if prop(report, "Stopped"):
                    data["stopped"] = True
                    break  # No further model reads after critical failures.
        finally:
            try:
                if group.GetStatus() == DB.TransactionStatus.Started:
                    data["group_rollback"] = str(group.RollBack())
                    if data["group_rollback"] != "RolledBack":
                        raise RuntimeError("Diagnostic group rollback failed.")
            finally:
                group.Dispose()
    except Exception:
        data["error"] = traceback.format_exc()
        print(data["error"])
    finally:
        with io.open(path, "w", encoding="utf-8") as stream:
            stream.write(text_type(json.dumps(data, ensure_ascii=False, indent=2)))
        print("Report: " + path)


if __name__ == "__main__":
    main()
