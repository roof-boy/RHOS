# kernel/ — the RHOS kernel, in C#

Compiled ahead of time with NativeAOT against `runtime/` and linked with the
boot code into `kernel8.img`.

Built up in Lesson 5 onward:

- exception vectors and interrupt handling (C# handlers, asm trampolines)
- the system timer
- physical memory manager and MMU / page tables
- processes, threads and a scheduler
- the system call interface
