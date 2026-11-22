# RHOS

A hobby operating system for the **Raspberry Pi 3** (AArch64), written in
**C and assembly for booting** and **C# for the kernel and userspace**.

The C# is compiled ahead of time with NativeAOT against a small custom runtime
(in the style of [zerosharp](https://github.com/MichalStrehovsky/zerosharp)),
so it runs on bare metal: no Linux underneath, no .NET runtime, no garbage
collector until we write one.

The project is built one lesson at a time; see [`docs/lessons/`](docs/lessons/).

## Goals

- Boot on a real Pi 3 and in QEMU (`raspi3b`)
- A text shell
- An application framework for writing RHOS programs in C#
- An init system and service manager, in the spirit of systemd

## Roadmap

| Stage | What gets built | Status |
|-------|-----------------|--------|
| 1. Toolchain | WSL + LLVM cross toolchain, QEMU, GDB, .NET SDK | 🚧 |
| 2. Boot | `_start` in assembly, park secondary cores, EL2 → EL1, stack, `.bss`, UART "Hello" in C | ⏳ |
| 3. C# entry | NativeAOT object linked into the kernel image, called from C | ⏳ |
| 4. Runtime | Minimal CoreLib, object layout, allocator, strings and arrays | ⏳ |
| 5. Kernel | Exception vectors, interrupts, timer, MMU and paging, threads and scheduler | ⏳ |
| 6. Userspace | EL0, system calls, processes, the app framework, the shell | ⏳ |
| 7. Init | Service manager: start, supervise and log services | ⏳ |

## Repository layout

```
boot/        C and assembly: first code to run, hands off to C#
runtime/     minimal C# runtime the kernel is compiled against
kernel/      the kernel, in C#
userspace/   app framework, shell, init system, programs
tools/       build and debug helpers
docs/        lessons and design notes
build/       build output (git-ignored)
playground/  scratch space for lesson exercises (git-ignored)
```

## Building

Requires Linux or WSL2. [Lesson 1](docs/lessons/01-toolchain-and-qemu.md)
walks through installing everything.

```bash
make check    # verify the toolchain
make          # build build/kernel8.img
make run      # boot in QEMU, UART output on this terminal (Ctrl-A X quits)
make debug    # boot paused; then `make gdb` in a second terminal
make disasm   # write a disassembly listing to build/kernel8.lst
make clean
```

## Running on real hardware

Copy `build/kernel8.img` to the boot partition of a microSD card that already
has the Raspberry Pi firmware files (`bootcode.bin`, `start.elf`, `fixup.dat`)
and a `config.txt` containing `arm_64bit=1`. Output appears on the UART
(GPIO 14/15), which you read with a 3.3 V USB-to-serial adapter.
