using System.Runtime.InteropServices;

namespace Internal.Runtime
{
    // Last resort: print a message on the UART and stop this core forever.
    public static unsafe class Panic
    {
        [DllImport("*")]
        static extern void uart_putc(byte c);

        // Prints byte by byte, without allocating, so it works even when
        // the heap is broken or full.
        static void Print(string s)
        {
            if ((object)s == null) { Print("(null)"); return; }
            char* p = s.FirstCharPointer();
            for (int i = 0; i < s.Length; i++)
                uart_putc((byte)p[i]);
        }

        public static void Halt(string message)
        {
            Print("\nPANIC: ");
            Print(message);
            Print("\n");
            while (true) { }
        }
    }
}
