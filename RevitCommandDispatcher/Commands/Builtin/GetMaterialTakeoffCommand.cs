using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitCommandDispatcher.Commands.Builtin
{
    /// <summary>
    /// Sums the main structural materials of the active document, one bucket per level.
    /// Concrete, masonry, timber and steel come from element material volume.
    /// Rebar is measured by bar length so it is not lost inside the host concrete volume.
    /// Finishes (paint, gypsum, glass, insulation) are left out.
    /// </summary>
    public sealed class GetMaterialTakeoffCommand : IRevitCommand
    {
        private const double VolumeEpsilon = 0.0001; // m3
        private const double OtherVolumeMinimum = 0.05; // m3
        private const double LengthEpsilon = 0.01; // m

        public string Method => "revit.getMaterialTakeoff";
        public bool RequiresTransaction => false;
        public bool RequiresDocument => true;

        public object Execute(UIApplication app, JToken parameters)
        {
            var doc = app.ActiveUIDocument.Document;
            var buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);

            var collector = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(new ElementMulticategoryFilter(RevitConstants.StructuralCategories));

            foreach (var element in collector)
            {
                if (element.OwnerViewId != null && element.OwnerViewId != ElementId.InvalidElementId)
                    continue;

                var level = ResolveLevel(doc, element);
                if (element.Category != null && element.Category.Id.IntegerValue == (int)BuiltInCategory.OST_Rebar)
                {
                    AddRebar(doc, element, level, buckets);
                    continue;
                }

                AddMaterialVolumes(doc, element, level, buckets);
            }

            var levels = buckets.Values
                .GroupBy(bucket => bucket.LevelName)
                .Select(group => new
                {
                    name = group.Key,
                    elevationM = group.First().ElevationM,
                    materials = group
                        .OrderBy(item => item.Kind)
                        .ThenBy(item => item.Name)
                        .Select(item => new
                        {
                            kind = item.Kind,
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

        private static void AddMaterialVolumes(
            Document doc,
            Element element,
            Level level,
            Dictionary<string, Bucket> buckets)
        {
            ICollection<ElementId> materialIds;
            try
            {
                materialIds = element.GetMaterialIds(false);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return;
            }

            foreach (var materialId in materialIds)
            {
                var material = doc.GetElement(materialId) as Material;
                if (material == null)
                    continue;

                var kind = Classify(material.Name, material.MaterialClass);
                if (kind == "finish")
                    continue;

                double volume;
                double area;
                try
                {
                    volume = RevitUnits.ToCubicMeters(element.GetMaterialVolume(materialId));
                    area = RevitUnits.ToSquareMeters(element.GetMaterialArea(materialId, false));
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    continue;
                }

                if (volume < VolumeEpsilon)
                    continue;
                if (kind == "other" && volume < OtherVolumeMinimum)
                    continue;

                var bucket = GetBucket(buckets, level, kind, material.Name, material.MaterialClass);
                bucket.VolumeM3 += volume;
                bucket.AreaM2 += area;
            }
        }

        private static void AddRebar(
            Document doc,
            Element element,
            Level level,
            Dictionary<string, Bucket> buckets)
        {
            var rebar = element as Rebar;
            if (rebar == null)
                return;

            var barCount = CountBars(rebar);
            var length = RevitUnits.ToMeters(rebar.TotalLength) * barCount;
            if (length < LengthEpsilon)
                return;

            var barType = doc.GetElement(rebar.GetTypeId()) as RebarBarType;
            var name = barType != null && !string.IsNullOrWhiteSpace(barType.Name)
                ? barType.Name
                : element.Name;

            var bucket = GetBucket(buckets, level, "rebar", name, "Rebar");
            bucket.LengthM += length;
            try
            {
                bucket.VolumeM3 += RevitUnits.ToCubicMeters(rebar.Volume);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                /* length is still useful when volume is unavailable */
            }
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
            string kind,
            string name,
            string materialClass)
        {
            var levelName = level != null ? level.Name : "(no level)";
            var key = levelName + "\u001f" + kind + "\u001f" + name;
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new Bucket
                {
                    LevelName = levelName,
                    ElevationM = level == null ? (double?)null : Round(RevitUnits.ToMeters(level.Elevation), 3),
                    Kind = kind,
                    Name = name,
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

        /// <summary>
        /// Class names and material names in both English and Russian models.
        /// Rebar is tested before steel so "арматурная сталь" stays rebar.
        /// </summary>
        private static string Classify(string name, string materialClass)
        {
            var text = ((name ?? string.Empty) + " " + (materialClass ?? string.Empty)).ToLower(CultureInfo.InvariantCulture);

            if (ContainsAny(text, "rebar", "reinforc", "арматур", "a500", "а500", "a400", "а400", "а240", "a240"))
                return "rebar";
            if (ContainsAny(text, "gypsum", "gkl", "гипсокарт", "plaster", "штукатур", "paint", "краск", "glass", "стекл", "insulation", "утеплит", "гидроизол", "membrane", "мембран"))
                return "finish";
            if (ContainsAny(text, "concrete", "бетон", "жб"))
                return "concrete";
            if (ContainsAny(text, "masonry", "brick", "кирпич", "кладк", "block", "блок"))
                return "masonry";
            if (ContainsAny(text, "wood", "timber", "lumber", "дерев", "брус"))
                return "timber";
            if (ContainsAny(text, "steel", "сталь", "metal", "металл"))
                return "steel";
            return "other";
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            foreach (var needle in needles)
            {
                if (text.Contains(needle))
                    return true;
            }

            return false;
        }

        private static double Round(double value, int digits) =>
            Math.Round(value, digits, MidpointRounding.AwayFromZero);

        private sealed class Bucket
        {
            public string LevelName;
            public double? ElevationM;
            public string Kind;
            public string Name;
            public string MaterialClass;
            public double VolumeM3;
            public double AreaM2;
            public double LengthM;
        }
    }
}
