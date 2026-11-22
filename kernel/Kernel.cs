using System.Runtime.InteropServices;

public static unsafe class Kernel
{
    // A C function from boot/uart.c. "*" means "it's linked into the same
    // image, look it up by name".
    [DllImport("*")]
    static extern void uart_putc(byte c);

    static void Print(string s)
    {
        fixed (char* p = s)
        {
            for (int i = 0; i < s.Length; i++)
                uart_putc((byte)s[i]);
        }
    }

    static void PrintDec(int value)
    {
        if (value < 0)
        {
            uart_putc((byte)'-');
            value = -value;
        }

        byte* buf = stackalloc byte[10];
        int n = 0;
        do
        {
            buf[n++] = (byte)('0' + value % 10);
            value /= 10;
        } while (value != 0);

        while (n > 0)
            uart_putc(buf[--n]);
    }

    static void PrintHex(ulong value)
    {
        Print("0x");
        for (int shift = 60; shift >= 0; shift -= 4)
            uart_putc((byte)"0123456789ABCDEF"[(int)((value >> shift) & 0xF)]);
    }

    [UnmanagedCallersOnly(EntryPoint = "kernel_main")]
    public static int Main()
    {
        Print("Hello from C#!\n");
        double total = 0;
        for (int i = 0; i < 10; i++)
            total += i * 0.5;
        if (total > 20)
            Print("Float OK\n");

        PrintDec(10);

        PrintHex(0xDEADBEEF);

        return 42;
    }
}