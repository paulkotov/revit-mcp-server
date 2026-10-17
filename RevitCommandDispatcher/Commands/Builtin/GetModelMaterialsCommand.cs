using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitCommandDispatcher.Commands.Builtin
{
    /// <summary>
    /// Sums every material used by placed elements in the active document, one bucket per level.
    /// Paint is included. Rebar is measured by bar length so it is not lost inside the host volume.
    /// </summary>
    public sealed class GetModelMaterialsCommand : IRevitCommand
    {
        private const double VolumeEpsilon = 0.0001; // m3
        private const double AreaEpsilon = 0.0001; // m2
        private const double LengthEpsilon = 0.01; // m

        private static readonly ElementId[] NoMaterialIds = Array.Empty<ElementId>();

        public string Method => "revit.getModelMaterials";
        public bool RequiresTransaction => false;
        public bool RequiresDocument => true;

        public object Execute(UIApplication app, JToken parameters)
        {
            var doc = app.ActiveUIDocument.Document;
            var buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType();

            foreach (var element in collector)
            {
                if (element.OwnerViewId != null && element.OwnerViewId != ElementId.InvalidElementId)
                    continue;

                var level = ResolveLevel(doc, element);
                if (element.Category != null && element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Rebar)
                {
                    if (AddRebar(doc, element, level, buckets))
                        continue;
                }

                AddMaterialQuantities(doc, element, level, buckets);
            }

            var levels = buckets.Values
                .GroupBy(bucket => bucket.LevelName)
                .Select(group => new
                {
                    name = group.Key,
                    elevationM = group.First().ElevationM,
                    materials = group
                        .OrderBy(item => item.Name)
                        .Select(item => new
                        {
                            name = item.Name,
                            materialClass = item.MaterialClass,
                            volumeM3 = Round(item.VolumeM3, 3),
                            areaM2 = Round(item.AreaM2, 3),
                            lengthM = Round(item.LengthM, 2)
                        })
                        .ToArray()
                })
                .OrderBy(level => level.elevationM ?? double.MaxValue)
                .ThenBy(level => level.name)
                .ToArray();

            return new
            {
                projectName = string.IsNullOrWhiteSpace(doc.Title) ? "Untitled" : doc.Title,
                projectPath = doc.PathName ?? string.Empty,
                units = new { volume = "m3", area = "m2", length = "m", elevation = "m" },
                levels
            };
        }

        private static void AddMaterialQuantities(
            Document doc,
            Element element,
            Level level,
            Dictionary<string, Bucket> buckets)
        {
            var bodyIds = MaterialIds(element, paint: false);
            var paintIds = MaterialIds(element, paint: true);
            var bodyLookup = IdSet(bodyIds);
            var paintLookup = IdSet(paintIds);
            var seen = new HashSet<int>();

            foreach (var materialId in bodyIds.Concat(paintIds))
            {
                if (!seen.Add(materialId.IntegerValue))
                    continue;

                var material = doc.GetElement(materialId) as Material;
                if (material == null)
                    continue;

                double volume = 0;
                double area = 0;
                var read = false;

                if (bodyLookup.Contains(materialId.IntegerValue))
                {
                    try
                    {
                        volume = RevitUnits.ToCubicMeters(element.GetMaterialVolume(materialId));
                        area += RevitUnits.ToSquareMeters(element.GetMaterialArea(materialId, false));
                        read = true;
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                    }
                }

                if (paintLookup.Contains(materialId.IntegerValue))
                {
                    try
                    {
                        area += RevitUnits.ToSquareMeters(element.GetMaterialArea(materialId, true));
                        read = true;
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                    }
                }

                if (!read)
                    continue;
                if (volume < VolumeEpsilon && area < AreaEpsilon)
                    continue;

                var bucket = GetBucket(buckets, level, material.Id.IntegerValue.ToString(), material.Name, material.MaterialClass);
                bucket.VolumeM3 += volume;
                bucket.AreaM2 += area;
            }
        }

        /// <summary>
        /// Revit returns body materials and paint materials from separate calls.
        /// </summary>
        private static IList<ElementId> MaterialIds(Element element, bool paint)
        {
            try
            {
                var ids = element.GetMaterialIds(paint);
                return ids == null || ids.Count == 0 ? NoMaterialIds : ids.ToList();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return NoMaterialIds;
            }
        }

        private static HashSet<int> IdSet(IList<ElementId> ids)
        {
            var set = new HashSet<int>();
            foreach (var id in ids)
                set.Add(id.IntegerValue);
            return set;
        }

        /// <summary>Returns false when the element is not a <see cref="Rebar"/> instance.</summary>
        private static bool AddRebar(
            Document doc,
            Element element,
            Level level,
            Dictionary<string, Bucket> buckets)
        {
            var rebar = element as Rebar;
            if (rebar == null)
                return false;

            var barCount = CountBars(rebar);
            var length = RevitUnits.ToMeters(rebar.TotalLength) * barCount;
            if (length < LengthEpsilon)
                return true;

            var barType = doc.GetElement(rebar.GetTypeId()) as RebarBarType;
            var name = barType != null && !string.IsNullOrWhiteSpace(barType.Name)
                ? barType.Name
                : element.Name;

            var bucket = GetBucket(buckets, level, "rebar:" + name, name, "Rebar");
            bucket.LengthM += length;
            try
            {
                bucket.VolumeM3 += RevitUnits.ToCubicMeters(rebar.Volume);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                /* length is still useful when volume is unavailable */
            }

            return true;
        }

        private static int CountBars(Rebar rebar)
        {
            var positions = rebar.NumberOfBarPositions;
            if (positions <= 0)
                return Math.Max(rebar.Quantity, 1);

            var count = 0;
            for (var i = 0; i < positions; i++)
            {
                if (rebar.DoesBarExistAtPosition(i))
                    count++;
            }

            return count > 0 ? count : Math.Max(rebar.Quantity, 1);
        }

        private static Bucket GetBucket(
            Dictionary<string, Bucket> buckets,
            Level level,
            string materialKey,
            string name,
            string materialClass)
        {
            var levelName = level != null ? level.Name : "(no level)";
            var key = levelName + "\u001f" + materialKey;
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new Bucket
                {
                    LevelName = levelName,
                    ElevationM = level == null ? (double?)null : Round(RevitUnits.ToMeters(level.Elevation), 3),
                    Name = name ?? string.Empty,
                    MaterialClass = string.IsNullOrWhiteSpace(materialClass) ? null : materialClass
                };
                buckets.Add(key, bucket);
            }

            return bucket;
        }

        private static Level ResolveLevel(Document doc, Element element)
        {
            var level = LevelFromElement(doc, element);
            if (level != null)
                return level;

            var rebar = element as Rebar;
            if (rebar == null)
                return null;

            var host = doc.GetElement(rebar.GetHostId());
            return host == null ? null : LevelFromElement(doc, host);
        }

        private static Level LevelFromElement(Document doc, Element element)
        {
            if (element.LevelId != null && element.LevelId != ElementId.InvalidElementId)
            {
                var direct = doc.GetElement(element.LevelId) as Level;
                if (direct != null)
                    return direct;
            }

            foreach (var parameterId in RevitConstants.LevelParameters)
            {
                var parameter = element.get_Parameter(parameterId);
                if (parameter == null || parameter.StorageType != StorageType.ElementId)
                    continue;

                var id = parameter.AsElementId();
                if (id == null || id == ElementId.InvalidElementId)
                    continue;

                var level = doc.GetElement(id) as Level;
                if (level != null)
                    return level;
            }

            return null;
        }

        private static double Round(double value, int digits) =>
            Math.Round(value, digits, MidpointRounding.AwayFromZero);

        private sealed class Bucket
        {
            public string LevelName;
            public double? ElevationM;
            public string Name;
            public string MaterialClass;
            public double VolumeM3;
            public double AreaM2;
            public double LengthM;
        }
    }
}
