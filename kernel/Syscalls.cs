using System;

public static class Syscalls
{
    public const ulong Print = 1;
    public static ulong Handle(ulong number, ulong arg)
    {
        if (number == Print)
        {
            Console.WriteLine("syscall print: " + arg);
            return 0;
        }

        Console.WriteLine("unknown syscall " + number);
        return ulong.MaxValue;
    }
}