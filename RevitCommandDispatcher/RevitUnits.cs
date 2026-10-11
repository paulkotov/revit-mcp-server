using Autodesk.Revit.DB;

namespace RevitCommandDispatcher
{
    /// <summary>Converts Revit internal units to metric quantities.</summary>
    public static class RevitUnits
    {
        public static double ToMeters(double internalLength) =>
            UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Meters);

        public static double ToSquareMeters(double internalArea) =>
            UnitUtils.ConvertFromInternalUnits(internalArea, UnitTypeId.SquareMeters);

        public static double ToCubicMeters(double internalVolume) =>
            UnitUtils.ConvertFromInternalUnits(internalVolume, UnitTypeId.CubicMeters);
    }
}
