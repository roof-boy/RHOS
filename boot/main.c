#include "uart.h"

int kernel_main(void *heap_start, uint64_t heapSize);

unsigned long __security_cookie = 0x2B992DDFA232UL;

extern char __heap_start[];

void boot_main(void)
{
    uart_init();
    uint64_t el;
    __asm__ volatile("mrs %0, CurrentEL" : "=r"(el));
    uart_puts("Exception level: ");
    uart_puthex(el >> 2);
    uart_puts("\n");

    uint32_t result = kernel_main(__heap_start, 16 * 1024 * 1024);
    uart_putc('\n');
    uart_puthex(result);
    while (1)
    {
        char c = uart_getc();
        uart_putc(c);
    }
}