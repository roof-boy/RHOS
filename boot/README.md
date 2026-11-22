# boot/ — C and assembly that runs before C#

This is the first code the CPU executes after the firmware hands over control.
Its job is to get the machine into a state where C# code can run:

- park cores 1–3 so only core 0 continues (for now)
- drop from EL2 to EL1
- set up a stack and zero the `.bss` section
- bring up the UART so we can print
- call into the C# kernel entry point (from Lesson 3)

Expected contents (you write these, starting in Lesson 2):

| File | Purpose |
|------|---------|
| `linker.ld` | Where each section lives in memory. Load address is `0x80000`. |
| `start.S` | `_start`: the very first instruction. |
| `*.c` | UART driver, early helpers. |
| `include/` | Headers shared by boot code (MMIO addresses, etc.). |

The Makefile picks up every `*.S` and `*.c` file in this folder automatically.
