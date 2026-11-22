// Attributes are ordinary classes. These are the ones the compiler, ILC or
// RHOS code needs. Their bodies are empty: tools only read their name and
// arguments.

namespace System
{
    public class Attribute { }

    [Flags]
    public enum AttributeTargets
    {
        Assembly = 1, Module = 2, Class = 4, Struct = 8, Enum = 16,
        Constructor = 32, Method = 64, Property = 128, Field = 256,
        Event = 512, Interface = 1024, Parameter = 2048, Delegate = 4096,
        ReturnValue = 8192, GenericParameter = 16384, All = 32767,
    }

    public sealed class AttributeUsageAttribute : Attribute
    {
        public AttributeUsageAttribute(AttributeTargets validOn) { }
        public bool AllowMultiple { get; set; }
        public bool Inherited { get; set; }
    }

    public sealed class FlagsAttribute : Attribute { }

    // Makes `params T[] args` work.
    public sealed class ParamArrayAttribute : Attribute { }
}

namespace System.Reflection
{
    // The compiler attaches this to classes with an indexer (like String).
    public sealed class DefaultMemberAttribute : Attribute
    {
        public DefaultMemberAttribute(string memberName) { }
    }
}

namespace System.Runtime
{
    // ILC: export this method under the given symbol name.
    internal sealed class RuntimeExportAttribute : Attribute
    {
        public RuntimeExportAttribute(string entry) { }
    }
}

namespace System.Runtime.InteropServices
{
    // Call a native (C or assembly) function. "*" = linked into the same image.
    public sealed class DllImportAttribute : Attribute
    {
        public DllImportAttribute(string dllName) { }
    }

    // Make a C# method callable from native code under EntryPoint.
    public sealed class UnmanagedCallersOnlyAttribute : Attribute
    {
        public string EntryPoint;
    }
}

namespace System.Runtime.CompilerServices
{
    public static class RuntimeHelpers
    {
        // Where a string's characters start: after the MethodTable* and the length.
        public static unsafe int OffsetToStringData => sizeof(IntPtr) + sizeof(int);
    }
}

namespace System.Runtime.CompilerServices
{
    // The compiler marks `volatile` fields with this.
    public static class IsVolatile { }
}
