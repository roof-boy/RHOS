# GDB setup for RHOS. Loaded by `make gdb`.
# QEMU (started with `make debug`) listens for GDB on localhost:1234.

set architecture aarch64
set pagination off
set confirm off
target remote localhost:1234

# Show the next instructions every time execution stops.
display/4i $pc

# Uncomment once your boot code has a _start symbol:
# break _start
