namespace System
{
    // The base class of every class. Every object in memory starts with a
    // pointer to its type's MethodTable; this field is that pointer.
    public class Object
    {
#pragma warning disable 169, 649
        internal unsafe Internal.Runtime.MethodTable* m_pMethodTable;
#pragma warning restore 169, 649

        public Object() { }

        public virtual string ToString() => "System.Object";

        // Reference equality. Types like String override it.
        public virtual bool Equals(object obj) => this == obj;

        public virtual unsafe int GetHashCode()
        {
            object self = this;
            ulong address = *(ulong*)&self;
            return (int)(address ^ (address >> 32));
        }
    }

    // Every struct inherits from ValueType, every enum from Enum.
    public abstract class ValueType { }
    public abstract class Enum : ValueType { }

    // The type behind the `void` keyword.
    public struct Void { }
}
