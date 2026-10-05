using System;

namespace RevitCommandDispatcher.Exceptions
{
    /// <summary>Base class for errors that the transport maps to JSON-RPC error codes.</summary>
    public abstract class CommandException : Exception
    {
        protected CommandException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>No command is registered for the requested method (JSON-RPC -32601).</summary>
    public sealed class CommandNotFoundException : CommandException
    {
        public string Method { get; }

        public CommandNotFoundException(string method)
            : base($"Unknown method '{method}'.")
        {
            Method = method;
        }
    }

    /// <summary>Params failed validation (JSON-RPC -32602).</summary>
    public sealed class InvalidParamsException : CommandException
    {
        public InvalidParamsException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>There is no active Revit document to operate on (custom -32001).</summary>
    public sealed class NoActiveDocumentException : CommandException
    {
        public NoActiveDocumentException()
            : base("No active Revit document.") { }
    }

    /// <summary>A Revit API call failed while executing the command (custom -32000).</summary>
    public sealed class RevitCommandException : CommandException
    {
        public RevitCommandException(string message, Exception inner = null) : base(message, inner) { }
    }
}
