# Lessons

Each lesson explains one step of building RHOS, then gives you exercises.
You write the code; the lesson tells you what it needs to do and why.
Solutions to quiz questions are hidden in collapsible blocks. Try first.

| # | Lesson | You end with |
|---|--------|--------------|
| 1 | [Toolchain and QEMU](01-toolchain-and-qemu.md) | A working cross toolchain, and a 3-instruction program stepped in GDB |
| 2 | [Booting to "Hello" over UART](02-boot-to-uart.md) | `make run` prints from your own C and assembly |
| 3 | [C# on bare metal with NativeAOT](03-csharp-on-bare-metal.md) | A C# method runs on bare metal |
| 4 | [Making `new` work](04-new-and-the-heap.md) | `new`, strings and arrays work |
| 5 | The kernel | Interrupts, timer, MMU, a scheduler |
| 6 | Userspace | System calls, processes, a shell |
| 7 | Init system | A service manager that starts and supervises programs |

Lessons 2+ are written once you finish the one before, so they can build on
what you actually wrote.
