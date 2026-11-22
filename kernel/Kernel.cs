using System;
using System.Runtime.InteropServices;

public static unsafe class Kernel
{
    static Shape[] shapes;

    // boot/main.c calls this after setting up the UART.
    [UnmanagedCallersOnly(EntryPoint = "kernel_main")]
    public static int Main(void* heapStart, ulong heapSize)
    {
        Internal.Runtime.Allocator.Init(heapStart, heapSize);

        Console.WriteLine("Hello from C#!");
        Console.Write("Heap: ");
        Console.WriteHex((ulong)heapStart);
        Console.WriteLine(", " + Internal.Runtime.Allocator.Free / 1024 + " KB free");

        shapes = new Shape[] { new Square(3), new Rect(2, 5), new Square(4) };
        int area = 0;
        foreach (Shape s in shapes)
        {
            Console.WriteLine(s);
            area += s.Area();
        }
        Console.WriteLine("Total area: " + area);

        return 0;
    }
}

abstract class Shape
{
    public abstract int Area();
}

class Square : Shape
{
    int side;
    public Square(int side) { this.side = side; }
    public override int Area() => side * side;
    public override string ToString() => "Square " + side;
}

class Rect : Shape
{
    int w, h;
    public Rect(int w, int h) { this.w = w; this.h = h; }
    public override int Area() => w * h;
    public override string ToString() => "Rect " + w + "x" + h;
}
