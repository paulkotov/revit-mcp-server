using Autodesk.Revit.DB;

namespace RevitCommandDispatcher
{
    /// <summary>Shared Revit API identifiers used by more than one command.</summary>
    public static class RevitConstants
    {
        public static readonly BuiltInCategory[] StructuralCategories =
        {
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_Ramps,
            BuiltInCategory.OST_StructConnections,
            BuiltInCategory.OST_Rebar
        };

        public static readonly BuiltInParameter[] LevelParameters =
        {
            BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
            BuiltInParameter.FAMILY_LEVEL_PARAM,
            BuiltInParameter.SCHEDULE_LEVEL_PARAM,
            BuiltInParameter.WALL_BASE_CONSTRAINT,
            BuiltInParameter.STAIRS_BASE_LEVEL_PARAM,
            BuiltInParameter.ROOF_CONSTRAINT_LEVEL_PARAM
        };
    }
}
