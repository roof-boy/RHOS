#!/usr/bin/env bash
# Checks that every tool RHOS needs is installed and can target AArch64.
# Run it with `make check`. Lesson 1 walks through installing anything missing.

set -u
pass=0
fail=0

ok()   { printf '  \033[32m[ ok ]\033[0m %s\n' "$1"; pass=$((pass + 1)); }
bad()  { printf '  \033[31m[FAIL]\033[0m %s\n         fix: %s\n' "$1" "$2"; fail=$((fail + 1)); }

need() {  # need <command> <install hint>
    if command -v "$1" >/dev/null 2>&1; then
        ok "$1 ($(command -v "$1"))"
    else
        bad "$1 not found" "$2"
    fi
}

echo "Host"
if grep -qi microsoft /proc/version 2>/dev/null; then
    ok "running under WSL"
    case "$PWD" in
        /mnt/*) bad "repo is on the Windows drive ($PWD)" \
                    "clone into your Linux home (~/) instead; /mnt/c is very slow for builds" ;;
        *)      ok "repo is on the Linux filesystem" ;;
    esac
else
    ok "running on native Linux"
fi

echo "Build tools"
APT="sudo apt install clang lld llvm make"
need clang        "$APT"
need ld.lld       "$APT"
need llvm-objcopy "$APT"
need llvm-objdump "$APT"
need make         "$APT"

if command -v clang >/dev/null 2>&1; then
    tmp=$(mktemp -d)
    printf '.global _start\n_start: wfe\n' > "$tmp/t.S"
    if clang --target=aarch64-none-elf -c "$tmp/t.S" -o "$tmp/t.o" 2>/dev/null \
       && ld.lld -Ttext=0x80000 -e _start "$tmp/t.o" -o "$tmp/t.elf" 2>/dev/null; then
        ok "clang + ld.lld can build an AArch64 bare-metal ELF"
    else
        bad "clang could not cross-build for aarch64-none-elf" "reinstall: $APT"
    fi
    rm -rf "$tmp"
fi

echo "Emulator and debugger"
need qemu-system-aarch64 "sudo apt install qemu-system-arm"
if command -v qemu-system-aarch64 >/dev/null 2>&1; then
    if qemu-system-aarch64 -machine help | grep -q '^raspi3b'; then
        ok "QEMU supports the raspi3b machine"
    else
        bad "this QEMU has no raspi3b machine" "you need QEMU 5.2 or newer"
    fi
fi
need gdb-multiarch "sudo apt install gdb-multiarch"
need xxd "sudo apt install xxd"

echo ".NET (used from Lesson 3)"
if command -v dotnet >/dev/null 2>&1; then
    ok "dotnet SDK $(dotnet --version 2>/dev/null)"
else
    bad "dotnet not found" "see 'Install the .NET SDK' in docs/lessons/01-toolchain-and-qemu.md"
fi

echo
if [ "$fail" -eq 0 ]; then
    echo "All $pass checks passed. You're ready for the Lesson 1 exercises."
else
    echo "$fail check(s) failed, $pass passed."
    exit 1
fi
