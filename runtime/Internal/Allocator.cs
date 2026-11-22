using System.Runtime;

namespace Internal.Runtime
{
    // A bump allocator: hand out memory by moving a pointer forward.
    // Nothing is ever freed (there is no garbage collector yet).
    public static unsafe class Allocator
    {
        // volatile: the compiler assumes `new` doesn't change our own static
        // fields, so without it, reading Free twice around a `new` could
        // return the same (stale) number.
        static volatile byte* _next;
        static byte* _end;

        public static void Init(void* start, ulong size)
        {
            _next = (byte*)start;
            _end = (byte*)start + size;
        }

        // Bytes still free.
        public static ulong Free => (ulong)(_end - _next);

        public static void* Alloc(ulong size)
        {
            ulong alignedSize = (size + 7) & ~7UL;

            if (_next == null)
                Panic.Halt("allocation before Allocator.Init");
            if (alignedSize > (ulong)(_end - _next))
                Panic.Halt("out of memory");

            byte* block = _next;
            _next += alignedSize;

            for (ulong i = 0; i < alignedSize; i++)
                block[i] = 0;

            return block;
        }

        // An object of a type without elements: `new Point(...)`.
        [RuntimeExport("RhpNewFast")]
        static void* RhpNewFast(MethodTable* type)
        {
            void* obj = Alloc(type->BaseSize);
            *(MethodTable**)obj = type;
            return obj;
        }

        // An array or string: BaseSize + length * ComponentSize bytes.
        internal static void* NewArray(MethodTable* type, int length)
        {
            if (length < 0)
                Panic.Halt("negative array length");
            ulong size = type->BaseSize + (ulong)length * type->ComponentSize;
            void* obj = Alloc(size);
            *(MethodTable**)obj = type;
            *(int*)((byte*)obj + 8) = length;
            return obj;
        }

        [RuntimeExport("RhpNewArrayFast")]
        static void* RhpNewArrayFast(MethodTable* type, int length) => NewArray(type, length);

        // Arrays of references (string[], Point[]) use this entry point.
        [RuntimeExport("RhpNewPtrArrayFast")]
        static void* RhpNewPtrArrayFast(MethodTable* type, int length) => NewArray(type, length);
    }
}
