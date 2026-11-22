namespace System
{
    public class Object
    {
        private IntPtr m_pMethodTable;
    }

    public struct Void { };
    public struct Int32 { };
    public struct Byte { };
    public struct IntPtr { };
    public struct Char { };
    public struct Double { };
    public struct UInt32 { };
    public struct UInt64 { };
    public struct UIntPtr { };

    public sealed class String
    {
        private readonly int _length;
        private char _firstChar;
        public int Length => _length;
        public unsafe char this[int index]
        {
            get
            {
                fixed (char* p = &_firstChar)
                    return p[index];
            }
        }
    };

    public abstract class ValueType { };

    public class Attribute { };
    public enum AttributeTargets { };
    public sealed class AttributeUsageAttribute : Attribute
    {
        public AttributeUsageAttribute(AttributeTargets attributeTargets)
        {
        }

        public bool AllowMultiple { get; set; }
        public bool Inherited { get; set; }
    };

    public struct Boolean { };

    public abstract class Enum : ValueType { };
}

namespace System.Reflection
{
    public sealed class DefaultMemberAttribute : Attribute
    {
        public DefaultMemberAttribute(string memberName) { }
    }
}

namespace System.Runtime
{
    internal sealed class RuntimeExportAttribute : Attribute
    {
        public RuntimeExportAttribute(string entry) { }
    }
}

namespace Internal.Runtime
{
    internal static class Transitions
    {
        [System.Runtime.RuntimeExport("RhpPInvoke")]
        static void RhpPInvoke(System.IntPtr frame) { }

        [System.Runtime.RuntimeExport("RhpPInvokeReturn")]
        static void RhpPInvokeReturn(System.IntPtr frame) { }

        [System.Runtime.RuntimeExport("RhpReversePInvoke")]
        static void RhpReversePInvoke(System.IntPtr frame) { }

        [System.Runtime.RuntimeExport("RhpReversePInvokeReturn")]
        static void RhpReversePInvokeReturn(System.IntPtr frame) { }

        [System.Runtime.RuntimeExport("RhpFallbackFailFast")]
        static void RhpFallbackFailFast() { while (true) { } }
    }
}

namespace System.Runtime.InteropServices
{
    public class DllImportAttribute : Attribute
    {
        public DllImportAttribute(string dllName) { }
    }

    public class UnmanagedCallersOnlyAttribute : Attribute
    {
        public string EntryPoint;
    }
}

namespace System.Runtime.CompilerServices
{
    public static class RuntimeHelpers
    {
        public static unsafe int OffsetToStringData => sizeof(IntPtr) + sizeof(int);
    }
}