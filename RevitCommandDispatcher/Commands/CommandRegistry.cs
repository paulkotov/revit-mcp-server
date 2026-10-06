using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace RevitCommandDispatcher.Commands
{
    /// <summary>
    /// Maps a JSON-RPC method name to the <see cref="IRevitCommand"/> that handles it.
    /// Populate once at add-in startup (single writer), then read concurrently.
    /// </summary>
    public sealed class CommandRegistry
    {
        private readonly ConcurrentDictionary<string, IRevitCommand> _commands =
            new ConcurrentDictionary<string, IRevitCommand>(StringComparer.OrdinalIgnoreCase);

        public void Register(IRevitCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (string.IsNullOrWhiteSpace(command.Method))
                throw new ArgumentException("Command.Method must be set.", nameof(command));

            _commands[command.Method] = command;
        }

        public bool TryGet(string method, out IRevitCommand command)
        {
            command = null;
            return !string.IsNullOrEmpty(method) && _commands.TryGetValue(method, out command);
        }

        public IReadOnlyCollection<string> Methods => _commands.Keys.ToArray();

        /// <summary>Built-in commands always available regardless of commandRegistry.json.</summary>
        public static CommandRegistry CreateDefault()
        {
            var registry = new CommandRegistry();
            registry.RegisterBuiltins();
            return registry;
        }

        /// <summary>
        /// Creates a registry with builtins, then loads enabled plugin entries from
        /// <paramref name="registryFilePath"/> (typically Commands/commandRegistry.json).
        /// Failed plugin entries are skipped; errors are reported via <paramref name="onError"/>.
        /// </summary>
        public static CommandRegistry LoadFrom(
            string registryFilePath,
            Action<string, Exception> onError = null)
        {
            var registry = CreateDefault();

            if (string.IsNullOrWhiteSpace(registryFilePath) || !File.Exists(registryFilePath))
            {
                onError?.Invoke(
                    $"Command registry file not found: '{registryFilePath}'. Using builtins only.",
                    null);
                return registry;
            }

            CommandRegistryFile file;
            try
            {
                var json = File.ReadAllText(registryFilePath);
                file = JsonConvert.DeserializeObject<CommandRegistryFile>(json)
                       ?? new CommandRegistryFile();
            }
            catch (Exception ex)
            {
                onError?.Invoke($"Failed to parse command registry '{registryFilePath}'.", ex);
                return registry;
            }

            var commandsDir = Path.GetDirectoryName(registryFilePath) ?? string.Empty;
            var entries = file.Commands ?? Array.Empty<CommandRegistryEntry>();

            foreach (var entry in entries)
            {
                if (entry == null || !entry.Enabled)
                    continue;

                try
                {
                    var command = InstantiatePlugin(entry, commandsDir);
                    registry.Register(command);
                }
                catch (Exception ex)
                {
                    var label = !string.IsNullOrEmpty(entry.Method)
                        ? entry.Method
                        : $"{entry.AssemblyPath}::{entry.ClassName}";
                    onError?.Invoke($"Failed to load plugin command '{label}'.", ex);
                }
            }

            return registry;
        }

        private void RegisterBuiltins()
        {
            Register(new Builtin.PingCommand());
            Register(new Builtin.GetRevitVersionCommand());
        }

        private static IRevitCommand InstantiatePlugin(CommandRegistryEntry entry, string commandsDir)
        {
            if (string.IsNullOrWhiteSpace(entry.AssemblyPath))
                throw new InvalidOperationException("assemblyPath is required.");
            if (string.IsNullOrWhiteSpace(entry.ClassName))
                throw new InvalidOperationException("className is required.");

            var assemblyPath = Path.IsPathRooted(entry.AssemblyPath)
                ? entry.AssemblyPath
                : Path.GetFullPath(Path.Combine(commandsDir, entry.AssemblyPath));

            if (!File.Exists(assemblyPath))
                throw new FileNotFoundException($"Plugin assembly not found: '{assemblyPath}'.");

            var assembly = Assembly.LoadFrom(assemblyPath);
            var type = assembly.GetType(entry.ClassName, throwOnError: false)
                       ?? throw new TypeLoadException(
                           $"Type '{entry.ClassName}' not found in '{assemblyPath}'.");

            if (!typeof(IRevitCommand).IsAssignableFrom(type))
                throw new InvalidOperationException(
                    $"Type '{entry.ClassName}' does not implement IRevitCommand.");

            if (Activator.CreateInstance(type) is not IRevitCommand command)
                throw new InvalidOperationException(
                    $"Failed to instantiate '{entry.ClassName}' (needs a public parameterless constructor).");

            if (!string.IsNullOrWhiteSpace(entry.Method)
                && !string.Equals(entry.Method, command.Method, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Registry method '{entry.Method}' does not match command.Method '{command.Method}'.");
            }

            return command;
        }
    }
}
