ENTRY(_start)

SECTIONS
{
    . = 0x80000;

    .text : {
        KEEP(*(.text.boot))
        *(.text .text.*)
        *(__managedcode)
    }
    .rodata : {
        *(.rodata .rodata.*)
        *(.dotnet_eh_table)
    }
    .data   : {
        *(.data .data.*)
        *(__modules) 
    }
    . = ALIGN(16);
    __bss_start = .;
    .bss : { *(.bss, .bss.*) *(COMMON) }
    . = ALIGN(16);
    __bss_end = .;
}