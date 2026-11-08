using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitCommandDispatcher.Commands.Builtin
{
    /// <summary>
    /// Reads project information, the axis-aligned size of host model geometry, and every level.
    /// </summary>
    public sealed class GetProjectInfoCommand : IRevitCommand
    {
        public string Method => "revit.getProjectInfo";
        public bool RequiresTransaction => false;
        public bool RequiresDocument => true;

        public object Execute(UIApplication app, JToken parameters)
        {
            var doc = app.ActiveUIDocument.Document;
            var info = doc.ProjectInformation;

            return new
            {
                projectName = FirstNonEmpty(info?.Name, doc.Title, "Untitled"),
                projectNumber = BlankToNull(info?.Number),
                buildingName = BlankToNull(info?.BuildingName),
                clientName = BlankToNull(info?.ClientName),
                address = BlankToNull(info?.Address),
                status = BlankToNull(info?.Status),
                author = BlankToNull(info?.Author),
                organizationName = BlankToNull(info?.OrganizationName),
                organizationDescription = BlankToNull(info?.OrganizationDescription),
                issueDate = BlankToNull(info?.IssueDate),
                fileName = string.IsNullOrWhiteSpace(doc.Title) ? "Untitled" : doc.Title,
                projectPath = doc.PathName ?? string.Empty,
                units = new { length = "m", elevation = "m" },
                dimensions = Measure(doc),
                levels = ReadLevels(doc)
            };
        }

        private static object Measure(Document doc)
        {
            var extents = new Extents();
            var collector = new FilteredElementCollector(doc).WhereElementIsNotElementType();

            foreach (var element in collector)
            {
                if (Skip(element))
                    continue;

                BoundingBoxXYZ box;
                try
                {
                    box = element.get_BoundingBox(null);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    continue;
                }

                Include(extents, box);
            }

            if (!extents.HasBox)
            {
                return new
                {
                    sizeXM = (double?)null,
                    sizeYM = (double?)null,
                    sizeZM = (double?)null,
                    minXM = (double?)null,
                    minYM = (double?)null,
                    minZM = (double?)null,
                    maxXM = (double?)null,
                    maxYM = (double?)null,
                    maxZM = (double?)null,
                    elementCount = 0
                };
            }

            return new
            {
                sizeXM = Meters(extents.MaxX - extents.MinX),
                sizeYM = Meters(extents.MaxY - extents.MinY),
                sizeZM = Meters(extents.MaxZ - extents.MinZ),
                minXM = Meters(extents.MinX),
                minYM = Meters(extents.MinY),
                minZM = Meters(extents.MinZ),
                maxXM = Meters(extents.MaxX),
                maxYM = Meters(extents.MaxY),
                maxZM = Meters(extents.MaxZ),
                elementCount = extents.ElementCount
            };
        }

        /// <summary>
        /// Host model geometry only. Links, CAD, scope boxes, and alternate design options
        /// would report a size that is not the building itself.
        /// </summary>
        private static bool Skip(Element element)
        {
            if (element.OwnerViewId != null && element.OwnerViewId != ElementId.InvalidElementId)
                return true;
            if (element is RevitLinkInstance || element is ImportInstance)
                return true;

            var category = element.Category;
            if (category == null || category.CategoryType != CategoryType.Model)
                return true;
            if (category.Id.IntegerValue == (int)BuiltInCategory.OST_VolumeOfInterest)
                return true;

            var option = element.DesignOption;
            return option != null && !option.IsPrimary;
        }

        private static void Include(Extents extents, BoundingBoxXYZ box)
        {
            if (box?.Min == null || box.Max == null)
                return;

            var min = box.Min;
            var max = box.Max;
            if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
                return;

            var transform = box.Transform;
            if (transform == null || transform.IsIdentity)
            {
                if (!IsFinite(min) || !IsFinite(max))
                    return;
                Absorb(extents, min.X, min.Y, min.Z);
                Absorb(extents, max.X, max.Y, max.Z);
            }
            else if (!AbsorbCorners(extents, transform, min, max))
            {
                return;
            }

            extents.ElementCount++;
        }

        /// <summary>Returns false when a transformed corner is not a finite coordinate.</summary>
        private static bool AbsorbCorners(Extents extents, Transform transform, XYZ min, XYZ max)
        {
            double x0 = min.X, x1 = max.X, y0 = min.Y, y1 = max.Y, z0 = min.Z, z1 = max.Z;
            var xs = new[] { x0, x0, x0, x0, x1, x1, x1, x1 };
            var ys = new[] { y0, y0, y1, y1, y0, y0, y1, y1 };
            var zs = new[] { z0, z1, z0, z1, z0, z1, z0, z1 };
            var corners = new XYZ[8];

            for (var i = 0; i < 8; i++)
            {
                var corner = transform.OfPoint(new XYZ(xs[i], ys[i], zs[i]));
                if (!IsFinite(corner))
                    return false;
                corners[i] = corner;
            }

            foreach (var corner in corners)
                Absorb(extents, corner.X, corner.Y, corner.Z);
            return true;
        }

        private static void Absorb(Extents extents, double x, double y, double z)
        {
            if (!extents.HasBox)
            {
                extents.MinX = extents.MaxX = x;
                extents.MinY = extents.MaxY = y;
                extents.MinZ = extents.MaxZ = z;
                extents.HasBox = true;
                return;
            }

            if (x < extents.MinX) extents.MinX = x;
            if (y < extents.MinY) extents.MinY = y;
            if (z < extents.MinZ) extents.MinZ = z;
            if (x > extents.MaxX) extents.MaxX = x;
            if (y > extents.MaxY) extents.MaxY = y;
            if (z > extents.MaxZ) extents.MaxZ = z;
        }

        private static object[] ReadLevels(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .Select(level => new
                {
                    name = level.Name ?? string.Empty,
                    elevationM = Meters(level.ProjectElevation)
                })
                .OrderBy(level => level.elevationM)
                .ThenBy(level => level.name, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool IsFinite(XYZ point) =>
            point != null && IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static double Meters(double internalLength) =>
            Math.Round(RevitUnits.ToMeters(internalLength), 3, MidpointRounding.AwayFromZero);

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return "Untitled";
        }

        private static string BlankToNull(string value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;

        private sealed class Extents
        {
            public bool HasBox;
            public int ElementCount;
            public double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        }
    }
}
