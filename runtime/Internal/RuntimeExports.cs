using System;
using System.Runtime;

namespace Internal.Runtime
{
    // Functions ILC-generated code calls by name. Most exist for features
    // RHOS doesn't have yet (a garbage collector, threads), so they do nothing.
    internal static unsafe class RuntimeExports
    {
        // Called around every [DllImport] call and every
        // [UnmanagedCallersOnly] entry, so a GC could know which threads are
        // running native code. No GC, so nothing to do.
        [RuntimeExport("RhpPInvoke")]
        static void RhpPInvoke(void* frame) { }

        [RuntimeExport("RhpPInvokeReturn")]
        static void RhpPInvokeReturn(void* frame) { }

        [RuntimeExport("RhpReversePInvoke")]
        static void RhpReversePInvoke(void* frame) { }

        [RuntimeExport("RhpReversePInvokeReturn")]
        static void RhpReversePInvokeReturn(void* frame) { }

        [RuntimeExport("RhpFallbackFailFast")]
        static void RhpFallbackFailFast() => Panic.Halt("fail fast");

        // `throw x;` lands here. With no exception handling, print and stop.
        [RuntimeExport("RhpThrowEx")]
        static void RhpThrowEx(object exception)
        {
            // C# only lets you throw Exceptions, so reinterpret without a cast.
            Exception e = *(Exception*)&exception;
            Panic.Halt(e.Message);
        }
    }
}

namespace Internal.Runtime.CompilerHelpers
{
    // ILC calls these when generated code detects an error.
    internal static class ThrowHelpers
    {
        public static void ThrowIndexOutOfRangeException() => Panic.Halt("index out of range");
        public static void ThrowNullReferenceException() => Panic.Halt("null reference");
        public static void ThrowDivideByZeroException() => Panic.Halt("divide by zero");
        public static void ThrowOverflowException() => Panic.Halt("arithmetic overflow");
        public static void ThrowInvalidCastException() => Panic.Halt("invalid cast");
        public static void ThrowArrayTypeMismatchException() => Panic.Halt("array type mismatch");
        public static void ThrowPlatformNotSupportedException() => Panic.Halt("platform not supported");
        public static void ThrowTypeLoadException() => Panic.Halt("type load");
    }
}
