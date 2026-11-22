# Lesson 2: Booting to "Hello" over UART

**Goal:** make the emulated Pi print a message that *your* code sends, and
echo back what you type:

```
Hello from RHOS!
Exception level: 0x0000000000000001
hi
```

**How this lesson works:** there are 10 small steps. Each one adds **one new
idea** and ends with something you can run and see. You'll see a letter
on screen at step 3. Don't try to understand everything up front; each idea
is explained at the moment you need it.

If a step doesn't work, that's normal. `make debug` + `make gdb` (Lesson 1)
lets you step through it one instruction at a time.

**Time:** take it in sittings: steps 1–5, then 6–9, then 10.

---

## Before you start: four ideas you need

### Memory is one big `byte[]`

Picture the Pi's RAM as one huge array, `byte[] memory`. An **address** is
just an index into that array. That's all an address is.

When Lesson 1 said "the firmware loads `kernel8.img` at `0x80000`", it meant
"it copies the file into `memory[0x80000]`, `memory[0x80001]`, and so on".

### Hex is just another way to write numbers

`0x80000` is a number written in hexadecimal (base 16). In C# you write it the
same way: `int x = 0x80000;` is 524288. Low-level code uses hex because each
hex digit is exactly 4 bits, so it's easy to see which bits are set. You never
need to convert hex in your head; use a calculator (Windows Calculator has a
"Programmer" mode).

### A number is a row of on/off switches (bits)

A `uint` (`uint32_t` in C) is 32 bits, numbered 0 (rightmost) to 31. Hardware
gives *meaning* to individual bits, like a C# `[Flags]` enum:

```csharp
[Flags] enum Status { RxEmpty = 1 << 4, TxFull = 1 << 5 }
```

`1 << 5` means "a number with only bit 5 set" (= 32 = `0x20`). You'll see this
constantly. The operators are the same in C and C#: `|` combines bits, `&`
tests them, `~` flips them all.

### CPU registers are the CPU's local variables

You met these in Lesson 1: `x0` to `x30`. The CPU can only do arithmetic on
values in registers, so code constantly moves values between memory and
registers. Keep this in mind, because the word "register" comes back in step 3
with a *second* meaning.

---

## Step 1: Tell the linker where things go

**New idea: the linker script.**

In Lesson 1 you linked with `-Ttext=0x80000`. Real kernels use a **linker
script** instead: a small file that tells the linker how to lay out the final
program in memory. Think of it as a seating plan: "code sits here, then
constants, then variables."

Create `boot/linker.ld`:

```ld
ENTRY(_start)

SECTIONS
{
    . = 0x80000;

    .text : {
        KEEP(*(.text.boot))
        *(.text .text.*)
    }
    .rodata : { *(.rodata .rodata.*) }
    .data   : { *(.data .data.*) }
    .bss    : { *(.bss .bss.*) *(COMMON) }
}
```

Line by line:

- `ENTRY(_start)`: the program starts at the label `_start`. (GDB uses this.)
- `. = 0x80000;`: `.` means "the current address". Start placing things at `0x80000`.
- `.text`: the **code**. `.text.boot` (where `_start` will live) goes **first**,
  because the firmware jumps to the very first byte. Then all other code.
- `.rodata`: **r**ead-**o**nly data, like string literals (`"Hello"`).
- `.data`: global variables that have a starting value (`int x = 5;`).
- `.bss`: global variables that start at zero (`int x;`). More on this in step 10.

These four names (`.text`, `.rodata`, `.data`, `.bss`) are standard. The
compiler puts every function and variable into one of them; the linker script
decides where each group goes.

You can't build yet (there's no code). That's step 2.

## Step 2: The first instruction

**New idea: the stack.**

Your C# programs have a call stack. You've seen it in the Visual Studio "Call
Stack" window. Every method call stores its local variables and its "where do
I return to" address there. C works the same way.

On bare metal, **nobody has created a stack for you**. It's just a register,
`sp` (stack pointer), holding an address. Before calling any C function, you
point `sp` at some free memory.

Which memory? The stack **grows downward** (towards smaller addresses), and
our code starts at `0x80000`. Everything below `0x80000` is free, so we set
`sp = 0x80000` and let the stack grow down into that space.

Create `boot/start.S`:

```asm
.section .text.boot
.global _start
_start:
    mov     x1, #0x80000      // x1 = 0x80000
    mov     sp, x1            // sp = x1   (the stack starts here, grows down)
    bl      boot_main         // call the C function boot_main()
sleep_forever:
    wfe                       // if boot_main ever returns, sleep forever
    b       sleep_forever
```

The only new instruction is `bl` ("branch with link"), which is a function
call. `.section .text.boot` puts this code in the section the linker script
places first.

You'll write `boot_main` in step 3.

## Step 3: Make the letter "A" appear

**New idea: hardware lives at addresses (MMIO).**

The Pi's designers made some addresses **not be memory**. Instead, they are
wired to devices. Everything from `0x3F000000` upward is "device land":
reading or writing there talks to a piece of hardware. This is called
**memory-mapped I/O**, or **MMIO**.

The closest thing in C# is a **property with side effects**:

```csharp
uart.Data = 'A';     // the setter physically sends 'A' down a wire
var f = uart.Flags;  // the getter asks the device for its current status
```

The device we'll use is the **UART**, the simplest way to send text out of a
computer: bytes go out one bit at a time over a wire. In QEMU, that wire is
connected to your terminal. Our UART lives at address `0x3F201000`.

A device exposes a handful of these special addresses, each 4 bytes wide, at
fixed distances from the device's starting address. **Each of these is called
a register.** This is the second meaning of "register": not the CPU's `x0`, but
a 4-byte slot that belongs to a device. For the UART:

| Name | Address | What it does |
|------|---------|--------------|
| `DR` (data) | `0x3F201000` | Write a byte here and the UART sends it |
| `CR` (control) | `0x3F201030` | Settings, including on/off switches |

Create `boot/main.c`:

```c
#include <stdint.h>

void boot_main(void)
{
    *(volatile uint32_t *)0x3F201030 = 0x301;   // switch the UART on
    *(volatile uint32_t *)0x3F201000 = 'A';     // send the letter A
}
```

Then:

```bash
make run
```

You should see `A`. Quit QEMU with **Ctrl-A, then X**.

**That line of C, piece by piece** (read it right to left):

```c
*(volatile uint32_t *)0x3F201000 = 'A';
```

- `0x3F201000`: a plain number.
- `(uint32_t *)`: "treat this number as the address of a `uint32_t`". This is a
  **pointer**, exactly like an `unsafe` C# pointer: `(uint*)0x3F201000`.
- `volatile`: "this is hardware, really do this write, don't optimise it".
  Step 4 explains why it matters.
- `*` at the front: "the thing at that address". So `* ... = 'A'` writes `'A'`
  into that address.

`uint32_t` comes from `<stdint.h>` and is C's name for `uint`. `0x301` turns on
3 switches (bits 0, 8 and 9: "UART on", "sending on", "receiving on"). You'll
name them properly in step 8.

**You just wrote your first device driver.** Everything else in this lesson
makes it neater and more correct.

**Try it:** change `'A'` to another letter. Send two letters by adding a
second line.

## Step 4: Give things names

**New ideas: `#define`, header files, and `volatile`.**

Raw addresses are hard to read. C has three tools to clean this up.

**`#define`** is find-and-replace that happens before compiling. After
`#define UART_DR 0x3F201000`, writing `UART_DR` is the same as writing the
number. It's like a C# `const`.

**Header files (`.h`)** are where you put things that several `.c` files share.
In C, each `.c` file is compiled on its own and knows nothing about the others.
If `main.c` wants to call a function in `uart.c`, it needs to see that
function's **declaration** (its signature, with no body), just like a C#
interface lists method signatures without code. You put declarations in a `.h`
file and `#include` it, which pastes the file in.

**`volatile`.** The compiler likes to optimise: if your code reads the same
memory twice with nothing in between, it may decide to read it once and reuse
the value. For normal variables that's fine. For a device it's wrong, because
the device's status can change between reads. `volatile` tells the compiler:
"every read and write here really matters, do all of them". Rule of thumb:
**every access to a device address goes through `volatile`.** That's easy to
guarantee if you only ever touch devices through two helper functions.

Create `boot/include/mmio.h`:

```c
#pragma once
#include <stdint.h>

#define MMIO_BASE 0x3F000000UL

static inline void mmio_write(uintptr_t address, uint32_t value)
{
    *(volatile uint32_t *)address = value;
}

static inline uint32_t mmio_read(uintptr_t address)
{
    return *(volatile uint32_t *)address;
}
```

- `#pragma once`: "only include this file once", even if included twice.
- `uintptr_t`: an integer big enough to hold an address (like C# `nuint`).
- `static inline`: "copy this small function into wherever it's called".
  Normal for tiny helpers in headers.
- `UL` on a number means "unsigned long", so it's treated as a 64-bit number.

**Your task:** create `boot/uart.c` with a `uart_init()` that switches the UART
on and a `uart_putc(char c)` that sends one character, using `mmio_write` and
`#define`s for the two addresses. Create `boot/include/uart.h` declaring both
functions. Change `boot_main` to `#include "uart.h"`, call `uart_init()`, then
`uart_putc('A')`.

The Makefile finds new `.c` files on its own, so `make run` just works.

<details>
<summary>Solution</summary>

`boot/include/uart.h`:

```c
#pragma once
void uart_init(void);
void uart_putc(char c);
```

`boot/uart.c`:

```c
#include "mmio.h"
#include "uart.h"

#define UART_BASE (MMIO_BASE + 0x201000)
#define UART_DR   (UART_BASE + 0x00)
#define UART_CR   (UART_BASE + 0x30)

void uart_init(void)
{
    mmio_write(UART_CR, 0x301);
}

void uart_putc(char c)
{
    mmio_write(UART_DR, (uint32_t)c);
}
```

`boot/main.c`:

```c
#include "uart.h"

void boot_main(void)
{
    uart_init();
    uart_putc('A');
}
```

Writing addresses as "base + offset" (`UART_BASE + 0x30`) is how hardware
manuals list them, and it makes the next device easier to add.

</details>

## Step 5: Print a whole string

**New idea: strings in C.**

C has no `string` class. A string is just bytes in memory, one per character,
**ending with a 0 byte**. `"Hi"` is stored as 3 bytes: `'H'`, `'i'`, `0`.
There is no `.Length`; to find the end, you walk forward until you hit the 0.

A function receives a string as a pointer to its first character:
`const char *s`. Then:

- `*s` is the current character.
- `s++` moves the pointer to the next character.

**Your task:** add `void uart_puts(const char *s)` to `uart.c` (and `uart.h`)
that sends every character until the 0. Then print `"Hello from RHOS!"` from
`boot_main`.

You'll notice the next prompt or output starts on the same line. Terminals want
`"\r\n"` (go to the start of the line, then down a line) where C# would use
`"\n"`. Make `uart_putc` send a `'\r'` first whenever it's asked to send `'\n'`,
and then `uart_puts("Hello from RHOS!\n")` ends the line properly.

<details>
<summary>Solution</summary>

```c
void uart_putc(char c)
{
    if (c == '\n')
        uart_putc('\r');
    mmio_write(UART_DR, (uint32_t)c);
}

void uart_puts(const char *s)
{
    while (*s != 0) {
        uart_putc(*s);
        s++;
    }
}
```

(Yes, `uart_putc` calls itself for the `'\r'`. That's fine; it only goes one
level deep.)

</details>

## Step 6: Don't send faster than the wire

**New idea: status bits, and waiting on hardware.**

The UART sends bits at a fixed speed, much slower than the CPU can write. So
it has a small **queue** inside it (16 bytes, called a **FIFO**: "first in,
first out", like a C# `Queue<byte>`). Writing to `DR` adds a byte to the queue;
the hardware sends them one by one.

If the queue is full, a new byte is lost. QEMU's queue never fills up, but a
real Pi's does. So before writing, check whether it's full.

The UART reports its status in the **flags register**, `FR`, at offset `0x18`.
It's a `[Flags]`-style value; the bit you need is:

| Bit | Name | Meaning when it's 1 |
|-----|------|---------------------|
| 5 | `TXFF` | **T**ransmit **F**IFO **F**ull: don't write yet |

To test a bit, use `&`, just like C#:

```c
if (mmio_read(UART_FR) & (1u << 5)) {
    // bit 5 is set: the queue is full
}
```

(`1u` means "1, as an unsigned number". Use `u` on bit masks to avoid surprises.)

**Your task:** at the start of `uart_putc`, wait **while** the queue is full.
Add `#define`s for `UART_FR` and for the bit, so the code reads well.

<details>
<summary>Solution</summary>

```c
#define UART_FR    (UART_BASE + 0x18)
#define FR_TX_FULL (1u << 5)

void uart_putc(char c)
{
    if (c == '\n')
        uart_putc('\r');
    while (mmio_read(UART_FR) & FR_TX_FULL) {
        // wait
    }
    mmio_write(UART_DR, (uint32_t)c);
}
```

A loop that does nothing but check a status is called **busy waiting** or
**polling**. It's simple and fine for now. In Lesson 5, interrupts let the
hardware tell *us* when it's ready instead.

</details>

## Step 7: Listen for keys

**New idea: receiving is the same thing in reverse.**

When you type in the terminal, the bytes arrive in the UART's **receive**
queue. Reading `DR` takes one out. Another bit in `FR` says whether there's
anything to read:

| Bit | Name | Meaning when it's 1 |
|-----|------|---------------------|
| 4 | `RXFE` | **R**eceive **F**IFO **E**mpty: nothing to read yet |

**Your task:** write `char uart_getc(void)` that waits while the receive queue
is empty, then reads `DR` and returns the character. When you press Enter, the
terminal sends `'\r'`; turn that into `'\n'` before returning it, so the rest of
your code only ever deals with `'\n'`.

Then make `boot_main` print the greeting and **echo** forever: read a
character, send it back, repeat. `while (1) { ... }` is C's `while (true)`.

<details>
<summary>Solution</summary>

```c
#define FR_RX_EMPTY (1u << 4)

char uart_getc(void)
{
    while (mmio_read(UART_FR) & FR_RX_EMPTY) {
        // wait for a key
    }
    char c = (char)mmio_read(UART_DR);
    if (c == '\r')
        c = '\n';
    return c;
}
```

```c
void boot_main(void)
{
    uart_init();
    uart_puts("Hello from RHOS!\n");
    while (1) {
        char c = uart_getc();
        uart_putc(c);
    }
}
```

</details>

**Checkpoint:** you now have a working text console. Everything after this
makes it correct on real hardware, and makes the boot code safe.

## Step 8: Set the UART up properly (for real hardware)

**New ideas: baud rate, and the GPIO pins.**

QEMU is forgiving: it worked with only "switch on". A real Pi needs three
more things. You won't see a difference in QEMU, so read this step for
understanding, then write the code.

### 1. Speed: the baud rate

A UART has no shared clock with the other end. Both sides simply agree, in
advance, how fast to send: **so many bits per second**. That speed is the
**baud rate**. We use **115200** (a standard speed). If the two ends disagree,
you get garbage characters on screen. It's like two people agreeing to read a
message aloud at a fixed pace.

The UART can't be told "115200" directly. It has an internal clock that ticks
48 million times a second, and you tell it **how many ticks per bit** (it
actually checks each bit 16 times, hence the 16):

```
48,000,000 ÷ (16 × 115,200) = 26.04
```

The whole part (**26**) goes in register `IBRD`. The fractional part goes in
`FBRD`, stored as 64ths: 0.04 × 64 ≈ **3**. You don't need to redo this maths;
just know where the numbers came from.

### 2. Data format

Register `LCRH` sets the format of each byte: we want 8 bits per character
and the queues switched on. That's the value **`0x70`** (bits 4, 5 and 6).

### 3. Connect the UART to the pins

The Pi's pin header has about 40 pins, and most of them can be connected to
one of several internal devices. A **GPIO** ("general-purpose input/output")
controller decides which device each pin is connected to, like a switchboard.

The UART's wires are **pins 14 and 15**. By default they're not connected to our
UART, so we connect them by writing to the GPIO register `GPFSEL1` at
`0x3F200004`. That register holds **3 bits per pin** for pins 10–19. Pin 14's 3
bits start at bit `(14 − 10) × 3 = 12`, and pin 15's at bit 15. The value that
means "connect to the UART" is `0b100` (= 4).

The trick is changing **only those 3 bits** without disturbing the other pins'
bits. It's a read-modify-write, the same thing you'd do to change one flag in a
`[Flags]` enum:

```c
uint32_t sel = mmio_read(GPFSEL1);
sel &= ~(7u << 12);   // clear pin 14's 3 bits  (7 = 0b111)
sel |=  (4u << 12);   // set them to 0b100
// ...the same for pin 15 at bit 15...
mmio_write(GPFSEL1, sel);
```

There's also a small ritual to switch off the pins' built-in "pull" resistors
(which hold an unconnected pin at a default level and would interfere). You
write `GPPUD`, wait a moment, write `GPPUDCLK0`, wait, write `GPPUDCLK0` again.
That's straight from the chip's manual; there's no deeper meaning to learn here.

### The full recipe

1. Switch the UART **off** (`CR = 0`) while changing settings.
2. Connect pins 14 and 15 to it (`GPFSEL1`).
3. Turn off the pin resistors (`GPPUD`, `GPPUDCLK0`).
4. Clear any old pending events (`ICR = 0x7FF`).
5. Set the speed (`IBRD = 26`, `FBRD = 3`).
6. Set the format (`LCRH = 0x70`).
7. Switch the UART **on**, with sending and receiving (`CR = 0x301`).

All the addresses:

| Register | Address | |
|----------|---------|--|
| `GPFSEL1` | `MMIO_BASE + 0x200004` | Pin function select, pins 10–19 |
| `GPPUD` | `MMIO_BASE + 0x200094` | Pull resistor setting |
| `GPPUDCLK0` | `MMIO_BASE + 0x200098` | Apply pull setting to selected pins |
| `IBRD` | `UART_BASE + 0x24` | Speed, whole part |
| `FBRD` | `UART_BASE + 0x28` | Speed, fractional part |
| `LCRH` | `UART_BASE + 0x2C` | Data format |
| `ICR` | `UART_BASE + 0x44` | Clear pending events |

**Your task:** add a `delay(count)` helper to `mmio.h` that loops `count`
times doing nothing, then rewrite `uart_init` to follow the recipe. In
`delay`, use `__asm__ volatile("nop");` inside the loop. `nop` is the
"do nothing" instruction, and the `volatile` stops the compiler deleting your
empty loop.

<details>
<summary>Solution</summary>

`mmio.h`:

```c
static inline void delay(uint32_t count)
{
    while (count--)
        __asm__ volatile("nop");
}
```

`uart.c`:

```c
#define GPFSEL1   (MMIO_BASE + 0x200004)
#define GPPUD     (MMIO_BASE + 0x200094)
#define GPPUDCLK0 (MMIO_BASE + 0x200098)

#define UART_IBRD (UART_BASE + 0x24)
#define UART_FBRD (UART_BASE + 0x28)
#define UART_LCRH (UART_BASE + 0x2C)
#define UART_ICR  (UART_BASE + 0x44)

void uart_init(void)
{
    mmio_write(UART_CR, 0);                  // 1. off

    uint32_t sel = mmio_read(GPFSEL1);       // 2. pins 14, 15 -> UART
    sel &= ~(7u << 12);
    sel |=  (4u << 12);
    sel &= ~(7u << 15);
    sel |=  (4u << 15);
    mmio_write(GPFSEL1, sel);

    mmio_write(GPPUD, 0);                    // 3. pull resistors off
    delay(150);
    mmio_write(GPPUDCLK0, (1u << 14) | (1u << 15));
    delay(150);
    mmio_write(GPPUDCLK0, 0);

    mmio_write(UART_ICR, 0x7FF);             // 4. clear events
    mmio_write(UART_IBRD, 26);               // 5. 115200 baud
    mmio_write(UART_FBRD, 3);
    mmio_write(UART_LCRH, 0x70);             // 6. 8 bits, queues on
    mmio_write(UART_CR, 0x301);              // 7. on, send + receive
}
```

</details>

## Step 9: Print numbers

**New idea: printing a number as hex.**

There's no `ToString()` yet. To print a number, you turn it into characters
yourself. Hex is the easiest: each hex digit is exactly 4 bits, so a 64-bit
number is 16 digits.

To get the digits from left to right, shift the number right by 60, 56, 52, …
down to 0, and each time keep only the lowest 4 bits (`& 0xF`). That gives a
value from 0 to 15, which you use as an index into `"0123456789ABCDEF"`.

**Your task:** write `void uart_puthex(uint64_t value)` that prints `0x`
followed by 16 hex digits. (`uint64_t` is C's `ulong`.) Test it with
`uart_puthex(255)`; you should see `0x00000000000000FF`.

<details>
<summary>Solution</summary>

```c
void uart_puthex(uint64_t value)
{
    const char *digits = "0123456789ABCDEF";
    uart_puts("0x");
    for (int shift = 60; shift >= 0; shift -= 4) {
        uint64_t nibble = (value >> shift) & 0xF;
        uart_putc(digits[nibble]);
    }
}
```

Remember to add `#include <stdint.h>` and the declaration to `uart.h`.

</details>

## Step 10: Make the boot code safe

Your kernel works, but `_start` skips three jobs that real hardware needs.
Each is a few lines of assembly. Add them to `start.S` **before** setting up
the stack, in this order.

New instructions for this step (you don't need to memorise them):

| Instruction | Meaning |
|-------------|---------|
| `mrs x1, NAME` | Read a CPU setting (a "system register") into `x1` |
| `msr NAME, x1` | Write `x1` into a CPU setting |
| `and x1, x1, #0xFF` | `x1 = x1 & 0xFF` |
| `lsr x1, x1, #2` | `x1 = x1 >> 2` |
| `cmp x1, #2` then `b.ne label` | `if (x1 != 2) goto label` |
| `cbnz x1, label` | `if (x1 != 0) goto label` |
| `ldr x1, =name` | `x1 = the address of name` |
| `str xzr, [x1], #8` | write 8 zero bytes at address `x1`, then `x1 += 8` |
| `b.hs label` | after `cmp`: `if (x1 >= x2) goto label` |

### 10a. Only core 0 continues

The Pi has 4 cores. On some boot setups, **all four** start running your code
at once, all using the same stack. That's like four threads running `Main`
simultaneously with no locks. QEMU and current firmware park cores 1–3 before
your code runs (you saw that in Lesson 1), but we check anyway, because it's 3
instructions.

Each core can ask "which core am I?" by reading the CPU setting `MPIDR_EL1`.
The lowest 8 bits are the core number.

```asm
_start:
    mrs     x1, mpidr_el1     // which core am I?
    and     x1, x1, #0xFF     // keep the core number (lowest 8 bits)
    cbnz    x1, sleep_forever // not core 0? go to sleep
```

### 10b. Zero the `.bss` section

Global variables without a starting value (`uint64_t boot_count;`) go in
`.bss`, and C promises they start at 0. In C# the runtime zeroes memory for you.
Here, nobody does: the image file doesn't contain those zeros (it would waste
space), so **you** write them.

First, have the linker script tell you where `.bss` starts and ends. Replace the
`.bss` line in `linker.ld` with:

```ld
    . = ALIGN(16);
    __bss_start = .;
    .bss : { *(.bss .bss.*) *(COMMON) }
    . = ALIGN(16);
    __bss_end = .;
```

`__bss_start = .;` creates a label at the current address that assembly can
use. `ALIGN(16)` rounds up to a multiple of 16, so the size is a multiple of 8
and the loop below can write 8 bytes at a time.

Then, in `start.S` after setting up the stack and before `bl boot_main`, this
loop is the assembly version of
`for (p = __bss_start; p < __bss_end; p += 8) *p = 0;`:

```asm
    ldr     x1, =__bss_start
    ldr     x2, =__bss_end
zero_loop:
    cmp     x1, x2
    b.hs    zero_done         // stop when x1 >= x2
    str     xzr, [x1], #8     // write 8 zero bytes, move on 8
    b       zero_loop
zero_done:
```

(`xzr` is a special register that always reads as 0.)

### 10c. Drop from EL2 to EL1

Lesson 1 showed your code starts in **EL2**, the hypervisor level. Kernels run
in **EL1**. Staying in EL2 would make later lessons (interrupts, memory
protection) work differently from every manual and example.

There's no "go to EL1" instruction. The CPU only lowers its level when it
**returns** from handling an exception (think: returning from an interrupt
handler). So we **fake a return**: we fill in "where to return to" and "what
level to return into", then execute `eret` ("exception return").

It's like this C#:

```csharp
// pretend we were called from EL1, then "return" there
returnAddress = in_el1;
returnLevel   = EL1;
return;        // eret
```

The code, with a comment on every line. You can treat the 4 setup values as
"magic numbers from the manual" for now; each one is explained in Lesson 5 when
it starts to matter.

```asm
    mrs     x1, CurrentEL      // which level are we in?
    lsr     x1, x1, #2         // the level number is in bits 2-3
    cmp     x1, #2
    b.ne    in_el1             // not EL2? nothing to do

    mov     x1, #(1 << 31)     // EL1 should run in 64-bit mode
    msr     hcr_el2, x1
    mov     x1, #3             // let EL1 use the timer (needed in Lesson 5)
    msr     cnthctl_el2, x1
    msr     cntvoff_el2, xzr
    ldr     x1, =0x30D00800    // EL1 settings: memory protection off, for now
    msr     sctlr_el1, x1

    mov     x1, #0x3C5         // "return into": EL1, interrupts off
    msr     spsr_el2, x1
    adr     x1, in_el1         // "return to": the label in_el1
    msr     elr_el2, x1
    eret                       // "return" -> now running in_el1, in EL1

in_el1:
    // ...set up the stack here (from step 2), then zero .bss, then bl boot_main
```

Note the stack setup goes **after** `in_el1`: each level has its own `sp`, so
you set it once you're in EL1.

**Your task:** assemble 10a, 10c and 10b into `start.S` in this order:
check core → drop to EL1 → `in_el1:` → stack → zero `.bss` → call C → sleep.
Then print the exception level from `boot_main` to prove it worked:

```c
uint64_t el;
__asm__ volatile("mrs %0, CurrentEL" : "=r"(el));   // read CurrentEL into el
uart_puts("Exception level: ");
uart_puthex(el >> 2);
uart_puts("\n");
```

That `__asm__` line runs one assembly instruction from C. You don't need to
learn the syntax; copy it when you need to read a CPU setting.

You should see `Exception level: 0x0000000000000001`. 🎉

<details>
<summary>Solution: complete start.S</summary>

```asm
.section .text.boot
.global _start
_start:
    // 10a. Only core 0 continues
    mrs     x1, mpidr_el1
    and     x1, x1, #0xFF
    cbnz    x1, sleep_forever

    // 10c. Drop from EL2 to EL1
    mrs     x1, CurrentEL
    lsr     x1, x1, #2
    cmp     x1, #2
    b.ne    in_el1

    mov     x1, #(1 << 31)
    msr     hcr_el2, x1
    mov     x1, #3
    msr     cnthctl_el2, x1
    msr     cntvoff_el2, xzr
    ldr     x1, =0x30D00800
    msr     sctlr_el1, x1
    mov     x1, #0x3C5
    msr     spsr_el2, x1
    adr     x1, in_el1
    msr     elr_el2, x1
    eret

in_el1:
    // Step 2. Stack
    mov     x1, #0x80000
    mov     sp, x1

    // 10b. Zero .bss
    ldr     x1, =__bss_start
    ldr     x2, =__bss_end
zero_loop:
    cmp     x1, x2
    b.hs    zero_done
    str     xzr, [x1], #8
    b       zero_loop
zero_done:

    bl      boot_main

sleep_forever:
    wfe
    b       sleep_forever
```

</details>

---

## Checkpoint

You're done with Lesson 2 when `make run` shows the greeting, `Exception level:
0x0000000000000001`, and echoes what you type.

You **don't** need to remember register addresses or bit numbers; nobody does.
You look them up. What's worth remembering is the shape of it:

1. Devices are controlled by reading and writing special addresses (MMIO).
2. Device registers are groups of bits, tested with `&` and set with `|`.
3. Every device access goes through `volatile`.
4. Before C can run, assembly must set up a stack and zero `.bss`.
5. Kernels run in EL1; we get there by faking an exception return.

Commit your work, then tell me how it went: which steps clicked, and which
didn't. Lesson 3 links a C# method into this kernel and calls it from
`boot_main`, and I'll pitch it based on what you tell me.

---

## Optional experiments

Do these if you're curious. None are needed for Lesson 3.

**A. See `volatile` matter.** In `mmio.h`, delete the word `volatile` from both
helpers, run `make && make disasm`, and look at `uart_putc` in
`build/kernel8.lst`. Then put `volatile` back and compare.

<details>
<summary>What you should see</summary>

Without `volatile`, the compiler reads `FR` **once**, and if the queue is full
it jumps to an instruction that jumps to itself forever (`b` to its own
address), never re-reading `FR`. With `volatile`, the read is inside the loop.
In QEMU both versions "work", because QEMU's queue never fills. On a real Pi,
the first one would freeze. That's why the rule is "always `volatile`".

</details>

**B. Is `.bss` really zero?** Add a global `uint64_t boot_count;` in `main.c`,
print it with `uart_puthex`, then increment it. Now comment out the zeroing
loop. What does it print, and why is that misleading?

<details>
<summary>Answer</summary>

It still prints 0, because QEMU starts with all memory zeroed. A real Pi after a
reboot has leftover data in RAM, so `boot_count` could be anything. "Works in
QEMU" doesn't always mean "correct". Keep the loop.

</details>

**C. A debug build.** The Makefile compiles with `-O2` (optimised), which makes
GDB jump around confusingly. Change `-O2` in `CFLAGS` to a variable,
`OPT ?= -O2`, so that `make clean && make OPT=-O0` gives an unoptimised build.

**D. `uart_printf`.** Write a tiny `printf` supporting `%s`, `%c` and `%x`. You'll
need `#include <stdarg.h>` (`va_list`, `va_start`, `va_arg`, `va_end`), which C
lets you use even on bare metal. It's C's version of `params object[] args`.

---

## Running on a real Pi 3 (once you have a serial cable)

The SD card's boot partition needs the firmware files from
[raspberrypi/firmware](https://github.com/raspberrypi/firmware/tree/master/boot)
(`bootcode.bin`, `start.elf`, `fixup.dat`), your `build/kernel8.img`, and a
`config.txt` containing:

```ini
arm_64bit=1
# Give our UART the pins 14/15 instead of Bluetooth
dtoverlay=disable-bt
# The 48 MHz UART clock our speed maths assumed
init_uart_clock=48000000
```

Then open the cable's COM port at 115200 baud (PuTTY on Windows). I haven't
tested this on a physical board yet; we'll confirm it together when you have one.

---

## Glossary

| Term | Plain meaning |
|------|---------------|
| **Address** | An index into memory, the big `byte[]` |
| **Hex** (`0x…`) | Base-16 numbers. One hex digit = 4 bits |
| **Bit** | One on/off switch inside a number. Bit 0 is the rightmost |
| **CPU register** | One of the CPU's built-in variables (`x0`–`x30`, `sp`) |
| **Device register** | A 4-byte slot at a fixed address that controls a device |
| **MMIO** | Memory-mapped I/O: controlling devices by reading/writing addresses |
| **Pointer** | A variable holding an address, like an `unsafe` C# pointer |
| **`volatile`** | "Every read/write of this really happens", needed for devices |
| **UART** | A device that sends/receives bytes one bit at a time over a wire |
| **Baud rate** | How many bits per second the UART sends; both ends must agree |
| **FIFO** | A small queue inside the UART (first in, first out) |
| **GPIO** | The controller that decides what each physical pin is connected to |
| **Stack** | Memory for local variables and return addresses; `sp` points at it |
| **`.text` / `.rodata` / `.data` / `.bss`** | Code / constants / globals with a value / globals starting at zero |
| **Linker script** | The "seating plan" that says where each section goes in memory |
| **Exception level (EL)** | CPU privilege: EL0 apps, EL1 kernel, EL2 hypervisor, EL3 firmware |
| **`eret`** | "Exception return": the only way to move to a lower EL |
