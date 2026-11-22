# Lesson 1: Toolchain and QEMU

**Goal:** by the end you have a cross toolchain in WSL, you understand how
source code becomes a bootable `kernel8.img`, and you have stepped through
your first three AArch64 instructions in an emulated Raspberry Pi 3 with GDB.

**You will write:** one tiny assembly file in `playground/` (not committed).
The real boot code starts in Lesson 2.

**Time:** 1–2 hours.

---

## 1. How a Raspberry Pi 3 boots

When you write a .NET app, a lot happens before `Main` runs: the OS loads the
executable, the runtime starts, the JIT or AOT code is mapped in. On bare
metal, *you* are the thing that runs first. So it's worth knowing exactly what
happens before your first instruction.

The Pi 3 boots in an unusual order: **the GPU starts first, not the CPU.**

1. The GPU (VideoCore IV) runs code from its own ROM, which reads
   `bootcode.bin` from the SD card.
2. `bootcode.bin` starts the RAM and loads `start.elf`, the GPU firmware.
3. `start.elf` reads `config.txt`, then loads your kernel image. With
   `arm_64bit=1` it looks for **`kernel8.img`** and copies it, byte for byte,
   to physical address **`0x80000`**.
4. The firmware releases the four ARM Cortex-A53 cores. **Core 0** jumps to
   `0x80000`. **Cores 1–3** are parked in a small loop, waiting for someone to
   write an address into a "spin table" in low memory (`0xE0`, `0xE8`, `0xF0`),
   at which point they jump there.
5. Core 0 starts in **EL2** (Exception Level 2, the hypervisor level), with the
   MMU off and caches off. Register `x0` holds the address of the device tree.

Some consequences you'll meet in Lesson 2:

- `kernel8.img` is a **raw binary**, not an ELF or PE file. There is no
  header and no loader. Byte 0 of the file is your first instruction.
- Your code must be **linked to run at `0x80000`**, because that's where it lands.
- There is no stack, no heap, no `.bss` zeroing. You set all of that up.

### Exception levels in one paragraph

AArch64 has four privilege levels. **EL0** is user programs (your shell will
run here). **EL1** is the OS kernel (RHOS's kernel). **EL2** is the hypervisor.
**EL3** is secure firmware. Higher numbers can do more. One of the first things
RHOS's boot code will do is drop from EL2 to EL1. If you know .NET, think of it
loosely as the difference between user code and the code the CLR is allowed to
run, enforced by the CPU itself.

## 2. What QEMU does for us

You don't have a USB-to-serial cable yet, so we develop against **QEMU's
`raspi3b` machine**. It emulates the Pi 3's CPU, RAM and a good chunk of its
peripherals, including the UARTs, the timer and the interrupt controller.

QEMU skips the GPU stage. When you pass `-kernel kernel8.img`, it:

- loads the file at `0x80000`,
- puts a tiny boot stub at address `0` that sets `x0` and jumps to `0x80000`,
- parks cores 1–3 in a spin loop, the same as the real firmware.

It's close enough to real hardware that code which works in QEMU usually works
on the Pi. "Usually" is doing real work in that sentence: QEMU is more
forgiving about cache maintenance and timing, so we'll test on hardware once
you have a cable (see the end of this lesson).

QEMU also has a **GDB server** built in. That's how we'll debug without any
output at all: pause the emulated CPU, inspect registers and memory, single-step
instructions. This is your most important tool until the UART works.

## 3. Set up WSL

You need WSL 2 with Ubuntu (24.04 is what these lessons are tested on). In
PowerShell:

```powershell
wsl --install -d Ubuntu-24.04
wsl -l -v        # VERSION should say 2
```

Then open the Ubuntu terminal for everything else.

> **Keep the repo inside Linux.** Clone into `~/` (for example `~/src/RHOS`),
> not into `/mnt/c/...`. Files under `/mnt/c` go through a translation layer
> that makes builds many times slower and can mangle line endings and file
> permissions. From Windows you can still reach the files at
> `\\wsl$\Ubuntu-24.04\home\<you>\src\RHOS`, and VS Code's *WSL* extension
> (`code .` from the Ubuntu terminal) works transparently.

## 4. Install the toolchain

```bash
sudo apt update
sudo apt install -y git make clang lld llvm qemu-system-arm gdb-multiarch xxd dotnet-sdk-10.0
```

What each piece is for:

| Package | Gives you | Why |
|---------|-----------|-----|
| `clang` | C compiler *and* assembler | Compiles `.c` and `.S` to AArch64 object files |
| `lld` | `ld.lld`, the linker | Combines object files and places them at the right addresses |
| `llvm` | `llvm-objcopy`, `llvm-objdump`, `llvm-readelf` | Convert ELF to raw binary, inspect output |
| `qemu-system-arm` | `qemu-system-aarch64` | The emulated Raspberry Pi 3 |
| `gdb-multiarch` | A GDB that understands AArch64 | Debugging through QEMU |
| `xxd` | `xxd` hex dumper | Look at raw bytes of an image |
| `dotnet-sdk-10.0` | .NET SDK | NativeAOT compiler, from Lesson 3 |

### Why LLVM and not GCC?

Most Pi bare-metal tutorials use GCC with a separate `aarch64-none-elf-gcc`
cross compiler. With LLVM, **one install targets every architecture**; you
choose with `--target=aarch64-none-elf`. NativeAOT, which compiles our C#,
is also built on LLVM-style object files and is happiest with `lld`. Using
LLVM for everything means one set of tools and fewer surprises when we link C
and C# together.

### Check it

Clone the repo and run the checker:

```bash
mkdir -p ~/src && cd ~/src
git clone https://github.com/roof-boy/RHOS.git
cd RHOS
make check
```

Every line should say `[ ok ]`. If something fails, the `fix:` line tells you
what to install.

## 5. From source to `kernel8.img`

In .NET, `dotnet build` hides several steps. On bare metal you see each one,
and you need to, because each one is a place things go wrong.

```
 start.S  ──clang──►  start.o  ─┐
 uart.c   ──clang──►  uart.o   ─┼─ld.lld + linker.ld──►  kernel8.elf ──llvm-objcopy──►  kernel8.img
 (C#)     ─NativeAOT► kernel.o ─┘
```

1. **Compile / assemble.** Each source file becomes an **object file** (`.o`).
   It contains machine code, but addresses aren't final yet: a call to
   `uart_puts` is a hole labelled "fill in `uart_puts` here" (a *relocation*).
   The closest .NET analogy is a single compiled class that hasn't been
   linked to the assemblies it references yet.
2. **Link.** The linker takes all the object files, decides where every
   function and variable goes in memory, and fills in the holes. The
   **linker script** (`boot/linker.ld`, Lesson 2) tells it the rules: start at
   `0x80000`, put code first, then read-only data, then data, then `.bss`.
   The output is an **ELF** file, which still has headers, a symbol table and
   debug info.
3. **Strip to raw binary.** The Pi firmware doesn't understand ELF. `objcopy
   -O binary` throws away everything except the bytes that should be in memory,
   laid out exactly as they'll be loaded. That's `kernel8.img`.

We keep the ELF around because **GDB uses its symbols and debug info**. QEMU
runs the `.img`, GDB reads the `.elf`, and they agree because they describe
the same bytes.

The top-level `Makefile` does all of this for you. Open it and read it once;
it's commented. You don't need to understand Make syntax deeply, just the
pipeline above.

## 6. Just enough AArch64 assembly

You'll learn assembly properly in Lesson 2. For today you need five things:

- **Registers.** 31 general-purpose 64-bit registers, `x0` to `x30`. `w0` to
  `w30` are the lower 32 bits of the same registers. `sp` is the stack
  pointer, `pc` is the program counter (address of the current instruction).
  Think of registers as the CPU's only local variables.
- **`mov x0, #42`** puts the constant 42 into `x0`.
- **`add x1, x1, #1`** means `x1 = x1 + 1`. AArch64 instructions are usually
  `op destination, source1, source2`.
- **`b label`** is an unconditional jump (branch), a `goto`.
- **`wfe`** means "wait for event": the core sleeps until something wakes it.
  It's how you idle a core without burning power in a busy loop.

Labels end with a colon. `1:` is a *local* label; `b 1b` means "branch to the
nearest label `1` **b**ackwards" (`1f` would be forwards).

Every AArch64 instruction is exactly **4 bytes**. That will matter in a minute.

---

## Exercises

Do these in `playground/` at the root of the repo. It's git-ignored, so
nothing you try there gets committed.

### Exercise 1: Check your environment

Run `make check` and get every line to `[ ok ]`. Also run plain `make` and
read the message it prints. You haven't written any boot code yet, so it
should tell you so, not crash.

### Exercise 2: Your first bare-metal program, built by hand

Create `playground/spin.S`:

```asm
.section .text
.global _start
_start:
    mov x0, #42
1:  wfe
    b 1b
```

Now build it **without the Makefile**, one step at a time, so you see each stage:

```bash
cd playground
clang --target=aarch64-none-elf -c spin.S -o spin.o           # assemble
ld.lld -Ttext=0x80000 -e _start spin.o -o spin.elf            # link at 0x80000
llvm-objcopy -O binary spin.elf spin.img                      # strip to raw binary
```

(`-Ttext=0x80000` is a shortcut for a one-line linker script. You'll write a
real one in Lesson 2.)

Then answer these questions using the tools. Write your answers down before
you open the solutions.

a. Run `llvm-objdump -d spin.elf`. At what address does each instruction live?

b. Run `ls -l spin.o spin.elf spin.img`. Why is `spin.img` so much smaller
   than the other two? Exactly how many bytes is it, and why that number?

c. Run `xxd spin.img`. Find the 4 bytes of `mov x0, #42` in the dump and
   compare them to the encoding `objdump` printed. What do you notice about
   the byte order?

d. Run `llvm-readelf -h spin.elf`. What does it say for `Machine` and
   `Entry point address`?

e. Run `llvm-objdump -d spin.o` (the object file, before linking). What
   address does `_start` have there? Why is it different from the ELF?

<details>
<summary>Solutions</summary>

a. `0x80000`, `0x80004`, `0x80008`. Each instruction is 4 bytes, starting at
   the link address.

b. The `.img` contains *only* the bytes that get loaded into memory: three
   4-byte instructions, so **12 bytes**. The `.o` and `.elf` also carry ELF
   headers, section tables, a symbol table and string tables.

c. `objdump` shows `d2800540` for `mov x0, #42`; `xxd` shows `40 05 80 d2`.
   The bytes are reversed because AArch64 is **little-endian**: the least
   significant byte is stored first. You'll see this every time you read a
   memory dump.

d. `Machine: AArch64`, `Entry point address: 0x80000`.

e. `0`. Object files aren't placed in memory yet; every section starts at 0
   and the linker decides the real address. This is why the linker exists.

</details>

### Exercise 3: Boot it in QEMU and step through it with GDB

You'll need **two WSL terminals**, both in `playground/`.

**Terminal 1**: start QEMU, paused before the first instruction (`-S`), with
the GDB server on port 1234 (`-s`):

```bash
qemu-system-aarch64 -M raspi3b -kernel spin.img -display none -serial null -s -S
```

It will look like it hangs. That's correct: the CPU is frozen waiting for GDB.

**Terminal 2**: attach GDB:

```bash
gdb-multiarch spin.elf
(gdb) target remote :1234
```

Now explore. Useful commands:

| Command | What it does |
|---------|--------------|
| `info registers` (`i r`) | All registers. `i r x0 pc` for specific ones |
| `x/4i $pc` | Disassemble 4 instructions at the program counter |
| `x/4gx 0x80000` | Dump 4 64-bit words of memory in hex |
| `stepi` (`si`) | Execute exactly one instruction |
| `break *0x80000` | Breakpoint at an address |
| `continue` (`c`) | Run until a breakpoint |
| `info threads` | One "thread" per CPU core |
| `thread 2` | Switch to looking at core 1 |
| `p $x0` | Print a register |

Answer these:

a. Before you continue, where is `pc`? Run `x/6i $pc`. That's not your code.
   What is it, and where does it end up jumping?

b. Set a breakpoint at `0x80000` and `continue`. You're now at your first
   instruction. Print `x0`, then `stepi` once and print it again. What changed?

c. `stepi` a few more times. What does the program do forever?

d. Run `info threads`. You should see four. Where are the other three cores,
   and what are they doing?

e. Quit with `Ctrl-C`, `kill`, `quit` in GDB, then `Ctrl-C` QEMU.

<details>
<summary>Solutions</summary>

a. `pc` is `0x0`. That's QEMU's boot stub (playing the role of the Pi
   firmware): it loads `x0` with the device tree address, zeroes `x1`–`x3`,
   loads `0x80000` into `x4` and does `br x4`, which jumps to your code.

b. Before `stepi`, `x0` holds whatever the stub left there (the device-tree
   address, `0x100`). After executing `mov x0, #42`, it's `42` (`0x2a`).

c. It alternates between `wfe` at `0x80004` and `b` at `0x80008`. In GDB,
   stepping over `wfe` may look like nothing happens; the core is "sleeping".
   This is the standard way to halt a core forever.

d. Cores 1–3 are around address `0x300`, inside QEMU's stub, in a loop that
   checks the spin table and sleeps with `wfe`. They'll stay there until some
   code writes an address for them and wakes them up. RHOS will leave them
   parked until we add multi-core support much later.

</details>

### Exercise 4: Which exception level are we in?

Still in GDB, at your breakpoint on `0x80000`, run:

```
p/x $cpsr
```

`CPSR` (in AArch64 it's really called `PSTATE`, and GDB shows it as `cpsr`)
holds the processor state. Its lowest 4 bits encode the exception level and
which stack pointer is in use:

| Bits `[3:0]` | Meaning |
|--------------|---------|
| `0b0000` | EL0t |
| `0b0100` / `0b0101` | EL1t / EL1h |
| `0b1000` / `0b1001` | EL2t / EL2h |

Which exception level did QEMU start you in? Does it match section 1?

<details>
<summary>Solution</summary>

You'll see something like `0x400003c9`. The low hex digit is `9` = `0b1001` =
**EL2h**: EL2, using the EL2 stack pointer. That matches the real Pi firmware.
(The `3c` part means interrupts are masked, which is also how we want it at
boot.) In Lesson 2 you'll write the code that drops to EL1.

</details>

### Exercise 5: Make it do something you can watch

Change `spin.S` so that core 0 counts forever: start `x1` at 0 and add 1 to it
in a loop. Rebuild it by hand (Exercise 2's three commands), boot it paused,
and in GDB use `display $x1` followed by repeated `stepi` (just pressing
Enter repeats the last command) to watch it count.

Then: let it run with `continue` for a second, press `Ctrl-C`, and check `x1`.
Roughly how many loop iterations did QEMU run per second?

<details>
<summary>One possible solution</summary>

```asm
.section .text
.global _start
_start:
    mov x1, #0
1:  add x1, x1, #1
    b 1b
```

The number you see after a second depends on your machine; it'll be in the
tens or hundreds of millions. QEMU translates AArch64 code to x86 code on the
fly, so it's fast, but not cycle-accurate. Never rely on loop counts for
timing; Lesson 5 uses the hardware timer instead.

</details>

### Exercise 6 (stretch): Break it on purpose

Link the program at the wrong address: change `-Ttext=0x80000` to
`-Ttext=0x0`, rebuild, and run it in QEMU with GDB. Then try the same with a
version that uses a **label address**, like this:

```asm
_start:
    adr x0, msg        // PC-relative: "address of msg, relative to where I am"
    ldr x1, =msg       // absolute: "the address the linker said msg is at"
1:  wfe
    b 1b
msg: .ascii "hi"
```

Compare `x0` and `x1` after the two instructions, with both link addresses.
(Use `break *0x80000` in both cases. With the wrong link address GDB's
symbols won't line up with the real code, which is part of the lesson.)
Which one is wrong when the link address doesn't match the load address, and
why does that matter for every kernel ever written?

<details>
<summary>Solution</summary>

With `-Ttext=0x0`, QEMU still loads the bytes at `0x80000`. `adr` computes the
address relative to the current `pc`, so `x0` is correct (it points at the
real `msg` near `0x80000`). `ldr x1, =msg` loads the address the *linker*
computed, so `x1` points somewhere near `0x0`, where `msg` isn't.

Code that only uses PC-relative addressing is **position-independent**; it
works wherever it's loaded. Most real code isn't, so the link address and the
load address must match. That's exactly why the linker script in Lesson 2
starts with `. = 0x80000;`. It also comes back in Lesson 5, when the MMU makes
the kernel run at a different virtual address from its physical one.

</details>

---

## Checkpoint

You're ready for Lesson 2 when you can answer these without looking:

1. What file does the Pi 3 firmware load for a 64-bit kernel, and at what address?
2. Why do we produce both a `.elf` and a `.img`?
3. What does the linker do that the compiler can't?
4. Which core runs your code first, and what are the others doing?
5. Which exception level does your code start in, and which one will the RHOS kernel run in?

When you're done, tell me, and share anything that surprised you or didn't
work. Lesson 2 (boot to "Hello" over UART) builds directly on this.

---

## Optional: things to buy for real hardware

Nothing here is needed until you want to run on the actual Pi.

- **A 3.3 V USB-to-TTL serial adapter** (CP2102, FT232 or PL2303 based, a few
  dollars). This is how you'll see output from the real board. Make sure it's
  **3.3 V logic**; a 5 V adapter can damage the Pi's GPIO. You'll connect
  three wires: adapter GND → Pi pin 6, adapter RX → Pi pin 8 (TXD), adapter TX
  → Pi pin 10 (RXD). Don't connect the adapter's power pin.
- **A microSD card** you can wipe (any size; the kernel is tiny).
- Optionally, an **SD card extender or a network boot setup** later on, because
  swapping the SD card for every build gets old fast. We'll cover a serial
  bootloader in a later lesson so you can upload kernels over the cable.
