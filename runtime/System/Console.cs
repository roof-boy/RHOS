using System.Runtime.InteropServices;

namespace System
{
    // Text in and out over the UART (boot/uart.c).
    public static unsafe class Console
    {
        [DllImport("*")]
        static extern void uart_putc(byte c);

        [DllImport("*")]
        static extern byte uart_getc();

        // uart_putc already turns '\n' into the "\r\n" terminals expect.
        public static void Write(char c) => uart_putc((byte)c);

        public static void Write(string s)
        {
            if (s == null) return;
            char* p = s.FirstCharPointer();
            for (int i = 0; i < s.Length; i++)
                Write(p[i]);
        }

        public static void Write(int value) => Write(value.ToString());
        public static void Write(long value) => Write(value.ToString());
        public static void Write(ulong value) => Write(value.ToString());
        public static void Write(bool value) => Write(value ? "True" : "False");
        public static void Write(object value) { if (value != null) Write(value.ToString()); }

        public static void WriteLine() => Write('\n');
        public static void WriteLine(string s) { Write(s); Write('\n'); }
        public static void WriteLine(int value) { Write(value); Write('\n'); }
        public static void WriteLine(long value) { Write(value); Write('\n'); }
        public static void WriteLine(ulong value) { Write(value); Write('\n'); }
        public static void WriteLine(bool value) { Write(value); Write('\n'); }
        public static void WriteLine(object value) { Write(value); Write('\n'); }

        // Prints 0x and 16 hex digits. Handy for addresses and registers.
        public static void WriteHex(ulong value) => Write(Internal.Runtime.Number.FormatHex(value));

        // Waits for one key press and returns it (no echo).
        public static char ReadChar() => (char)uart_getc();
    }
}
