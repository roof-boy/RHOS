namespace System
{
    // ILC's code generator looks these up when it optimises object
    // comparisons. RHOS has no reflection, so they stay empty.
    public abstract class Type { }
    internal sealed class RuntimeType : Type { }
    public struct RuntimeTypeHandle { }
    public struct RuntimeMethodHandle { }
    public struct RuntimeFieldHandle { }
}
