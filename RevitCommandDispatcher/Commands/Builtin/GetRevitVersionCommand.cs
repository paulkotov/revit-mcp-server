using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitCommandDispatcher.Commands.Builtin
{
    /// <summary>Returns the running Revit version. Application-level; no document required.</summary>
    public sealed class GetRevitVersionCommand : IRevitCommand
    {
        public string Method => "revit.getVersion";
        public bool RequiresTransaction => false;
        public bool RequiresDocument => false;

        public object Execute(UIApplication app, JToken parameters)
        {
            var application = app.Application;
            return new
            {
                versionName = application.VersionName,
                versionNumber = application.VersionNumber,
                versionBuild = application.VersionBuild
            };
        }
    }
}
