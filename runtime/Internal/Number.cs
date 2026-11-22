namespace Internal.Runtime
{
    // Turns integers into strings. Used by int.ToString() and friends.
    public static unsafe class Number
    {
        public static string FormatSigned(long value)
        {
            if (value >= 0)
                return FormatUnsigned((ulong)value);
            // -value overflows for long.MinValue; negate as unsigned instead.
            return Format(0 - (ulong)value, true);
        }

        public static string FormatUnsigned(ulong value) => Format(value, false);

        static string Format(ulong value, bool negative)
        {
            // Write digits right to left into a buffer, then copy them out.
            char* buf = stackalloc char[20];
            int n = 0;
            do
            {
                buf[19 - n] = (char)('0' + (int)(value % 10));
                n++;
                value /= 10;
            } while (value != 0);

            int length = negative ? n + 1 : n;
            string s = string.FastAllocate(length);
            char* dest = s.FirstCharPointer();
            int start = 0;
            if (negative) dest[start++] = '-';
            for (int i = 0; i < n; i++)
                dest[start + i] = buf[20 - n + i];
            return s;
        }

        public static string FormatHex(ulong value)
        {
            // "0x" + 16 digits, always full width so addresses line up.
            string s = string.FastAllocate(18);
            char* dest = s.FirstCharPointer();
            dest[0] = '0';
            dest[1] = 'x';
            for (int i = 0; i < 16; i++)
            {
                int digit = (int)((value >> (60 - i * 4)) & 0xF);
                dest[2 + i] = (char)(digit < 10 ? '0' + digit : 'A' + digit - 10);
            }
            return s;
        }
    }
}
