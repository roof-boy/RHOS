// Pi 3 local interrupt controller (one per chip, shared by the 4 cores)
public static unsafe class Interrupts
{   
    // core 0: which timer interrupts are allowed through
    static uint* Core0TimerControl => (uint*)0x40000040;
    // core 0: which interrupts are waiting right now
    static uint* Core0Source => (uint*)0x40000060;

    const uint PhysicalTimer = 1 << 1;

    public static void EnableTimer() => *Core0TimerControl = PhysicalTimer;

    // handle IRQ exception
    public static void Handle()
    {
        uint pending = *Core0Source;
        if ((pending & PhysicalTimer) != 0)
            Timer.OnTick();
    }
}