using Newtonsoft.Json;

namespace RevitCommandDispatcher.Commands
{
    /// <summary>Root of Commands/commandRegistry.json.</summary>
    public sealed class CommandRegistryFile
    {
        [JsonProperty("commands")]
        public CommandRegistryEntry[] Commands { get; set; } = new CommandRegistryEntry[0];
    }

    /// <summary>
    /// One external (plugin) command. Built-ins are always registered in code and need not
    /// appear here. <see cref="AssemblyPath"/> is relative to the Commands/ directory
    /// unless it is an absolute path.
    /// </summary>
    public sealed class CommandRegistryEntry
    {
        /// <summary>
        /// Optional override for logging. The authoritative method name comes from
        /// <see cref="IRevitCommand.Method"/> on the instantiated type.
        /// </summary>
        [JsonProperty("method")]
        public string Method { get; set; }

        /// <summary>DLL path relative to Commands/ (or absolute).</summary>
        [JsonProperty("assemblyPath")]
        public string AssemblyPath { get; set; }

        /// <summary>Fully-qualified type name implementing <see cref="IRevitCommand"/>.</summary>
        [JsonProperty("className")]
        public string ClassName { get; set; }

        [JsonProperty("enabled")]
        public bool Enabled { get; set; } = true;
    }
}
