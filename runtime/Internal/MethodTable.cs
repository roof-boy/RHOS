namespace Internal.Runtime
{
    // The header ILC writes for every type. Every object's first 8 bytes
    // point at one of these. We only need the first three fields.
    internal struct MethodTable
    {
        // Size of one element, for arrays and strings (0 for other types).
        public ushort ComponentSize;
        public ushort Flags;
        // Size of an object of this type with zero elements, in bytes.
        public uint BaseSize;
    }
}
