#include "uart.h"

void boot_main(void)
{
    uart_init();
    uint64_t el;
    __asm__ volatile("mrs %0, CurrentEL" : "=r"(el));
    uart_puts("Exception level: ");
    uart_puthex(el >> 2);
    uart_puts("\n");

    uart_puts("Hello from RHOS!\n");
    uart_puthex(255);
    while (1)
    {
        char c = uart_getc();
        uart_putc(c);
    }
}