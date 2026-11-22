namespace System
{
    class Object
    {
        private IntPtr m_pMethodTable;
    }

    public struct Void { };
    public struct Int32 { };
    public struct Byte { };
    public struct IntPtr { };

    sealed class String
    {
        public readonly int Length;
    }

    public abstract class ValueType { };

    public class Attribute { };
    public enum AttributeTargets { };
    sealed class AttributeUsageAttribute : Attribute
    {
        AttributeUsageAttribute(AttributeTargets attributeTargets)
        {
        }

        bool AllowMultiple { get; set; }
        bool Inherited { get; set; }
    };

    public struct Boolean { };

    public abstract class Enum : ValueType { };
}

namespace System.Runtime.InteropServices
{
    public class DllImportAttribute : Attribute
    {
        public DllImportAttribute(string dllName);
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