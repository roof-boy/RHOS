using System;
using System.Runtime.InteropServices;

public static unsafe class Kernel
{
    // boot/main.c calls this after setting up the UART.
    [UnmanagedCallersOnly(EntryPoint = "kernel_main")]
    public static int Main(void* heapStart, ulong heapSize)
    {
        Internal.Runtime.Allocator.Init(heapStart, heapSize);

        Console.WriteLine("RHOS Kernel started");
        Console.Write("Heap: ");
        Console.WriteHex((ulong)heapStart);
        Console.WriteLine(", " + Internal.Runtime.Allocator.Free / 1024 + " KB free");

        Console.WriteLine("Syscall below: ");
        Cpu.cpu_syscall(Syscalls.Print, 1234);
        Console.WriteLine("Syscall finished. ");

        return 0;
    }
}