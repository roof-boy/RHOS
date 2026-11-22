#pragma once
#include <stdint.h>

#define MMIO_BASE 0x3F000000UL

static inline void mmio_write(uintptr_t address, uint32_t value)
{
    *(volatile uint32_t *)address = value;
}

static inline uint32_t mmio_read(uintptr_t address)
{
    return *(volatile uint32_t *)address;
}

static inline void delay(uint32_t count)
{
    while (count--)
        __asm__ volatile("nop");
}