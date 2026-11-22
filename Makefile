# RHOS top-level Makefile
#
# Pipeline:   boot/*.S, boot/*.c  --(clang)-->  build/boot/*.o
#             runtime/, kernel/   --(dotnet build)-->  IL .dll files
#             IL .dll files       --(ILC, NativeAOT)-->  build/kernel/kernel.o
#             build/**/*.o        --(ld.lld + boot/linker.ld)-->  build/kernel8.elf
#             build/kernel8.elf   --(llvm-objcopy)-->  build/kernel8.img
#
# Common targets:
#   make check     verify your toolchain (Lesson 1)
#   make           build build/kernel8.img
#   make run       boot it in QEMU, UART on this terminal (Ctrl-A X to quit)
#   make debug     boot it in QEMU paused, waiting for GDB on :1234
#   make gdb       attach GDB to a `make debug` session (run in a 2nd terminal)
#   make disasm    disassemble the kernel into build/kernel8.lst
#   make clean     delete build/

BUILD    := build

# ---- Toolchain ---------------------------------------------------------------
# LLVM is a cross compiler out of the box: one install targets every arch,
# you pick the target with --target. NativeAOT (Lesson 3) also emits objects
# that ld.lld links happily, so we use LLVM everywhere.
TARGET   := aarch64-none-elf
CC       := clang
LD       := ld.lld
OBJCOPY  := llvm-objcopy
OBJDUMP  := llvm-objdump
QEMU     := qemu-system-aarch64
GDB      := gdb-multiarch

# ---- Flags -------------------------------------------------------------------
# -ffreestanding     : no hosted C library; only <stdint.h>-style headers exist
# -nostdlib          : don't link libc / crt0, we provide _start ourselves
# -mgeneral-regs-only: no FP/SIMD registers until the kernel enables them
# -mcpu=cortex-a53   : the Pi 3's CPU
ARCHFLAGS := --target=$(TARGET) -mcpu=cortex-a53
# -MMD -MP          : also write build/**/*.d files listing the headers each
#                     source includes, so editing a header rebuilds its users
DEPFLAGS  := -MMD -MP
CFLAGS    := $(ARCHFLAGS) -ffreestanding -nostdlib -mgeneral-regs-only \
             -O2 -g -Wall -Wextra -std=c11 -Iboot/include $(DEPFLAGS)
ASFLAGS   := $(ARCHFLAGS) -g -Iboot/include $(DEPFLAGS)
LDFLAGS   := -nostdlib -T boot/linker.ld -Map=$(BUILD)/kernel8.map

# ---- QEMU --------------------------------------------------------------------
# raspi3b emulates the board and its firmware: it loads kernel8.img at
# 0x80000 and starts core 0 there (cores 1-3 are parked).
# The first -serial is the PL011 UART0, the second is the mini UART (UART1).
QEMU_FLAGS := -M raspi3b -kernel $(BUILD)/kernel8.img -display none \
              -serial mon:stdio

# ---- C# (NativeAOT) ----------------------------------------------------------
# `dotnet build` compiles C# to IL (.dll files). ILC, the NativeAOT compiler,
# then turns the IL into an AArch64 object file that links like any C object.
DOTNET     := dotnet
DOTNET_OUT := $(BUILD)/dotnet/bin
# --systemmodule: our runtime/ project *is* the base library (System.Private.CoreLib)
# --nativelib   : produce a library (no Main/exe startup), we call in from C
# -O --noscan   : optimise each method, without whole-program analysis that
#                 expects a full .NET runtime to exist
# --reflectiondata:none : no reflection metadata (we have no reflection)
ILCFLAGS   := --targetos:linux --targetarch:arm64 -O --noscan --nativelib \
              --systemmodule:System.Private.CoreLib --reflectiondata:none

# ---- Sources -----------------------------------------------------------------
BOOT_S   := $(wildcard boot/*.S)
BOOT_C   := $(wildcard boot/*.c)
BOOT_OBJ := $(patsubst boot/%,$(BUILD)/boot/%.o,$(BOOT_S) $(BOOT_C))
CS_SRC   := $(shell find runtime kernel -name '*.cs' 2>/dev/null)
OBJS     := $(BOOT_OBJ)
# The C# kernel joins the build as soon as kernel/ contains a .cs file.
ifneq ($(wildcard kernel/*.cs),)
OBJS     += $(BUILD)/kernel/kernel.o
endif

# ---- Rules -------------------------------------------------------------------
.PHONY: all check run debug gdb disasm clean

all: $(BUILD)/kernel8.img

ifeq ($(and $(wildcard boot/linker.ld),$(strip $(BOOT_S) $(BOOT_C))),)
# boot/ is still empty: explain instead of failing with a cryptic error.
$(BUILD)/kernel8.elf:
	@echo "Nothing to build yet: boot/ needs linker.ld and at least one .S or .c file."
	@echo "You write those in Lesson 2. For Lesson 1, see docs/lessons/01-toolchain-and-qemu.md."
	@exit 1
else
$(BUILD)/boot/%.S.o: boot/%.S
	@mkdir -p $(dir $@)
	$(CC) $(ASFLAGS) -c $< -o $@

$(BUILD)/boot/%.c.o: boot/%.c
	@mkdir -p $(dir $@)
	$(CC) $(CFLAGS) -c $< -o $@

$(BUILD)/kernel8.elf: $(OBJS) boot/linker.ld
	$(LD) $(LDFLAGS) $(OBJS) -o $@
endif

-include $(OBJS:.o=.d)

# C# -> IL
$(DOTNET_OUT)/Kernel/Release/Kernel.dll: $(CS_SRC) kernel/Kernel.csproj runtime/System.Private.CoreLib.csproj
	$(DOTNET) build kernel/Kernel.csproj -c Release -nologo -v quiet -clp:NoSummary

# IL -> AArch64 object file
# tools/find-ilc.sh prints ILC's path, downloading it the first time.
$(BUILD)/kernel/kernel.o: $(DOTNET_OUT)/Kernel/Release/Kernel.dll
	@mkdir -p $(dir $@)
	$$(./tools/find-ilc.sh) $< $(DOTNET_OUT)/Kernel/Release/System.Private.CoreLib.dll \
		$(ILCFLAGS) --map:$(BUILD)/kernel/kernel.map.xml -o $@

$(BUILD)/kernel8.img: $(BUILD)/kernel8.elf
	$(OBJCOPY) -O binary $< $@
	@echo "Built $@ ($$(stat -c %s $@) bytes)"

check:
	@./tools/check-toolchain.sh

run: $(BUILD)/kernel8.img
	$(QEMU) $(QEMU_FLAGS)

debug: $(BUILD)/kernel8.img
	@echo "QEMU is paused and waiting for GDB on localhost:1234."
	@echo "In another terminal run: make gdb"
	$(QEMU) $(QEMU_FLAGS) -s -S

gdb:
	$(GDB) -x tools/rhos.gdb $(BUILD)/kernel8.elf

disasm: $(BUILD)/kernel8.elf
	$(OBJDUMP) -d -S $< > $(BUILD)/kernel8.lst
	@echo "Wrote $(BUILD)/kernel8.lst"

clean:
	rm -rf $(BUILD)
