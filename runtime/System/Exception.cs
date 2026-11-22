namespace System
{
    // RHOS has no exception handling yet: `throw` prints the message as a
    // kernel panic and stops (see Internal.Runtime.Exceptions). `catch` blocks
    // never run.
    public class Exception
    {
        public string Message { get; }

        public Exception() { Message = "Exception"; }
        public Exception(string message) { Message = message; }
    }

    public class InvalidOperationException : Exception
    {
        public InvalidOperationException(string message) : base(message) { }
    }

    public class ArgumentException : Exception
    {
        public ArgumentException(string message) : base(message) { }
    }

    public class NotSupportedException : Exception
    {
        public NotSupportedException(string message) : base(message) { }
    }
}
