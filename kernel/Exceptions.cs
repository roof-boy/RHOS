using System;
using System.Runtime.InteropServices;

public struct TrapFrame
{
    public ulong X0, X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, X13, X14, X15,
                 X16, X17, X18, X19, X20, X21, X22, X23, X24, X25, X26, X27, X28, X29, X30;
    public ulong Elr;   // where to continue afterwards
    public ulong Spsr;  // the CPU state to go back to
    public ulong Esr;   // "Exception Syndrome": why we're here
}

public static unsafe class Exceptions
{
    // Exception classes: top 6 bits of ESR
    const ulong EC_BRK = 0x3C;
    const ulong EC_DATA_ABORT = 0x25;
    const ulong EC_INST_ABORT = 0x21;
    const ulong EC_SVC = 0x15; // Syscall exception

    public static void Init() => Cpu.cpu_install_vectors();

    // gets called by boot/vectors.S for every exception
    // kind is the slot number: 4 = sync, 5 = irq
    [UnmanagedCallersOnly(EntryPoint = "exception_handler")]
    static void Handle(int kind, TrapFrame* frame)
    {
        if (kind == 5) // irq slot kind
            Interrupts.Handle();
        else if (kind == 4)
            HandleSync(frame);
        else
            Crash("unexpected exception kind " + kind, frame);
    }

    static void HandleSync(TrapFrame* frame)
    {
        ulong ec = frame->Esr >> 26;

        if (ec == EC_SVC)
        {
            // Arguments come from the saved registers, the result goes back into X0
            frame->X0 = Syscalls.Handle(frame->X8, frame->X0);
        }
        else if (ec == EC_BRK)
        {
            Console.WriteLine("Breakpoint at " + Hex(frame->Elr));

            // ELR points at the brk. Skip it or it gets into an infinite loop.
            frame->Elr += 4;
        }
        else if (ec == EC_DATA_ABORT)
        {
            Crash("bad memory access at address " + Hex(Cpu.cpu_fault_address()), frame);
        }
        else if (ec == EC_INST_ABORT)
        {
            Crash("bad instruction jump at address " + Hex(Cpu.cpu_fault_address()), frame);
        }
        else
        {
            Crash("unknown exception, class " + Hex(ec), frame);
        }
    }

    static void Crash(string reason, TrapFrame* frame)
    {
        Console.WriteLine();
        Console.WriteLine("*** CPU Exception: " + reason);
        Console.WriteLine("    at code address " + Hex(frame->Elr) + ", ESR " + Hex(frame->Esr));
        Internal.Runtime.Panic.Halt("unrecoverable exception");
    }

    static string Hex(ulong value) => Internal.Runtime.Number.FormatHex(value);
}