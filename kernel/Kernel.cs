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
                uart_putc((byte)p[i]);
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "kernel_main")]
    public static int Main()
    {
        Print("Hello from C#!\n");
        return 42;
    }
}