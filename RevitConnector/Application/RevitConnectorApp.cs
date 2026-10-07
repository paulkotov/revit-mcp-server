using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.UI;
using RevitCommandDispatcher;
using RevitCommandDispatcher.Commands;
using RevitConnector.Configuration;
using RevitConnector.Core;
using RevitConnector.Transport;
using RevitConnector.UI;
using RevitConnector.Utils;

namespace RevitConnector.Application
{
    /// <summary>
    /// Add-in entry point. Wires the bridge together on startup and tears it down on shutdown.
    /// The ExternalEvent MUST be created here (inside a valid Revit API context).
    /// </summary>
    public sealed class RevitConnectorApp : IExternalApplication
    {
        private ExternalEvent _externalEvent;
        private CommandExecutionHandler _handler;
        private WebSocketClient _client;
        private ConnectionStatusPresenter _statusPresenter;
        private CancellationTokenSource _cts;

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                var settings = ConnectorSettings.Load();

                var registryPath = PathManager.GetCommandRegistryFilePath();
                var registry = CommandRegistry.LoadFrom(
                    registryPath,
                    (message, ex) =>
                    {
                        if (ex != null) Logger.Error(message, ex);
                        else Logger.Warn(message);
                    });
                Logger.Info(
                    $"Command registry loaded from '{registryPath}'. " +
                    $"Methods: [{string.Join(", ", registry.Methods)}]");

                var executor = new RevitCommandExecutor(registry);

                var queue = new ConcurrentQueue<CommandExecution>();
                _handler = new CommandExecutionHandler(queue, executor);
                _externalEvent = ExternalEvent.Create(_handler);

                var dispatcher = new DataExchangeDispatcher(queue, _externalEvent, settings);
                _client = new WebSocketClient(settings, dispatcher);
                _statusPresenter = new ConnectionStatusPresenter();
                _client.StatusChanged += _statusPresenter.Publish;
                ConnectorSession.Attach(_client);

                try
                {
                    ConnectionRibbon.Build(application, _statusPresenter);
                }
                catch (Exception ex)
                {
                    // The socket loop is still useful without a ribbon; don't fail startup.
                    Logger.Error("Failed to create the MCP connection ribbon.", ex);
                }

                // Visible to Reconnect before the thread-pool task is actually scheduled.
                _client.MarkRunning();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;
                Task.Run(() => _client.RunAsync(token), token);

                Logger.Info("RevitConnector started.");
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                Logger.Error("RevitConnector failed to start.", ex);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            try
            {
                _cts?.Cancel();
                _client?.Dispose();
                _statusPresenter?.Dispose();
                _externalEvent?.Dispose();
                _cts?.Dispose();
                Logger.Info("RevitConnector stopped.");
            }
            catch (Exception ex)
            {
                Logger.Error("Error during RevitConnector shutdown.", ex);
            }
            return Result.Succeeded;
        }
    }
}
