using System.Runtime.InteropServices;

public static unsafe class Cpu
{
    [DllImport("*")] public static extern void cpu_install_vectors();
    [DllImport("*")] public static extern void cpu_breakpoint();
    [DllImport("*")] public static extern ulong cpu_fault_address();
    [DllImport("*")] public static extern ulong cpu_syscall(ulong number, ulong arg);
}