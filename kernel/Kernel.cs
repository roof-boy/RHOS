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

    static Point saved;
    static Shape myShape;

    [UnmanagedCallersOnly(EntryPoint = "kernel_main")]
    public static int Main(void* heapStart, ulong heapSize)
    {
        Internal.Runtime.Allocator.Init(heapStart, heapSize);

        PrintHex((ulong)Internal.Runtime.Allocator.Alloc(5));
        Print("\n");
        PrintHex((ulong)Internal.Runtime.Allocator.Alloc(16)); Print("\n");
        Print("\n");

        saved = new Point(3, 4);
        PrintDec(saved.Sum());

        myShape = new Square(4);
        PrintDec(myShape.Area());
        PrintDec(myShape.Sides());

        return 42;
    }
}

class Point
{
    public int X, Y;
    public Point(int x, int y) { X = x; Y = y; }
    public int Sum() => X + Y;
}

abstract class Shape
{
    public abstract int Area();
    public virtual int Sides() => 0;
}

class Square : Shape
{
    int side;
    public Square(int side) { this.side = side; }
    public override int Area() => side * side;
    public override int Sides() => 4;
}