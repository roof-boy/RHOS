# Lesson 5: Exceptions, system calls and the timer

**Goal:** the kernel stops being a program that runs top to bottom, and
starts *reacting to events*: crashes, requests, and the passing of time.

```
RHOS kernel started
Breakpoint at 0x00000000000800D0
survived the breakpoint
syscall print: 1234
timer frequency: 62500000
second 1, ticks 100, uptime 1000 ms
second 2, ticks 200, uptime 2000 ms
second 3, ticks 300, uptime 3000 ms
now a bad memory access:

*** CPU exception: bad memory access at address 0xFFFF000000000000
    at code address 0x0000000000081A00, ESR 0x0000000096000000

PANIC: unrecoverable exception
```

This is the most important lesson so far. Everything an OS does later
(switching between programs, running user code safely, drivers) is built on
what you write here.

**This lesson is in four parts**, each ending with something that runs and a
⏸️ **good place to stop**.

| Part | You build | Time |
|------|-----------|------|
| 1. The vector table | The CPU calls your C# when something happens | 45 minutes |
| 2. Crashes | Bad memory accesses get reported, not ignored | 15 minutes |
| 3. System calls | Code asks the kernel for things with `svc` | 20 minutes |
| 4. The timer | 100 interrupts per second, and `Timer.Sleep` | 45 minutes |

**First:** `git pull`. You'll create these files:

```
boot/vectors.S        the vector table (assembly)
boot/cpu.S            small assembly functions C# can call
kernel/Cpu.cs         C# declarations for those functions
kernel/Exceptions.cs  the C# exception handler
kernel/Syscalls.cs    (part 3)
kernel/Interrupts.cs  (part 4)
kernel/Timer.cs       (part 4)
```

The Makefile already picks up every `.S` file in `boot/` and every `.cs` file
in `kernel/`, so there's nothing to add to it.

---

## The big idea: the CPU can interrupt itself

So far the CPU does exactly what your code says, one instruction after
another. But sometimes it has to stop and do something else:

| Event | Example | C# comparison |
|-------|---------|---------------|
| **An error** | reading from an address where no memory exists | a crash |
| **A request** | the `svc` instruction: "kernel, please do X" | calling an API |
| **An interrupt** | the timer says "10 ms have passed" | an event firing |

ARM calls all of these **exceptions**. That's a CPU word, not the C#
`Exception` class. A CPU exception is closer to an **event handler**: when
the event happens, the CPU drops what it's doing, jumps to your handler, and
afterwards continues exactly where it left off. The interrupted code never
notices.

How does the CPU know where your handler is? You give it a table of
addresses up front, the **vector table**. "Vector" is an old word for "the
address to jump to". It's like ASP.NET routing: for each kind of event, the
table says which code runs.

---

## Part 1: The vector table

### What the table looks like

The table has **16 slots**. The CPU picks a slot by asking two questions:
*what happened* (4 kinds) and *what was running* (4 situations).

|  | sync | irq | fiq | serror |
|--|------|-----|-----|--------|
| kernel, on the user stack | 0 | 1 | 2 | 3 |
| **kernel, on its own stack** | **4** | **5** | 6 | 7 |
| user program (64-bit) | 8 | 9 | 10 | 11 |
| user program (32-bit) | 12 | 13 | 14 | 15 |

- **sync** ("synchronous") means *the instruction itself* caused it: a crash,
  a breakpoint, a system call.
- **irq** is a normal interrupt from a device, like the timer.
- **fiq** (fast interrupt) and **serror** (a hardware error) we won't use.

Today our kernel is the only code running, so only **slots 4 and 5** ever
happen. The user-program rows matter in Lesson 7.

Each slot is 128 bytes (`0x80`) of code, and the whole table must start at
an address that's a multiple of 2048. The CPU doesn't read addresses out of
the table: it **jumps straight into the slot** and runs the code there.

### What a handler must do

When an exception happens, the CPU only saves two things for you:

- **ELR** (Exception Link Register): the address of the interrupted
  instruction, so we can go back.
- **SPSR** (Saved Program Status Register): the CPU's state flags at that
  moment.

Every other register (`x0`–`x30`, and the FP/SIMD registers `q0`–`q31`)
still holds whatever the interrupted code was using. Our handler is C#, which
will happily use those registers. So the handler must:

1. **Save** all registers to the stack. This block of saved registers is
   called a **trap frame**.
2. **Call** the C# handler, passing it a pointer to the trap frame.
3. **Restore** all registers from the trap frame.
4. **`eret`** ("exception return"): jump back to ELR with the state in SPSR.

That's the same `eret` you used in Lesson 2 to drop from EL2 to EL1. It was an
exception return all along: the firmware started us "inside" an EL2
exception, and we returned from it into EL1.

### The trap frame

This is the layout we'll use. Each `x` register is 8 bytes, each `q` register
16 bytes:

```
offset   0   x0
offset   8   x1
  ...
offset 240   x30
offset 248   ELR    where to return to
offset 256   SPSR   CPU state to return to
offset 264   ESR    why the exception happened (more in part 2)
offset 272   q0 .. q31   (32 × 16 = 512 bytes)
total: 784 bytes
```

In C# the same layout is a struct. Since the fields are in the same order,
a `TrapFrame*` pointer lets C# read and **change** the saved registers.
Changing them is how a handler sends results back: whatever is in the trap
frame when we restore is what the interrupted code sees.

### Write `boot/vectors.S`

This is the longest assembly you've written, but it's mostly the same line
repeated. Three new instructions:

- `stp a, b, [sp, #16]` ("store pair") stores two registers at
  `sp + 16` and `sp + 24`. `ldp` loads them back.
- `mrs x1, elr_el1` reads a system register (you've seen this), `msr` writes
  one.
- `.align 7` pads with zeros until the address is a multiple of 2⁷ = 128, so
  each slot starts at the right place.

`.macro` is assembly's copy-paste: `VENTRY 4` pastes the macro's body with
`\kind` replaced by `4`.

```asm
// The exception vector table: where the CPU jumps when something
// "exceptional" happens (an interrupt, a system call, a crash).
//
//                          sync   irq   fiq   serror
//   EL1, using SP_EL0        0     1     2     3
//   EL1, using SP_EL1        4     5     6     7    <- the kernel (us, now)
//   EL0 (user program)       8     9    10    11    <- user programs, later
//   EL0, 32-bit             12    13    14    15

// Must match TrapFrame in kernel/Exceptions.cs.
#define FRAME_SIZE (272 + 32 * 16)

// Each slot: make room for the trap frame, save x0 and x1, put the slot
// number in x0, jump to the common code.
.macro VENTRY kind
    .align 7
    sub     sp, sp, #FRAME_SIZE
    stp     x0, x1, [sp, #0]
    mov     x0, #\kind
    b       exception_common
.endm

.section .text
.align 11                           // 2^11 = 2048
.global exception_vectors
exception_vectors:
    VENTRY 0
    VENTRY 1
    VENTRY 2
    VENTRY 3
    VENTRY 4
    VENTRY 5
    VENTRY 6
    VENTRY 7
    VENTRY 8
    VENTRY 9
    VENTRY 10
    VENTRY 11
    VENTRY 12
    VENTRY 13
    VENTRY 14
    VENTRY 15

exception_common:
    // 1. Save the registers. x0 and x1 are already saved by the slot.
    stp     x2, x3, [sp, #16]
    stp     x4, x5, [sp, #32]
    stp     x6, x7, [sp, #48]
    stp     x8, x9, [sp, #64]
    stp     x10, x11, [sp, #80]
    stp     x12, x13, [sp, #96]
    stp     x14, x15, [sp, #112]
    stp     x16, x17, [sp, #128]
    stp     x18, x19, [sp, #144]
    stp     x20, x21, [sp, #160]
    stp     x22, x23, [sp, #176]
    stp     x24, x25, [sp, #192]
    stp     x26, x27, [sp, #208]
    stp     x28, x29, [sp, #224]
    mrs     x1, elr_el1
    stp     x30, x1, [sp, #240]
    mrs     x2, spsr_el1
    mrs     x3, esr_el1
    stp     x2, x3, [sp, #256]

    add     x1, sp, #272            // x1 = where the q registers go
    stp     q0, q1, [x1, #0]
    stp     q2, q3, [x1, #32]
    stp     q4, q5, [x1, #64]
    stp     q6, q7, [x1, #96]
    stp     q8, q9, [x1, #128]
    stp     q10, q11, [x1, #160]
    stp     q12, q13, [x1, #192]
    stp     q14, q15, [x1, #224]
    stp     q16, q17, [x1, #256]
    stp     q18, q19, [x1, #288]
    stp     q20, q21, [x1, #320]
    stp     q22, q23, [x1, #352]
    stp     q24, q25, [x1, #384]
    stp     q26, q27, [x1, #416]
    stp     q28, q29, [x1, #448]
    stp     q30, q31, [x1, #480]

    // 2. Call C#: exception_handler(int kind, TrapFrame* frame)
    //    x0 = kind (set by the slot), x1 = frame (= sp)
    mov     x1, sp
    bl      exception_handler

    // 3. Restore everything, in reverse.
    add     x1, sp, #272
    ldp     q0, q1, [x1, #0]
    ldp     q2, q3, [x1, #32]
    ldp     q4, q5, [x1, #64]
    ldp     q6, q7, [x1, #96]
    ldp     q8, q9, [x1, #128]
    ldp     q10, q11, [x1, #160]
    ldp     q12, q13, [x1, #192]
    ldp     q14, q15, [x1, #224]
    ldp     q16, q17, [x1, #256]
    ldp     q18, q19, [x1, #288]
    ldp     q20, q21, [x1, #320]
    ldp     q22, q23, [x1, #352]
    ldp     q24, q25, [x1, #384]
    ldp     q26, q27, [x1, #416]
    ldp     q28, q29, [x1, #448]
    ldp     q30, q31, [x1, #480]

    ldp     x30, x1, [sp, #240]
    msr     elr_el1, x1
    ldp     x2, x3, [sp, #256]
    msr     spsr_el1, x2

    ldp     x2, x3, [sp, #16]
    ldp     x4, x5, [sp, #32]
    ldp     x6, x7, [sp, #48]
    ldp     x8, x9, [sp, #64]
    ldp     x10, x11, [sp, #80]
    ldp     x12, x13, [sp, #96]
    ldp     x14, x15, [sp, #112]
    ldp     x16, x17, [sp, #128]
    ldp     x18, x19, [sp, #144]
    ldp     x20, x21, [sp, #160]
    ldp     x22, x23, [sp, #176]
    ldp     x24, x25, [sp, #192]
    ldp     x26, x27, [sp, #208]
    ldp     x28, x29, [sp, #224]
    ldp     x0, x1, [sp, #0]
    add     sp, sp, #FRAME_SIZE

    // 4. Back to where we were.
    eret
```

Typing this out is fine, but copying it is fine too: the ideas matter more
than the typing. Make sure you can answer *why* each of the 4 steps is there.

> **Why the order in step 3 matters:** we use `x1`, `x2` and `x3` as scratch
> space to restore ELR and SPSR, so we must do that *before* restoring the
> real `x1`–`x3`. And `x0`/`x1` come back last because `x1` is used as a
> pointer to the q registers until then.

### Write `boot/cpu.S`

C# can't run instructions like `msr`, so we wrap each one in a tiny assembly
function that C# calls with `[DllImport("*")]`, exactly like `uart_putc`.
Start with two:

```asm
// Tiny functions for CPU instructions that C# can't express.
// Arguments arrive in x0, x1...; the return value goes in x0.

.section .text

// void cpu_install_vectors(void): tell the CPU where our table is.
.global cpu_install_vectors
cpu_install_vectors:
    adr     x0, exception_vectors
    msr     vbar_el1, x0            // Vector Base Address Register
    isb                             // make sure it takes effect now
    ret

// void cpu_breakpoint(void): cause an exception on purpose.
.global cpu_breakpoint
cpu_breakpoint:
    brk     #0
    ret
```

`brk` is the "breakpoint" instruction. A debugger normally uses it, but with
no debugger it simply causes a sync exception. A perfect first test.

### Write `kernel/Cpu.cs`

```csharp
using System.Runtime.InteropServices;

// The assembly functions from boot/cpu.S, callable from C#.
public static unsafe class Cpu
{
    [DllImport("*")] public static extern void cpu_install_vectors();
    [DllImport("*")] public static extern void cpu_breakpoint();
}
```

### Write `kernel/Exceptions.cs`

Now the C# side: the trap frame struct, and the handler that
`exception_common` calls with `bl exception_handler`.

One new register: **ESR** (Exception Syndrome Register) says *why* the
exception happened. Its top 6 bits are the **exception class**. `brk` is
class `0x3C`.

```csharp
using System;
using System.Runtime.InteropServices;

// The registers boot/vectors.S saved, in the same order.
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
    // Exception classes: the top 6 bits of ESR.
    const ulong EC_BRK = 0x3C;          // brk instruction: a breakpoint

    public static void Init() => Cpu.cpu_install_vectors();

    // boot/vectors.S calls this for every exception.
    // kind is the slot number: 4 = sync, 5 = irq (see the table there).
    [UnmanagedCallersOnly(EntryPoint = "exception_handler")]
    static void Handle(int kind, TrapFrame* frame)
    {
        if (kind == 4)
            HandleSync(frame);
        else
            Crash("unexpected exception kind " + kind, frame);
    }

    static void HandleSync(TrapFrame* frame)
    {
        ulong ec = frame->Esr >> 26;

        if (ec == EC_BRK)
        {
            Console.WriteLine("Breakpoint at " + Hex(frame->Elr));
            // ELR points AT the brk. Skip it (every instruction is 4 bytes),
            // or we'd return to it and hit it again, forever.
            frame->Elr += 4;
        }
        else
        {
            Crash("unknown exception, class " + Hex(ec), frame);
        }
    }

    static void Crash(string reason, TrapFrame* frame)
    {
        Console.WriteLine();
        Console.WriteLine("*** CPU exception: " + reason);
        Console.WriteLine("    at code address " + Hex(frame->Elr) + ", ESR " + Hex(frame->Esr));
        Internal.Runtime.Panic.Halt("unrecoverable exception");
    }

    static string Hex(ulong value) => Internal.Runtime.Number.FormatHex(value);
}
```

Look at `frame->Elr += 4`. That one line changes where the CPU goes after
`eret`. The handler controls what happens next, and that's the power an OS
has over the code it runs.

### Try it

Replace the body of `Main` in `kernel/Kernel.cs` (keep the Shape classes if
you like, or delete them):

```csharp
[UnmanagedCallersOnly(EntryPoint = "kernel_main")]
public static int Main(void* heapStart, ulong heapSize)
{
    Internal.Runtime.Allocator.Init(heapStart, heapSize);
    Console.WriteLine("RHOS kernel started");

    Exceptions.Init();
    Cpu.cpu_breakpoint();
    Console.WriteLine("survived the breakpoint");

    return 0;
}
```

`make run`:

```
RHOS kernel started
Breakpoint at 0x00000000000800D0
survived the breakpoint
```

(Your address may differ.) The CPU hit `brk`, jumped to slot 4, your assembly
saved everything, your C# ran, and the kernel carried on as if nothing
happened.

**Check:** run `llvm-nm build/kernel8.elf | grep cpu_breakpoint`. It prints
the address of the `cpu_breakpoint` function. Compare it with the breakpoint
address. Why are they the same?

<details>
<summary>Answer</summary>

`brk` is the very first instruction of `cpu_breakpoint`, so the address of
the function *is* the address of the `brk`. ELR points at the instruction that
caused the exception.

</details>

**Experiment:** remove the `frame->Elr += 4;` line and run again. What
happens, and why? (Put it back afterwards.)

<details>
<summary>Answer</summary>

"Breakpoint at ..." prints forever. Without the `+= 4`, `eret` returns to the
`brk` itself, which causes the exception again, which returns to the `brk`...

</details>

⏸️ **Good place to stop.**

---

## Part 2: Crashes

In Lesson 4 you saw that reading through `null` doesn't crash on our Pi: the
memory at address 0 exists, so the read just works and returns junk. That's
because there's no memory protection yet (the MMU, a later lesson).

But some addresses don't exist at all. The Pi 3 has 1 GB of RAM plus some
device registers, all below `0x4100_0000`. Read from `0xFFFF_0000_0000_0000`,
and there's nothing there to answer. The CPU raises a sync exception with
class `0x25`, a **data abort**. A second register, **FAR** (Fault Address
Register), holds the address that was accessed.

A data abort usually means a bug, and we can't "skip" a read that returned
nothing, so the handler reports it and stops. That's what a "blue screen" or
"kernel panic" is on a real OS: the exception handler for an unrecoverable
exception.

### Add `cpu_fault_address`

In `boot/cpu.S`:

```asm
// ulong cpu_fault_address(void): the address that caused a data abort.
.global cpu_fault_address
cpu_fault_address:
    mrs     x0, far_el1
    ret
```

In `kernel/Cpu.cs`:

```csharp
[DllImport("*")] public static extern ulong cpu_fault_address();
```

### Handle it

In `Exceptions.cs`, add the constant next to `EC_BRK`:

```csharp
const ulong EC_DATA_ABORT = 0x25;   // bad memory access
```

and a branch in `HandleSync`, before the final `else`:

```csharp
else if (ec == EC_DATA_ABORT)
{
    Crash("bad memory access at address " + Hex(Cpu.cpu_fault_address()), frame);
}
```

### Try it

At the end of `Main`, before `return 0;`:

```csharp
Console.WriteLine("now a bad memory access:");
ulong x = *(ulong*)0xFFFF000000000000;
Console.WriteLine("never printed " + x);
```

```
now a bad memory access:

*** CPU exception: bad memory access at address 0xFFFF000000000000
    at code address 0x0000000000081A00, ESR 0x0000000096000000

PANIC: unrecoverable exception
```

The ESR is `0x96000000`. Shift it right by 26 and you get `0x25`. (Windows
Calculator in Programmer mode has a `>>` button.)

**Your turn:** jumping to a bad address (instead of reading one) is class
`0x21`, an **instruction abort**. FAR holds the bad address there too. Add a
constant and a branch for it that crashes with
`"jumped to bad address " + ...`. You can't easily test this one from C#, so
just make it compile.

<details>
<summary>Answer</summary>

```csharp
const ulong EC_INSTRUCTION_ABORT = 0x21;

// in HandleSync:
else if (ec == EC_INSTRUCTION_ABORT)
{
    Crash("jumped to bad address " + Hex(Cpu.cpu_fault_address()), frame);
}
```

</details>

⏸️ **Good place to stop.**

---

## Part 3: System calls

Right now all code is kernel code: it can do anything. Later (Lesson 7), the
shell and your programs will run as **user programs** at EL0, where they
can't touch hardware or kernel memory. So how does a program print text?
It **asks the kernel**.

The asking is done with one instruction: **`svc`** ("supervisor call"). It
causes a sync exception (class `0x15`), and the kernel's handler does the
work. It's a function call into the kernel, with the exception mechanism as
the doorway. On Linux, every `write`, `open` or `read` is an `svc`.

We need a convention for "which request, with what arguments". We'll copy
Linux:

| Register | Meaning |
|----------|---------|
| `x8` | the request number ("syscall number") |
| `x0` | the first argument |
| `x0` (on return) | the result |

And here the trap frame pays off: the handler reads the arguments **from the
trap frame**, and writes the result **into the trap frame's X0**. When the
registers are restored, the caller finds the result in `x0`, which is where
every function returns its result.

Unlike `brk`, after an `svc` the ELR already points at the *next*
instruction, so there's no `+= 4` needed.

### Add `cpu_syscall`

In `boot/cpu.S`:

```asm
// ulong cpu_syscall(ulong number, ulong arg): ask the kernel for something.
.global cpu_syscall
cpu_syscall:
    mov     x8, x0                  // syscall number goes in x8
    mov     x0, x1                  // first argument in x0
    svc     #0
    ret                             // the result is in x0
```

In `kernel/Cpu.cs`:

```csharp
[DllImport("*")] public static extern ulong cpu_syscall(ulong number, ulong arg);
```

### Write `kernel/Syscalls.cs`

```csharp
using System;

// System calls: how programs ask the kernel to do things for them.
// Today the kernel calls itself; once user programs exist they'll use the
// exact same path.
public static class Syscalls
{
    public const ulong Print = 1;      // print a number

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
```

### Connect it

In `Exceptions.cs`, add the constant:

```csharp
const ulong EC_SVC = 0x15;          // svc instruction: a system call
```

and the branch in `HandleSync`:

```csharp
if (ec == EC_SVC)
{
    // Arguments come from the saved registers, the result goes back into X0.
    frame->X0 = Syscalls.Handle(frame->X8, frame->X0);
}
else if (ec == EC_BRK)
...
```

### Try it

In `Main`, after the breakpoint:

```csharp
Cpu.cpu_syscall(Syscalls.Print, 1234);
```

```
syscall print: 1234
```

**Your turn:** call `Cpu.cpu_syscall(99, 0)` and print what it returns.

<details>
<summary>Answer</summary>

It prints `unknown syscall 99`, then the returned value: `ulong.MaxValue`,
which is `18446744073709551615`. The handler wrote that into the trap frame's
X0, and that's what `cpu_syscall` returned.

</details>

⏸️ **Good place to stop.**

---

## Part 4: The timer

The kernel can now react to things *the code* does. The last piece is
reacting to things that happen *on their own*: an **interrupt** (IRQ).

### The plan

Every ARM core has a built-in **generic timer**: a counter that goes up at a
fixed rate since power-on, plus an alarm. You set the alarm to "N counts from
now", and when it goes off, the timer raises an interrupt. We'll set it to
go off 100 times per second. Each time, the handler adds 1 to a `ticks`
counter and sets the alarm again.

That gives the kernel a sense of time. Later, the same tick is what lets the
kernel switch between programs: "you've had your 10 ms, next!"

### Three switches must all be on

An interrupt has a long way to travel, and it's blocked at three points. All
three must be open:

```
 generic timer  ──►  local interrupt   ──►  CPU's IRQ mask  ──►  slot 5
 (alarm on?)         controller              (DAIF I bit)
                     (timer routed?)
```

1. **The timer itself**: alarm enabled, interrupt not masked. Set with the
   system registers `cntp_tval_el0` (countdown) and `cntp_ctl_el0` (control).
2. **The local interrupt controller**: a small chip on the Pi at `0x40000000`
   that decides which interrupts reach which core. You enable the timer's
   line for core 0 by writing bit 1 at `0x40000040`. It's memory mapped,
   exactly like the UART registers in Lesson 2.
3. **The CPU's mask**: the `DAIF` register has an "ignore IRQs" bit (`I`).
   It's set at boot, so the CPU ignores every interrupt until we clear it with
   `msr daifclr, #2`.

> The local interrupt controller is at `0x40000000`, not in the usual
> `0x3F000000` peripheral area. It belongs to the ARM cores, while everything
> at `0x3F...` belongs to the GPU side of the chip.

### Add the timer and IRQ functions

In `boot/cpu.S`:

```asm
// Interrupts on/off. DAIF are four "ignore" bits: D(ebug) A(bort) I(RQ) F(IQ).
// daifclr clears bits (stop ignoring), daifset sets them. #2 = the I bit.
.global cpu_irq_enable
cpu_irq_enable:
    msr     daifclr, #2
    ret

.global cpu_irq_disable
cpu_irq_disable:
    msr     daifset, #2
    ret

// void cpu_wait(void): sleep until an interrupt arrives.
.global cpu_wait
cpu_wait:
    wfi                             // "wait for interrupt"
    ret

// ---- The ARM generic timer ----

// ulong timer_frequency(void): how many counts per second
.global timer_frequency
timer_frequency:
    mrs     x0, cntfrq_el0
    ret

// void timer_start(ulong ticks): raise an interrupt `ticks` counts from now
.global timer_start
timer_start:
    msr     cntp_tval_el0, x0       // the countdown
    mov     x0, #1
    msr     cntp_ctl_el0, x0        // bit 0 = enable, bit 1 = mask (0 = don't)
    ret
```

In `kernel/Cpu.cs`:

```csharp
[DllImport("*")] public static extern void cpu_irq_enable();
[DllImport("*")] public static extern void cpu_irq_disable();
[DllImport("*")] public static extern void cpu_wait();
[DllImport("*")] public static extern ulong timer_frequency();
[DllImport("*")] public static extern void timer_start(ulong ticks);
```

### Write `kernel/Interrupts.cs`

C# can read and write device registers with pointers, the same way your C
`mmio_write` does:

```csharp
// The Pi 3's local interrupt controller (one per chip, shared by the 4 cores).
public static unsafe class Interrupts
{
    // Core 0: which timer interrupts are allowed through.
    static uint* Core0TimerControl => (uint*)0x40000040;
    // Core 0: which interrupts are waiting right now.
    static uint* Core0Source => (uint*)0x40000060;

    const uint PhysicalTimer = 1 << 1;

    public static void EnableTimer() => *Core0TimerControl = PhysicalTimer;

    // Called from Exceptions.Handle for every IRQ.
    public static void Handle()
    {
        uint pending = *Core0Source;
        if ((pending & PhysicalTimer) != 0)
            Timer.OnTick();
    }
}
```

`Handle` asks the controller "who's interrupting?" Today the answer is always
the timer, but soon the UART will interrupt too (when a key is pressed), and
this is where they'll be told apart.

### Write `kernel/Timer.cs`

```csharp
// A tick every 10 ms.
public static class Timer
{
    public const ulong TicksPerSecond = 100;

    static ulong _interval;     // timer counts between two ticks
    static ulong _ticks;

    public static ulong Ticks => _ticks;
    public static ulong Milliseconds => _ticks * (1000 / TicksPerSecond);

    public static void Init()
    {
        _interval = Cpu.timer_frequency() / TicksPerSecond;
        Cpu.timer_start(_interval);
        Interrupts.EnableTimer();
    }

    // Runs inside the interrupt: keep it short.
    public static void OnTick()
    {
        _ticks++;
        Cpu.timer_start(_interval);     // set the alarm again
    }
}
```

### Connect it

In `Exceptions.Handle`, route slot 5 (IRQ) to the new code:

```csharp
static void Handle(int kind, TrapFrame* frame)
{
    if (kind == 5)
        Interrupts.Handle();
    else if (kind == 4)
        HandleSync(frame);
    else
        Crash("unexpected exception kind " + kind, frame);
}
```

Notice there's no `Elr` change for an IRQ: the interrupted instruction never
ran, so we return to it and it runs now.

### Try it

In `Main`, after the syscall (and before the bad memory access, if you kept
it):

```csharp
Console.WriteLine("timer frequency: " + Cpu.timer_frequency());
Timer.Init();
Cpu.cpu_irq_enable();

while (Timer.Ticks < 300)
    Cpu.cpu_wait();
Console.WriteLine("3 seconds passed, ticks " + Timer.Ticks);
```

```
timer frequency: 62500000
3 seconds passed, ticks 300
```

The timer counts 62.5 million times per second, so the alarm is set to
625,000 counts for a 10 ms tick. The message should appear after about 3
real seconds. (QEMU isn't a perfect clock, so it might be a little off.)

`cpu_wait` runs `wfi`, which **pauses the CPU** until an interrupt arrives.
Without it, the loop would spin at 100% CPU just checking a number. A real
OS does exactly this when it has nothing to do: that's what the "System Idle
Process" in Windows Task Manager is.

### Your turn: `Timer.Sleep`

Add `public static void Sleep(ulong milliseconds)` to `Timer`: it returns
after that many milliseconds have passed. Then replace the `while` loop in
`Main` with a loop that prints a line every second for 3 seconds:

```
second 1, ticks 100
second 2, ticks 200
second 3, ticks 300
```

<details>
<summary>Answer</summary>

```csharp
public static void Sleep(ulong milliseconds)
{
    ulong until = Milliseconds + milliseconds;
    while (Milliseconds < until)
        Cpu.cpu_wait();             // sleep until the next interrupt
}
```

```csharp
for (int i = 1; i <= 3; i++)
{
    Timer.Sleep(1000);
    Console.WriteLine("second " + i + ", ticks " + Timer.Ticks);
}
```

</details>

### Bonus: an "uptime" system call

Add syscall number 2, `Uptime`, that returns `Timer.Milliseconds`, and print
its result in your loop. This is your first system call that *returns*
something useful.

<details>
<summary>Answer</summary>

```csharp
public const ulong Uptime = 2;     // milliseconds since boot

// in Handle:
if (number == Uptime)
    return Timer.Milliseconds;
```

```csharp
Console.WriteLine("second " + i + ", ticks " + Timer.Ticks +
                  ", uptime " + Cpu.cpu_syscall(Syscalls.Uptime, 0) + " ms");
```

</details>

⏸️ **Done!** Commit your work.

---

## Checkpoint

1. What's the difference between a *sync* exception and an *IRQ*?
2. Why does the vector code save `q0`–`q31` and not only `x0`–`x30`?
3. After a `brk` we add 4 to ELR, after an `svc` we don't, and after an IRQ
   we don't. Why?
4. How does a system call's result get back to the caller?
5. The timer interrupt fires, but `Timer.Ticks` stays 0. Name the three
   places to check.

<details>
<summary>Answers</summary>

1. A sync exception is caused by the instruction that was running (`brk`,
   `svc`, a bad memory access). An IRQ comes from a device, at any moment,
   unrelated to the current instruction.
2. An IRQ can interrupt *any* code, including C# code using the FP/SIMD
   registers (ILC uses them for copying memory, for example). Our handler is
   C# too and may use them, so they must be saved and restored, or the
   interrupted code finds its values changed.
3. For `brk`, ELR points at the `brk` itself, so returning there would hit it
   again. For `svc`, the CPU already sets ELR to the next instruction. For an
   IRQ, the instruction at ELR hasn't run yet, so it must run now.
4. The handler writes it into the trap frame's X0. The restore code loads it
   into `x0`, where the caller expects a function's return value.
5. The timer (`cntp_ctl_el0` enabled, unmasked), the local interrupt
   controller (bit 1 at `0x40000040`), and the CPU's mask (`daifclr, #2`).

</details>

---

## What's next

Your kernel now has the three ways in that every OS kernel has: crashes,
requests, and interrupts. Next up:

- **Keyboard input by interrupt**: instead of `uart_getc` spinning, the UART
  interrupts when a key arrives. Then a first **shell prompt**.
- **The MMU**: memory protection, so `null` finally crashes and user programs
  can't touch the kernel.
