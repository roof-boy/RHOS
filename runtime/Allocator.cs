using System.Runtime;

namespace Internal.Runtime
{
    internal struct MethodTable
    {
        public ushort ComponentSize;
        public ushort Flags;
        public uint BaseSize;
    };

    public static unsafe class Allocator
    {
        static byte* _next;
        static byte* _end;

        public static void Init(void* start, ulong size)
        {
            _next = (byte*)start;
            _end = (byte*)start + size;
        }

        public static void* Alloc(ulong size)
        {
            ulong alignedSize = (size + 7) & ~7UL;

            if (_next + size > _end)
                while (true) { }

            byte* block = _next;
            _next += alignedSize;

            for (ulong i = 0; i < size; i++)
                block[i] = 0;

            return block;
        }

        [RuntimeExport("RhpNewFast")]
        static void* RhpNewFast(MethodTable* type)
        {
            void* obj = Alloc(type->BaseSize);
            *(MethodTable**)obj = type;
            return obj;
        }
    }
}