using System;
using System.Runtime.InteropServices;

public static unsafe class Kernel
{
    // boot/main.c calls this after setting up the UART.
    [UnmanagedCallersOnly(EntryPoint = "kernel_main")]
    public static int Main(void* heapStart, ulong heapSize)
    {
        Internal.Runtime.Allocator.Init(heapStart, heapSize);
        Exceptions.Init();

        Console.WriteLine("RHOS Kernel started");
        Console.Write("Heap: ");
        Console.WriteHex((ulong)heapStart);
        Console.WriteLine(", " + Internal.Runtime.Allocator.Free / 1024 + " KB free");

        Console.WriteLine("Timer frequency: " + Cpu.timer_frequency());
        Timer.Init();
        Cpu.cpu_irq_enable();

        for (int i = 1; i <= 3; i++)
        {
            Timer.Sleep(1000);
            Console.WriteLine(Cpu.cpu_syscall(Syscalls.Uptime, 0));
            Console.WriteLine("second " + i + ", ticks " + Timer.Ticks);
        }

        return 0;
    }
}