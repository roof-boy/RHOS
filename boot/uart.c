#include "mmio.h"

// UART initialization definitions
#define UART_BASE (MMIO_BASE + 0x201000)
#define GPFSEL1 (MMIO_BASE + 0x200004)
#define GPPUD (MMIO_BASE + 0x200094)
#define GPPUDCLK0 (MMIO_BASE + 0x200098)

// UART configuration definitions
#define UART_IBRD (UART_BASE + 0x24) // speed, whole
#define UART_FBRD (UART_BASE + 0x28) // speed, fractional
#define UART_LCRH (UART_BASE + 0x2C) // data format
#define UART_ICR (UART_BASE + 0x44) // clear pending events

// UART std register definitions
#define UART_DR (UART_BASE + 0x00)
#define UART_CR (UART_BASE + 0x30)
#define UART_FR (UART_BASE + 0x18)
#define FR_TX_FULL (1u << 5)
#define FR_RX_EMPTY (1u << 4)

static void configure_uart_pin(uint32_t pin)
{
    uint32_t sel = mmio_read(GPFSEL1);
    sel &= ~(7u << (uint32_t)pin); // clear GPIO pin bits
    sel |= (4u << (uint32_t)pin); // connect GPIO pin bits to UART
    mmio_write(GPFSEL1, sel);
}

void uart_init(void) // UART setup ritual
{
    mmio_write(UART_CR, 0); // Turn off UART

    configure_uart_pin(12);
    configure_uart_pin(15);

    // Pull resistors off
    mmio_write(GPPUD, 0);
    delay(150);
    mmio_write(GPPUDCLK0, (1u << 14) | (1u << 15));
    delay(150);
    mmio_write(GPPUDCLK0, 0);

    // Clear events
    mmio_write(UART_ICR, 0x7FF);

    // Set rates
    mmio_write(UART_IBRD, 26);
    mmio_write(UART_FBRD, 3);
    mmio_write(UART_LCRH, 0x70);

    // Turn on UART
    mmio_write(UART_CR, 0x301);
}

void uart_putc(char c)
{
    if (c == '\n')
        uart_putc('\r');
    while (mmio_read(UART_FR) & FR_TX_FULL)
    {
        // wait
    }
    mmio_write(UART_DR, (uint32_t)c);
}

void uart_puts(const char *s)
{
    while (*s != 0)
    {
        uart_putc(*s);
        s++;
    }
}

void uart_puthex(uint64_t value)
{
    const char *digits = "0123456789ABCDEF";
    uart_puts("0x");
    for (int shift = 60; shift >= 0; shift -= 4) {
        uint64_t nibble = (value >> shift) & 0xF;
        uart_putc(digits[nibble]);
    }
}

char uart_getc(void)
{
    while (mmio_read(UART_FR) & FR_RX_EMPTY)
    {
        // wait
    }
    char c = (char)mmio_read(UART_DR);
    if (c == '\r')
        c = '\n';
    return c;
}