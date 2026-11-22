// The types behind C#'s built-in keywords (bool, int, char...).
// The compiler and ILC already know their size and how to do arithmetic on
// them. The m_value fields exist so methods like ToString can read the value.

using Internal.Runtime;

namespace System
{
    public struct Boolean
    {
        private readonly bool m_value;
        public override string ToString() => m_value ? "True" : "False";
    }

    public struct Char
    {
        private readonly char m_value;
        public override string ToString() => string.FromChar(m_value);
    }

    public struct SByte
    {
        private readonly sbyte m_value;
        public const sbyte MaxValue = 127;
        public const sbyte MinValue = -128;
        public override string ToString() => Number.FormatSigned(m_value);
    }

    public struct Byte
    {
        private readonly byte m_value;
        public const byte MaxValue = 255;
        public const byte MinValue = 0;
        public override string ToString() => Number.FormatUnsigned(m_value);
    }

    public struct Int16
    {
        private readonly short m_value;
        public const short MaxValue = 32767;
        public const short MinValue = -32768;
        public override string ToString() => Number.FormatSigned(m_value);
    }

    public struct UInt16
    {
        private readonly ushort m_value;
        public const ushort MaxValue = 65535;
        public const ushort MinValue = 0;
        public override string ToString() => Number.FormatUnsigned(m_value);
    }

    public struct Int32
    {
        private readonly int m_value;
        public const int MaxValue = 0x7FFFFFFF;
        public const int MinValue = unchecked((int)0x80000000);
        public override string ToString() => Number.FormatSigned(m_value);
    }

    public struct UInt32
    {
        private readonly uint m_value;
        public const uint MaxValue = 0xFFFFFFFF;
        public const uint MinValue = 0;
        public override string ToString() => Number.FormatUnsigned(m_value);
    }

    public struct Int64
    {
        private readonly long m_value;
        public const long MaxValue = 0x7FFFFFFFFFFFFFFF;
        public const long MinValue = unchecked((long)0x8000000000000000);
        public override string ToString() => Number.FormatSigned(m_value);
    }

    public struct UInt64
    {
        private readonly ulong m_value;
        public const ulong MaxValue = 0xFFFFFFFFFFFFFFFF;
        public const ulong MinValue = 0;
        public override string ToString() => Number.FormatUnsigned(m_value);
    }

    public unsafe struct IntPtr
    {
        private readonly void* m_value;
        public override string ToString() => Number.FormatSigned((long)m_value);
    }

    public unsafe struct UIntPtr
    {
        private readonly void* m_value;
        public override string ToString() => Number.FormatUnsigned((ulong)m_value);
    }

    // Floating point: arithmetic works; formatting is left out for now.
    public struct Single
    {
        private readonly float m_value;
    }

    public struct Double
    {
        private readonly double m_value;
    }
}
