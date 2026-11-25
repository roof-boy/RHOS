// A tick every 10 ms
public static class Timer
{
    public const ulong TicksPerSecond = 100;

    static ulong _interval;
    static ulong _ticks;

    public static ulong Ticks => _ticks;
    public static ulong Milliseconds => _ticks * (1000 / TicksPerSecond);

    public static void Init()
    {
        _interval = Cpu.timer_frequency() / TicksPerSecond;
        Cpu.timer_start(_interval);
        Interrupts.EnableTimer();
    }

    public static void Sleep(ulong milliseconds)
    {
        ulong until = Milliseconds + milliseconds;
        while (Milliseconds < until)
            Cpu.cpu_wait();
    }

    // runs inside the interrupt. Keep short.
    public static void OnTick()
    {
        _ticks++;
        Cpu.timer_start(_interval); // set the alarm again
    }
}