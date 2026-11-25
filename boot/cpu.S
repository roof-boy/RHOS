// Tiny functions for CPU instructions that C# can't express.
// Arguments arrive in x0, x1...; the return value goes in x0.

.section .text

// void cpu_install_vectors(void): tell the CPU where our table is.
.global cpu_install_vectors
cpu_install_vectors:
    adr     x0, exception_vectors
    msr     vbar_el1, x0
    isb
    ret

// void cpu_breakpoint(void): cause an exception on purpose.
.global cpu_breakpoint
cpu_breakpoint:
    brk     #0
    ret

// ulong cpu_fault_address(void): the address that caused a data abort.
.global cpu_fault_address
cpu_fault_address:
    mrs     x0, far_el1
    ret

// ulong cpu_syscall(ulong number, ulong arg): ask the kernel for something
.global cpu_syscall
cpu_syscall:
    mov     x8, x0
    mov     x0, x1
    svc     #0
    ret

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
    wfi
    ret

// ---- ARM Generic Timer ----

// ulong timer_frequency(void): how many counts per second
.global timer_frequency
timer_frequency:
    mrs     x0, cntfrq_el0
    ret

// void timer_start(ulong ticks): raise an interrupt `ticks` counts from now
.global timer_start
timer_start:
    msr     cntp_tval_el0, x0
    mov     x0, #1
    msr     cntp_ctl_el0, x0
    ret