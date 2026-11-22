# Lesson 2: Booting to "Hello" over UART

**Goal:** `make run` boots *your* code in QEMU and prints:

```
Hello from RHOS!
Exception level: 0x0000000000000001
```

and then echoes back whatever you type.

**You will write** (all in `boot/`):

| File | What it does |
|------|--------------|
| `linker.ld` | Places your code at `0x80000` and defines where `.bss` and the stack are |
| `start.S` | `_start`: parks cores 1–3, drops EL2 → EL1, sets up the stack, zeroes `.bss`, calls C |
| `include/mmio.h` | Helpers to read and write hardware registers |
| `uart.c` | A driver for the PL011 UART: init, send, receive |
| `main.c` | `boot_main()`: the first C function, prints the greeting |

The Makefile picks up every `.S` and `.c` in `boot/` automatically, so you
don't need to edit it.

**Time:** an afternoon or two. Do it in steps; each step has a way to check
it works before moving on.

---

## 1. C for C# developers

You'll write C in this lesson and the next, and assembly underneath it. C#
already gave you most of the syntax: `if`, `for`, `while`, operators, bit
twiddling and function calls all look the same. What's different:

**No classes, no GC, no runtime.** C has functions, `struct`s (like C# structs,
but with no methods), and pointers. Nothing is allocated unless you allocate it,
and on bare metal there's no `malloc` until you write one.

**Fixed-size integer types** come from `<stdint.h>`, which is one of the few
headers available in freestanding mode:

| C | C# |
|---|----|
| `uint8_t` / `int8_t` | `byte` / `sbyte` |
| `uint16_t` / `int16_t` | `ushort` / `short` |
| `uint32_t` / `int32_t` | `uint` / `int` |
| `uint64_t` / `int64_t` | `ulong` / `long` |
| `uintptr_t` | `nuint` (an integer big enough to hold a pointer) |
| `char` | `byte`, really. Not a UTF-16 `char` |

Avoid plain `int`/`long` for hardware work; their sizes are platform-dependent.

**Pointers** work like C# `unsafe` pointers. `uint32_t *p` is "address of a
`uint32_t`". `*p` reads or writes what it points to. `(uint32_t *)0x3F201000`
turns a number into a pointer, which is how you talk to hardware.

**Strings** are just `char` arrays ending in a `0` byte. `"Hi"` is three bytes:
`'H'`, `'i'`, `0`. There's no `.Length`; you walk until the zero.

**Headers and declarations.** C compiles each `.c` file separately and knows
nothing about other files. To call `uart_puts` from `main.c`, `main.c` must see
a *declaration*: `void uart_puts(const char *s);`. You put declarations in a
`.h` file and `#include` it. That's the source-level version of what you saw in
Lesson 1: the compiler leaves a hole, the linker fills it in.

**`#define`** is text substitution done before compiling: `#define UART0 0x3F201000`
replaces `UART0` everywhere. Use it for register addresses. `static inline`
functions in headers are the idiomatic way to write small helpers.

**`static`** on a function or global means "private to this file", roughly
`internal` scoped to one file.

**`volatile`** is the one you've probably never needed in C#. It tells the
compiler "every read and write of this memory matters, don't optimise them
away or reorder them." Hardware registers change on their own (the UART's
"busy" bit flips when a byte finishes sending), and writing to them has side
effects (writing a byte *sends* it). Without `volatile`, the compiler is allowed
to read a register once and assume it never changes. Exercise 6 shows this.

**Inline assembly** lets C execute one specific instruction:

```c
uint64_t el;
__asm__ volatile("mrs %0, CurrentEL" : "=r"(el));
```

reads the `CurrentEL` system register into `el`. `%0` is "the first operand",
and `"=r"(el)` means "an output, in any general register, store it in `el`".

## 2. Talking to hardware: MMIO

The Pi 3 has no special I/O instructions. Hardware devices appear as **memory
addresses**: reading or writing certain addresses talks to a device instead of
RAM. This is **memory-mapped I/O (MMIO)**.

On the Pi 3 (BCM2837):

| Physical address | What's there |
|------------------|--------------|
| `0x00000000`–`0x3EFFFFFF` | RAM (shared with the GPU; the split is set by firmware) |
| `0x3F000000`–`0x3FFFFFFF` | Peripherals: GPIO, UARTs, timers, interrupt controller… |
| `0x40000000`– | ARM-local peripherals (per-core timers and interrupts) |

> **Watch out for the datasheet.** Broadcom's *BCM2835 ARM Peripherals* PDF
> (the standard reference, and still correct for the Pi 3) lists addresses like
> `0x7E201000`. Those are **bus addresses** as the GPU sees them. For the ARM
> CPU, replace `0x7E` with `0x3F`: `0x7E201000` → `0x3F201000`.

Each device has **registers**, usually 32 bits wide, at fixed offsets from its
base address. A register is a set of **bit fields**. Writing a driver means
reading the datasheet's register tables and setting the right bits in the right
order. Two helpers do all the actual access:

```c
static inline void     mmio_write(uintptr_t reg, uint32_t value);
static inline uint32_t mmio_read(uintptr_t reg);
```

Both cast `reg` to a `volatile uint32_t *` and write or read through it.

## 3. The UART

A **UART** sends bytes one bit at a time over a wire. It's the simplest output
device there is, and it's what we'll use for logging forever, even after
there's a screen.

The Pi 3 has two: the **PL011** ("UART0", a full UART from ARM) and the **mini
UART** ("UART1", a cut-down one whose speed depends on the GPU clock). We use
the **PL011**:

- In QEMU it's wired to the first `-serial`, which the Makefile connects to your
  terminal.
- Its speed doesn't change when the GPU changes clock, which makes it more
  reliable on hardware.

On a real Pi 3, the PL011 is connected to the Bluetooth chip by default, and
the GPIO pins 14/15 get the mini UART. We fix that by setting the pins
ourselves in the driver (step C), plus a line in `config.txt` (section 5).

### Bringing up the PL011

There are two pieces of hardware to configure: the **GPIO** pins (so pins 14
and 15 are connected to the UART), then the **UART** itself.

**GPIO** (base `0x3F200000`):

| Register | Offset | What you do with it |
|----------|--------|---------------------|
| `GPFSEL1` | `0x04` | Pin function select for pins 10–19, 3 bits per pin. Pin *n*'s field is at bit `(n - 10) * 3`. Set pins 14 and 15 to **ALT0** = `0b100`. |
| `GPPUD` | `0x94` | Pull-up/down control. Write `0` = off. |
| `GPPUDCLK0` | `0x98` | Write `1` to bit *n* to apply `GPPUD` to pin *n*. |

Pull-up/down sequence (from the datasheet): write `GPPUD`, wait ~150 cycles,
write `GPPUDCLK0` with bits 14 and 15, wait ~150 cycles, write `GPPUDCLK0 = 0`.
A loop of `nop` instructions is fine for the wait.

**PL011** (base `0x3F201000`):

| Register | Offset | Bits you care about |
|----------|--------|---------------------|
| `DR` | `0x00` | Write a byte to send it; read to receive one |
| `FR` (flags) | `0x18` | bit 5 `TXFF`: transmit FIFO full. bit 4 `RXFE`: receive FIFO empty |
| `IBRD` | `0x24` | Integer part of the baud divisor |
| `FBRD` | `0x28` | Fractional part of the baud divisor (6 bits) |
| `LCRH` | `0x2C` | bit 4 `FEN`: enable FIFOs. bits 6:5 `WLEN`: word length, `0b11` = 8 bits |
| `CR` (control) | `0x30` | bit 0 `UARTEN`, bit 8 `TXE`, bit 9 `RXE` |
| `ICR` | `0x44` | Write `0x7FF` to clear all pending interrupts |

The order matters: **disable** the UART (`CR = 0`), set up GPIO, clear
interrupts, set the baud rate, set the line format, then **enable** it with
`UARTEN | TXE | RXE`. The PL011 only latches the baud rate when `LCRH` is
written, which is why `IBRD`/`FBRD` come first.

**Baud rate.** The divisor is `UART_clock / (16 × baud)`. The Pi 3 firmware
gives the PL011 a 48 MHz clock, and we want 115200 baud:

```
48,000,000 / (16 × 115,200) = 26.0416…
IBRD = 26
FBRD = round(0.0416… × 64) = 3
```

(QEMU ignores the baud rate entirely, but real hardware doesn't.)

**Sending** a byte: wait while `FR.TXFF` is set, then write the byte to `DR`.
**Receiving**: wait while `FR.RXFE` is set, then read `DR`.

Terminals expect `\r\n` line endings, so `uart_putc('\n')` should send `'\r'`
first. And when you type Enter, the terminal sends `'\r'`, so it's handy to turn
a received `'\r'` into `'\n'`.

## 4. The boot sequence, in assembly

`_start` has five jobs before it can call C. Here's each one, with the new
instructions it needs.

### New instructions

| Instruction | Meaning (C-ish) |
|-------------|-----------------|
| `mrs x1, REG` | `x1 = REG` for a **system register** (a CPU control register) |
| `msr REG, x1` | `REG = x1` |
| `and x1, x1, #0xFF` | `x1 &= 0xFF` |
| `lsr x1, x1, #2` | `x1 >>= 2` (logical shift right) |
| `cmp x1, x2` then `b.ne label` | `if (x1 != x2) goto label`. Also `b.eq`, `b.hs` (unsigned ≥), `b.lo` (unsigned <) |
| `cbnz x1, label` | `if (x1 != 0) goto label` (`cbz` for `== 0`) |
| `ldr x1, =symbol` | `x1 = address of symbol` (or any 64-bit constant) |
| `adr x1, label` | `x1 = address of label`, PC-relative (remember Lesson 1, Exercise 6) |
| `str xzr, [x1], #8` | `*(uint64_t *)x1 = 0; x1 += 8;` (`xzr` always reads as 0) |
| `mov sp, x1` | Set the stack pointer |
| `bl func` | Call `func` (return address goes into `x30`) |
| `eret` | "Exception return": jump to `ELR_ELx` and switch to the level in `SPSR_ELx` |

### Job 1: Park every core except core 0

`MPIDR_EL1` identifies the core; its low 8 bits (`Aff0`) are the core number,
0–3. If it isn't 0, jump to a `wfe` loop.

(You saw in Lesson 1 that QEMU and the real firmware already park cores 1–3, so
in practice only core 0 reaches `_start`. Check anyway: older firmware and other
boot paths start all four cores at `0x80000`, and four cores running your boot
code at once means four cores zeroing `.bss` and sharing one stack.)

### Job 2: Drop from EL2 to EL1

There is no "go to EL1" instruction. Instead you **pretend you're returning
from an exception that was taken from EL1**: fill in the registers an exception
would have saved, then `eret`. That's how you always move *down* a level.

Check `CurrentEL` first: bits 3:2 are the current level. If you're already at
EL1, skip ahead.

Then set:

| Register | Value | Why |
|----------|-------|-----|
| `HCR_EL2` | bit 31 (`RW`) = 1 | EL1 runs in 64-bit mode. Without it, EL1 would be AArch32 |
| `CNTHCTL_EL2` | `0b11` | Let EL1 read the timer and counter (needed in Lesson 5) |
| `CNTVOFF_EL2` | `0` | No offset on the virtual counter |
| `SCTLR_EL1` | `0x30D00800` | EL1's system control: MMU off, caches off, required-1 bits set. Its reset value is undefined, so set it explicitly |
| `SPSR_EL2` | `0x3C5` | The state to "return" to: `0b0101` = EL1h (EL1, own stack pointer), and `0x3C0` = interrupts masked (DAIF) |
| `ELR_EL2` | address of your EL1 code | Where to "return" to. Use `adr` |

Then `eret`. Execution continues at your EL1 label, now at EL1.

### Job 3: Set up the stack

C needs a stack for local variables and return addresses. It grows *down*, so
`sp` starts at the **top** of a reserved area. Get the address from a symbol the
linker script defines (`__stack_top`). AArch64 requires `sp` to be 16-byte aligned.

You can't `ldr sp, =...` directly; load into `x1` and `mov sp, x1`.

### Job 4: Zero `.bss`

`.bss` holds global and static variables that start at zero (in C,
`static int x;`). To keep the image small, the linker doesn't store those zeros
in `kernel8.img`; it only records where `.bss` starts and ends. C code assumes
they're zero, so **you** write the zeros, 8 bytes at a time, from `__bss_start`
to `__bss_end`.

### Job 5: Call C

`bl boot_main`. If it ever returns, fall through into the park loop so the core
doesn't run off into whatever memory follows.

## 5. The linker script

The linker script tells `ld.lld` where everything goes. It's in its own little
language. The pieces you need:

```ld
ENTRY(_start)              /* the entry point symbol, recorded in the ELF */

SECTIONS
{
    . = 0x80000;           /* "." is the current address. Start here. */

    .text : {              /* an output section called .text, containing... */
        KEEP(*(.text.boot))/* ..._start's section first (KEEP: never discard it) */
        *(.text .text.*)   /* ...then all code from every input file */
    }

    sym = .;               /* define a symbol at the current address */
    . = ALIGN(16);         /* round "." up to a multiple of 16 */
    . += 0x4000;           /* skip 16 KiB */
}
```

Your script needs, in order:

1. Start at `0x80000`.
2. `.text`, with `.text.boot` (where you put `_start`) **first**, because the
   firmware jumps to the first byte.
3. `.rodata` (constant data, like string literals), 16-byte aligned.
4. `.data` (initialised globals), 16-byte aligned.
5. `.bss`, 16-byte aligned, marked `(NOLOAD)`, wrapped in `__bss_start` and
   `__bss_end` symbols. Include `*(COMMON)` too. Align the end to 16 so your
   8-byte zeroing loop never overshoots.
6. A 16 KiB stack after `.bss`, with `__stack_top` at its top.
7. A `/DISCARD/ : { *(.comment) *(.note*) *(.eh_frame*) }` section to drop
   compiler metadata the kernel doesn't need.

In `start.S`, put `_start` in the right section with `.section .text.boot`.

## 6. Running on a real Pi 3 (when you have a cable)

QEMU is all you need for this lesson. For hardware later, the SD card's boot
partition needs the firmware files from
[raspberrypi/firmware](https://github.com/raspberrypi/firmware/tree/master/boot)
(`bootcode.bin`, `start.elf`, `fixup.dat`), your `kernel8.img`, and this
`config.txt`:

```ini
arm_64bit=1
# Give the PL011 to GPIO 14/15 instead of Bluetooth
dtoverlay=disable-bt
# PL011 clock our baud divisors assume
init_uart_clock=48000000
```

Then connect to the adapter at 115200 baud (on Windows, PuTTY on the adapter's
COM port). I haven't tested this config on a physical board yet; we'll confirm
it together when you have one.

---

## Exercises

Work through them in order. Each one adds a file or checks one step. If you get
stuck, `make debug` + `make gdb` and `stepi` is how you find out where.

> **Tip:** the Makefile builds with `-O2`, which makes C harder to follow in
> GDB because the compiler reorders and inlines code. Exercise 9 adds a switch
> for an unoptimised debug build; do it early if stepping through C gets confusing.

### Exercise 1: Linker script + the smallest `_start`

Write `boot/linker.ld` (section 5) and a `boot/start.S` that only does Job 1
(park non-zero cores) and then parks core 0 too. Run `make`, then:

- `llvm-objdump -d build/kernel8.elf`: is `_start` at `0x80000`?
- Open `build/kernel8.map`. Find `__bss_start`, `__bss_end` and `__stack_top`.
  Do the addresses make sense?

Boot it with `make debug` / `make gdb`, `break _start`, `continue`, and step
through the core check.

<details>
<summary>Solution: linker.ld</summary>

```ld
ENTRY(_start)

SECTIONS
{
    . = 0x80000;
    __kernel_start = .;

    .text : {
        KEEP(*(.text.boot))
        *(.text .text.*)
    }
    .rodata : ALIGN(16) { *(.rodata .rodata.*) }
    .data   : ALIGN(16) { *(.data .data.*) }
    .bss (NOLOAD) : ALIGN(16) {
        __bss_start = .;
        *(.bss .bss.*)
        *(COMMON)
        . = ALIGN(16);
        __bss_end = .;
    }

    . = ALIGN(16);
    . += 0x4000;              /* 16 KiB boot stack, grows down */
    __stack_top = .;
    __kernel_end = .;

    /DISCARD/ : { *(.comment) *(.note*) *(.eh_frame*) }
}
```

</details>

<details>
<summary>Solution: the core check</summary>

```asm
.section .text.boot
.global _start
_start:
    mrs     x1, mpidr_el1
    and     x1, x1, #0xFF
    cbnz    x1, park
    // core 0 continues here (for now, also parks)
park:
    wfe
    b       park
```

</details>

### Exercise 2: Drop to EL1

Add Job 2. In GDB, step over the `eret` and check `p/x $cpsr`. The low nibble
should go from `9` (EL2h) to `5` (EL1h).

Question: why `adr` for `ELR_EL2` rather than `ldr =`? (Hint: Lesson 1,
Exercise 6.) Does it matter right now?

<details>
<summary>Solution</summary>

```asm
    mrs     x1, CurrentEL
    lsr     x1, x1, #2
    cmp     x1, #2
    b.ne    el1_entry         // not in EL2: already where we want to be

    mov     x1, #(1 << 31)    // HCR_EL2.RW: EL1 is AArch64
    msr     hcr_el2, x1
    mov     x1, #3            // EL1 may use the physical timer and counter
    msr     cnthctl_el2, x1
    msr     cntvoff_el2, xzr
    ldr     x1, =0x30D00800   // SCTLR_EL1: MMU/caches off, RES1 bits set
    msr     sctlr_el1, x1
    mov     x1, #0x3C5        // EL1h, DAIF masked
    msr     spsr_el2, x1
    adr     x1, el1_entry
    msr     elr_el2, x1
    eret

el1_entry:
```

`adr` is PC-relative, so it's correct even if the code runs at a different
address from the one it was linked for. Right now link and load addresses match,
so `ldr =` would work too. It starts to matter when the MMU arrives in Lesson 5
and the boot code has to work both before and after virtual addresses are on.

</details>

### Exercise 3: Stack, `.bss`, and calling C

Add Jobs 3–5, and a `boot/main.c` with:

```c
void boot_main(void)
{
    for (;;) { }
}
```

In GDB, `break boot_main`, continue, and check `p/x $sp`. It should equal
`__stack_top` (`p &__stack_top`).

<details>
<summary>Solution</summary>

```asm
el1_entry:
    ldr     x1, =__stack_top
    mov     sp, x1

    ldr     x1, =__bss_start
    ldr     x2, =__bss_end
1:  cmp     x1, x2
    b.hs    2f                // done when x1 >= x2
    str     xzr, [x1], #8
    b       1b
2:
    bl      boot_main
    // falls through into park if boot_main returns
```

</details>

### Exercise 4: MMIO helpers and the UART driver

Write `boot/include/mmio.h` (with `MMIO_BASE`, `mmio_write`, `mmio_read`, and a
`delay(n)` that spins `n` `nop`s), then `boot/uart.c` with:

```c
void uart_init(void);
void uart_putc(char c);
void uart_puts(const char *s);
```

and a `boot/include/uart.h` that declares them. Call `uart_init()` and
`uart_puts("Hello from RHOS!\n")` from `boot_main`, and `make run`.

`Ctrl-A` then `X` quits QEMU.

Try writing the bit manipulation for `GPFSEL1` yourself before looking. You
need to clear 3 bits for each pin, then set them to `0b100`, without touching
the other pins' bits.

<details>
<summary>Solution: mmio.h</summary>

```c
#pragma once
#include <stdint.h>

#define MMIO_BASE 0x3F000000UL

static inline void mmio_write(uintptr_t reg, uint32_t value)
{
    *(volatile uint32_t *)reg = value;
}

static inline uint32_t mmio_read(uintptr_t reg)
{
    return *(volatile uint32_t *)reg;
}

static inline void delay(uint32_t cycles)
{
    while (cycles--)
        __asm__ volatile("nop");
}
```

</details>

<details>
<summary>Solution: uart.c</summary>

```c
#include "mmio.h"
#include "uart.h"

#define GPFSEL1   (MMIO_BASE + 0x200004)
#define GPPUD     (MMIO_BASE + 0x200094)
#define GPPUDCLK0 (MMIO_BASE + 0x200098)

#define UART0     (MMIO_BASE + 0x201000)
#define UART_DR   (UART0 + 0x00)
#define UART_FR   (UART0 + 0x18)
#define UART_IBRD (UART0 + 0x24)
#define UART_FBRD (UART0 + 0x28)
#define UART_LCRH (UART0 + 0x2C)
#define UART_CR   (UART0 + 0x30)
#define UART_ICR  (UART0 + 0x44)

#define FR_RXFE   (1u << 4)
#define FR_TXFF   (1u << 5)

void uart_init(void)
{
    mmio_write(UART_CR, 0);                       // disable while configuring

    uint32_t sel = mmio_read(GPFSEL1);
    sel &= ~((7u << 12) | (7u << 15));            // clear pin 14 and 15 fields
    sel |=   (4u << 12) | (4u << 15);             // ALT0 = PL011 TXD0/RXD0
    mmio_write(GPFSEL1, sel);

    mmio_write(GPPUD, 0);                         // no pull-up/down
    delay(150);
    mmio_write(GPPUDCLK0, (1u << 14) | (1u << 15));
    delay(150);
    mmio_write(GPPUDCLK0, 0);

    mmio_write(UART_ICR, 0x7FF);                  // clear interrupts
    mmio_write(UART_IBRD, 26);                    // 115200 baud at 48 MHz
    mmio_write(UART_FBRD, 3);
    mmio_write(UART_LCRH, (1u << 4) | (3u << 5)); // FIFOs on, 8 bits
    mmio_write(UART_CR, (1u << 0) | (1u << 8) | (1u << 9)); // UART, TX, RX on
}

void uart_putc(char c)
{
    if (c == '\n')
        uart_putc('\r');
    while (mmio_read(UART_FR) & FR_TXFF) { }
    mmio_write(UART_DR, (uint32_t)c);
}

void uart_puts(const char *s)
{
    while (*s)
        uart_putc(*s++);
}
```

</details>

### Exercise 5: Print numbers, read input

Add two more functions:

- `void uart_puthex(uint64_t value)`: prints `0x` and 16 hex digits.
- `char uart_getc(void)`: waits for a byte and returns it (`'\r'` → `'\n'`).

Use them to print the exception level from `boot_main` (read `CurrentEL` with
inline assembly from section 1, shift right by 2), then echo every character
you type, forever. You should see the output at the top of this lesson.

<details>
<summary>Hint for uart_puthex</summary>

Loop over the 16 nibbles from the top: shift right by 60, 56, … 0, mask with
`0xF`, and index into `"0123456789ABCDEF"`. In C, a string literal is an array,
so `"0123456789ABCDEF"[n]` is valid.

</details>

<details>
<summary>Solution</summary>

```c
void uart_puthex(uint64_t value)
{
    uart_puts("0x");
    for (int shift = 60; shift >= 0; shift -= 4)
        uart_putc("0123456789ABCDEF"[(value >> shift) & 0xF]);
}

char uart_getc(void)
{
    while (mmio_read(UART_FR) & FR_RXFE) { }
    char c = (char)mmio_read(UART_DR);
    return c == '\r' ? '\n' : c;
}
```

```c
void boot_main(void)
{
    uart_init();
    uart_puts("Hello from RHOS!\n");

    uint64_t el;
    __asm__ volatile("mrs %0, CurrentEL" : "=r"(el));
    uart_puts("Exception level: ");
    uart_puthex(el >> 2);
    uart_puts("\n");

    for (;;)
        uart_putc(uart_getc());
}
```

</details>

### Exercise 6: What `volatile` is for

In `mmio.h`, remove `volatile` from both `mmio_read` and `mmio_write`. Rebuild, then run
`make disasm` and find `uart_putc` in `build/kernel8.lst`. Compare the
transmit-wait loop with and without `volatile`. What did the compiler do? Would
it still work on hardware with a full FIFO? Put `volatile` back afterwards.

<details>
<summary>Solution</summary>

Without `volatile`, the compiler sees a loop that reads the same memory over
and over with nothing in between that could change it, so it's allowed to read
it **once**. That's what clang does here: one `ldrb` from `UART_FR`, then if
`TXFF` is set it branches to an instruction that jumps to itself (`b .`), an
infinite loop that never reads the register again. With `volatile`, the `ldr`
is inside the loop and runs on every iteration.

(If the disassembly didn't change, make sure the build actually recompiled
`uart.c`. The Makefile tracks header dependencies, so editing `mmio.h` should
trigger it.)
On hardware, the first time the FIFO is full the core would hang forever (or
never wait, and drop bytes). In QEMU the FIFO is effectively never full, so it
appears to work, which is exactly the kind of bug that only shows up on hardware.

</details>

### Exercise 7: Is `.bss` really zero?

Add a global `uint64_t boot_count;` (no initialiser, so it goes in `.bss`) in
`main.c`. In `boot_main`, print it, then increment it. Check in
`build/kernel8.map` that it's in `.bss`.

Now comment out the zeroing loop in `start.S`. What does it print? Why is that
misleading, and why do we zero `.bss` anyway?

<details>
<summary>Solution</summary>

It still prints `0`. QEMU starts with all RAM zeroed, so `.bss` happens to be
zero already. On a real Pi, RAM after a reboot (especially a soft reset)
contains whatever was there before, so `boot_count` could be anything.
"Works in QEMU" is not "correct"; keep the loop.

</details>

### Exercise 8: Baud rate arithmetic

Without looking at the formula's result above, compute `IBRD` and `FBRD` for
**9600 baud** with the same 48 MHz clock. What's the actual baud rate you get,
and how far off is it?

<details>
<summary>Solution</summary>

`48,000,000 / (16 × 9600) = 312.5`. `IBRD = 312`, `FBRD = round(0.5 × 64) = 32`.
The divisor is exactly `312 + 32/64 = 312.5`, so the baud rate is exactly 9600.
(For 115200, the divisor is `26 + 3/64 = 26.046875`, giving about 115177 baud,
0.02% off, well within what UARTs tolerate.)

</details>

### Exercise 9 (stretch): A debug build

Add an optimisation knob to the `Makefile`: a variable `OPT ?= -O2` used in
`CFLAGS` instead of the hard-coded `-O2`, so `make clean && make OPT=-O0` builds
unoptimised code. Then step through `uart_puts` in GDB in both builds and notice
how much easier `-O0` is to follow.

### Exercise 10 (stretch): `uart_printf`

Write a tiny `uart_printf(const char *fmt, ...)` that supports `%s`, `%c`, `%x`
(hex) and `%d` (signed decimal). You'll need `<stdarg.h>`, which is available
in freestanding mode: `va_list`, `va_start`, `va_arg`, `va_end`. This will be
your main debugging tool until the C# side can print.

---

## Checkpoint

You're ready for Lesson 3 when:

- `make run` prints the greeting and the exception level `1`, and echoes your typing.
- You can explain why each of the five jobs in `_start` is needed, and what
  would go wrong without it.
- You can explain why the UART driver needs `volatile`.

Commit your work on a branch and open a PR (or just push to `main`, it's your
repo). Tell me when you're there, along with anything that surprised you or that
you'd like more depth on. Lesson 3 links a C# method compiled with NativeAOT
into this image and calls it from `boot_main`.

## Further reading

- *BCM2835 ARM Peripherals* (Broadcom), chapters 6 (GPIO) and 13 (UART). The
  addresses are bus addresses; swap `0x7E` for `0x3F`.
- *ARM PrimeCell UART (PL011) Technical Reference Manual*: the full PL011
  register descriptions.
- *Arm Architecture Reference Manual for A-profile*, D1 (exception levels) and
  the register descriptions for `HCR_EL2`, `SPSR_EL2`, `SCTLR_EL1`. Huge; search
  it rather than reading it.
