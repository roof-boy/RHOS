using Internal.Runtime;

namespace System
{
    // Layout: [MethodTable*][int length][char, char, ...][0]
    public sealed unsafe class String
    {
        // ILC looks these two fields up by name, so keep the names.
        private readonly int _stringLength;
        private char _firstChar;

        public static readonly string Empty = "";

        public int Length => _stringLength;

        public char this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_stringLength)
                    Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowIndexOutOfRangeException();
                fixed (char* p = &_firstChar)
                    return p[index];
            }
        }

        // Allocates an uninitialised (zeroed) string with room for `length` chars.
        // Strings are allocated like arrays of char: BaseSize + length * 2 bytes.
        internal static string FastAllocate(int length)
        {
            if (length == 0)
                return Empty;
            // Every string literal points at the String MethodTable; borrow it.
            string literal = "";
            MethodTable* type = *(MethodTable**)*(void**)&literal;
            void* obj = Allocator.NewArray(type, length);
            return *(string*)&obj;
        }

        internal char* FirstCharPointer()
        {
            fixed (char* p = &_firstChar)
                return p;
        }

        internal static string FromChar(char c)
        {
            string s = FastAllocate(1);
            s.FirstCharPointer()[0] = c;
            return s;
        }

        public static string Concat(string a, string b)
        {
            if (a == null) a = Empty;
            if (b == null) b = Empty;
            string result = FastAllocate(a._stringLength + b._stringLength);
            char* dest = result.FirstCharPointer();
            Copy(dest, a);
            Copy(dest + a._stringLength, b);
            return result;
        }

        public static string Concat(string a, string b, string c) => Concat(Concat(a, b), c);

        public static string Concat(string a, string b, string c, string d)
            => Concat(Concat(a, b), Concat(c, d));

        // More than four pieces: the compiler packs them into an array.
        public static string Concat(params string[] values)
        {
            int length = 0;
            for (int i = 0; i < values.Length; i++)
                if (values[i] != null) length += values[i]._stringLength;
            string result = FastAllocate(length);
            char* dest = result.FirstCharPointer();
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == null) continue;
                Copy(dest, values[i]);
                dest += values[i]._stringLength;
            }
            return result;
        }

        static void Copy(char* dest, string source)
        {
            char* src = source.FirstCharPointer();
            for (int i = 0; i < source._stringLength; i++)
                dest[i] = src[i];
        }

        public static bool Equals(string a, string b)
        {
            if ((object)a == (object)b) return true;
            if ((object)a == null || (object)b == null) return false;
            if (a._stringLength != b._stringLength) return false;
            char* pa = a.FirstCharPointer();
            char* pb = b.FirstCharPointer();
            for (int i = 0; i < a._stringLength; i++)
                if (pa[i] != pb[i]) return false;
            return true;
        }

        public override bool Equals(object obj) => obj is string s && Equals(this, s);

        public static bool operator ==(string a, string b) => Equals(a, b);
        public static bool operator !=(string a, string b) => !Equals(a, b);

        public override int GetHashCode()
        {
            int hash = 5381;
            char* p = FirstCharPointer();
            for (int i = 0; i < _stringLength; i++)
                hash = hash * 33 + p[i];
            return hash;
        }

        public override string ToString() => this;
    }
}
