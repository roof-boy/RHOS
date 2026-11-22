ENTRY(_start)

SECTIONS
{
    . = 0x8000;

    .text : {
        KEEP(*(.text.boot))
        *(.text .text.*)
    }
    .rodata : { *(.rodata .rodata.*) }
    .data   : { *(.data .data.*) }
    . = ALIGN(16);
    __bss_start = .;
    .bss : { *(.bss, .bss.*) *(COMMON) }
    . = ALIGN(16);
    __bss_end = .;
}