using Internal.Runtime;

namespace System.Runtime
{
    // `x is Foo`, `x as Foo`, `(Foo)x` and storing into object arrays.
    // ILC finds these helpers by their names in this class.
    internal static unsafe class TypeCast
    {
        // A class's MethodTable stores a pointer to its parent class's
        // MethodTable 8 bytes in. Walk up the chain looking for the target.
        static bool IsSubclassOf(MethodTable* type, MethodTable* target)
        {
            while (type != null)
            {
                if (type == target) return true;
                type = *(MethodTable**)((byte*)type + 8);
            }
            return false;
        }

        static MethodTable* TypeOf(object obj) => *(MethodTable**)*(void**)&obj;

        public static object IsInstanceOfClass(void* toType, object obj)
        {
            if (obj == null || IsSubclassOf(TypeOf(obj), (MethodTable*)toType))
                return obj;
            return null;
        }

        public static object CheckCastClass(void* toType, object obj)
        {
            if (obj == null || IsSubclassOf(TypeOf(obj), (MethodTable*)toType))
                return obj;
            Panic.Halt("invalid cast");
            return null;
        }

        public static object CheckCastClassSpecial(void* toType, object obj) => CheckCastClass(toType, obj);

        // Interfaces and arrays need more of the MethodTable than RHOS reads.
        public static object IsInstanceOfAny(void* toType, object obj) => IsInstanceOfClass(toType, obj);
        public static object CheckCastAny(void* toType, object obj) => CheckCastClass(toType, obj);

        public static object IsInstanceOfInterface(void* toType, object obj)
        {
            Panic.Halt("interface casts are not supported yet");
            return null;
        }

        public static object CheckCastInterface(void* toType, object obj) => IsInstanceOfInterface(toType, obj);

        // `objects[i] = x`. Written with pointers: `array[index] = obj` here
        // would compile to a call to this same method, forever.
        public static void StelemRef(object[] array, nint index, object obj)
        {
            if ((uint)index >= (uint)array.Length)
                Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowIndexOutOfRangeException();
            fixed (object* first = array)
                first[index] = obj;
        }

        // `ref objects[i]`.
        public static ref object LdelemaRef(object[] array, nint index, void* elementType)
        {
            if ((uint)index >= (uint)array.Length)
                Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowIndexOutOfRangeException();
            fixed (object* first = array)
                return ref first[index];
        }
    }
}
