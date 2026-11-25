using System.Runtime.InteropServices;

public static unsafe class Cpu
{
    [DllImport("*")] public static extern void cpu_install_vectors();
    [DllImport("*")] public static extern void cpu_breakpoint();
    [DllImport("*")] public static extern ulong cpu_fault_address();
    [DllImport("*")] public static extern ulong cpu_syscall(ulong number, ulong arg);
    [DllImport("*")] public static extern void cpu_irq_enable();
    [DllImport("*")] public static extern void cpu_irq_disable();
    [DllImport("*")] public static extern void cpu_wait();
    [DllImport("*")] public static extern ulong timer_frequency();
    [DllImport("*")] public static extern void timer_start(ulong ticks);
}