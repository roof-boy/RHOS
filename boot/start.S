.section .text.boot
.global _start
_start:
    // Make sure only core 0 runs code.
    mrs     x1, mpidr_el1
    and     x1, x1, #0xFF
    cbnz    x1, sleep_forever

    // Drop from EL2 to EL1
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
    // Setup execution stack
    mov     x1, #0x80000
    mov     sp, x1

    // Zero .bss section
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