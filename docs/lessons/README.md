# Lessons

Each lesson explains one step of building RHOS, then gives you exercises.
You write the code; the lesson tells you what it needs to do and why.
Solutions to quiz questions are hidden in collapsible blocks. Try first.

| # | Lesson | You end with |
|---|--------|--------------|
| 1 | [Toolchain and QEMU](01-toolchain-and-qemu.md) | A working cross toolchain, and a 3-instruction program stepped in GDB |
| 2 | [Booting to "Hello" over UART](02-boot-to-uart.md) | `make run` prints from your own C and assembly |
| 3 | [C# on bare metal with NativeAOT](03-csharp-on-bare-metal.md) | A C# method runs on bare metal |
| 4 | [A tour of the runtime](04-new-and-the-heap.md) | You know how `new`, arrays, strings and panics work underneath |
| 5 | [Exceptions, system calls and the timer](05-exceptions-and-timer.md) | The kernel catches its own crashes and counts time with interrupts |
| 6 | The kernel, continued | MMU, threads, a scheduler |
| 7 | Userspace | System calls, processes, a shell |
| 8 | Init system | A service manager that starts and supervises programs |

Lessons 2+ are written once you finish the one before, so they can build on
what you actually wrote.
