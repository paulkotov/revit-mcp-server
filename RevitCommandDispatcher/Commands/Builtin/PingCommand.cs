using System;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;

namespace RevitCommandDispatcher.Commands.Builtin
{
    /// <summary>Liveness probe. Read-only, no transaction, no document required.</summary>
    public sealed class PingCommand : IRevitCommand
    {
        public string Method => "revit.ping";
        public bool RequiresTransaction => false;
        public bool RequiresDocument => false;

        public object Execute(UIApplication app, JToken parameters)
        {
            return new
            {
                pong = true,
                timestampUtc = DateTime.UtcNow.ToString("O")
            };
        }
    }
}
