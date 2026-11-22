# Lesson 3: Running C# on bare metal

**Goal:** your C boot code calls a C# method, and that C# method prints
through your UART driver:

```
Hello from RHOS!
Hello from C#!
C# returned 0x000000000000002A
```

**You will write:**

| File | What it is |
|------|------------|
| `kernel/Kernel.cs` | Your first C# kernel code |
| `runtime/CoreLib.cs` | Your own tiny version of .NET's base library: `Object`, `String`, `Int32`… |
| small changes in `boot/` | Call into C#, and let C# use the CPU's floating-point registers |

**Already done for you** (build system, as before): the two `.csproj` files,
`Directory.Build.props`/`.targets`, `tools/find-ilc.sh`, and the new Makefile
rules. Step 1 explains them.

**How this lesson works:** like Lesson 2, in small steps. The middle of the
lesson is a game: you write some C# kernel code, and the compiler keeps
telling you what's missing. Each error teaches you one thing about how .NET
works underneath.

**Time:** two sittings: steps 0–3, then 4–7.

---

## Step 0: Fix the link address

Before anything else: in `boot/linker.ld`, change `. = 0x8000;` to
`. = 0x80000;`. Lesson 3 adds C# code and data to the image, and with the wrong
address it would break in confusing ways (see my review of your Lesson 2 code).

Run `make run` and check everything still works.

---

## Before you start: how C# normally runs

You've used C# for years, so let's look at what actually happens when you run
a C# program, and which parts we have to replace.

### Normally (`dotnet run`)

1. The C# compiler (`csc`, run by `dotnet build`) turns your `.cs` files into
   **IL** (Intermediate Language): a CPU-independent bytecode, stored in a
   `.dll`.
2. When you run it, the **.NET runtime** starts. It loads your `.dll`, and
   the **JIT** (just-in-time compiler) turns each method's IL into real machine
   code the first time it's called.
3. Your code leans on the runtime constantly: the **GC** (garbage collector)
   frees objects, the type system makes `is`/`as`/virtual calls work, and
   exceptions get thrown and caught.
4. And all of it uses the **base library**, `System.Private.CoreLib.dll`: that's
   where `Object`, `String`, `Int32`, `List<T>`, `Console` and friends live.
   CoreLib in turn calls the operating system (Windows or Linux) for files,
   memory, threads and so on.

### What RHOS can't use

There is no OS underneath us. RHOS *is* the OS. So:

- **No JIT.** There's nothing to run it. We need machine code ahead of time.
- **No runtime, no GC.** It's a big program that expects an OS.
- **No normal CoreLib.** It's built on the runtime and on OS calls.

### What we do instead: NativeAOT, with our own CoreLib

**NativeAOT** is the official .NET feature that compiles IL to machine code
**ahead of time**, at build time. You may have used it with
`<PublishAot>true</PublishAot>`. Its compiler is called **ILC**.

Normally NativeAOT still includes the runtime and Microsoft's CoreLib. But ILC
has an option to use a **different** base library instead: `--systemmodule`.
So the RHOS pipeline is:

```
runtime/CoreLib.cs ─┐
                    ├─ dotnet build ─► .dll files (IL) ─► ILC ─► kernel.o ─┐
kernel/Kernel.cs  ──┘                                                      ├─ ld.lld ─► kernel8.img
boot/*.S, boot/*.c ─────────────────── clang ────────────► *.o ───────────┘
```

From the linker's point of view, `kernel.o` is just another object file, like
the ones clang makes from your C. That's the whole trick.

What this means for your C# code, at least until later lessons:

| Works | Doesn't work yet |
|-------|------------------|
| Static methods, local variables, loops, `if`, math | `new` (no memory allocator yet; Lesson 4) |
| Structs, pointers, `unsafe`, `fixed`, `stackalloc` | Garbage collection (maybe never; we'll see) |
| String **literals** (`"Hello"`) | Exceptions (`throw`/`try`/`catch`) |
| Calling C functions, being called from C | Anything from the normal .NET library: `Console`, `List<T>`, LINQ |

String literals work even without `new` because ILC builds them **into the
image** at compile time, as ready-made objects. Nothing needs to be allocated.

---

## Step 1: A tour of the new build files

Run `git pull` and look at what's new. You don't need to change any of these,
but you should know what they do.

**`Directory.Build.props`** holds settings shared by every C# project in the
repo. The important ones:

- `NoStdLib`: "don't reference Microsoft's base library". Without this, the
  compiler would hand you Microsoft's `Object`, `String`… and the final code
  would need the real runtime.
- `AllowUnsafeBlocks`: we'll use pointers a lot.
- The output paths send all .NET build output to `build/dotnet/`.

**`Directory.Build.targets`** removes the framework references the .NET SDK
adds anyway, even with `NoStdLib`.

**`runtime/System.Private.CoreLib.csproj`** is **your** base library. It must
be called `System.Private.CoreLib`, because that's the name both the C#
compiler and ILC look for when they need "the assembly that defines `Object`".

**`kernel/Kernel.csproj`** is the kernel. It references the runtime project,
exactly like a normal project references a class library.

**`tools/find-ilc.sh`** finds ILC on your machine. ILC ships as a NuGet package,
so the first time you build, it downloads it (through `tools/ilc/ilc.csproj`,
an empty project that exists only to trigger that download).

**The Makefile** has two new rules:

1. `dotnet build kernel/Kernel.csproj` → IL `.dll` files.
2. ILC → `build/kernel/kernel.o`.

And `kernel.o` joins the link **as soon as `kernel/` contains a `.cs` file**.
Until then, your build stays exactly as it was. The ILC options are explained
in comments in the Makefile.

## Step 2: Write the kernel's first C# code

Create `kernel/Kernel.cs`:

```csharp
using System.Runtime.InteropServices;

public static unsafe class Kernel
{
    // A C function from boot/uart.c. "*" means "it's linked into the same
    // image, look it up by name".
    [DllImport("*")]
    static extern void uart_putc(byte c);

    static void Print(string s)
    {
        fixed (char* p = s)
        {
            for (int i = 0; i < s.Length; i++)
                uart_putc((byte)p[i]);
        }
    }

    // Exported under the C name "kernel_main", so C can call it.
    [UnmanagedCallersOnly(EntryPoint = "kernel_main")]
    public static int Main()
    {
        Print("Hello from C#!\n");
        return 42;
    }
}
```

Two attributes do all the work of connecting C and C#:

- **`[DllImport("*")]`** declares a function that lives *outside* C#. Normally
  `DllImport` names a DLL (`[DllImport("user32.dll")]`). Here, `"*"` means "no
  DLL, the linker will find `uart_putc` in the same image". It's the C# version
  of the declarations in your `uart.h`.
- **`[UnmanagedCallersOnly(EntryPoint = "kernel_main")]`** goes the other way:
  it makes a C# method callable **from** C, under the name `kernel_main`.

`Print` uses `fixed` to get a `char*` pointing at the string's characters. C#
strings are UTF-16 (2 bytes per character); the UART sends bytes. For plain
English text, the low byte of each `char` is the ASCII code, so `(byte)p[i]` is
fine for now.

Now build:

```bash
make
```

You'll get a wall of errors. That's expected. Read on.

## Step 3: Build your own CoreLib, one error at a time

Every error says, in effect, "I need a type you haven't defined". Your job: add
it to `runtime/CoreLib.cs`, run `make` again, repeat. Below is the order you'll
likely meet them in, with what each type is for. Try to write each one from the
explanation before you open the hint.

The errors come in two kinds:

- **`CS0518: Predefined type 'System.X' is not defined or imported`**: the C#
  compiler itself needs `X` to compile basic C# (for example, `int` is just a
  nickname for `System.Int32`, and it must exist somewhere).
- **`CS0246: The type or namespace name 'X' could not be found`**: your code
  uses `X` (like `DllImport`) and nobody has defined it.

> **Make everything `public`.** In C#, a type with no access modifier is
> `internal`, and a member with none is `private`. The compiler only accepts a
> *public* `System.Object` as the real one; an internal one gives the same
> "not defined" error as no `Object` at all. So every type in your CoreLib, and
> every constructor, property and field the compiler uses, must be `public`.
> (The only exceptions in this lesson are marked `internal` explicitly.)

> **Tip:** errors are printed twice (once per project); only read the first few
> each time. And when you fix a batch, the *next* batch can be completely
> different, because the compiler only gets further once the first problems
> are gone.

### Round 1: the absolute basics

You'll see `CS0518` for `Object`, `Void`, `String`, `Int32` and `Byte`.

Start `runtime/CoreLib.cs` with a `namespace System { ... }` block and add:

**`Object`**: the base class of every class. Here's something you never had to
think about in C#: **every object in memory starts with a hidden 8-byte
pointer** to its type information (in .NET this is called the **MethodTable**;
it's how `GetType()`, virtual calls and `is` work). The compiler expects
`Object` to declare that field, so give it one: a private `IntPtr` field named
`m_pMethodTable`. (`IntPtr` is C#'s pointer-sized integer, so you'll need
that type too.)

**`Void`, `Int32`, `Byte`, `IntPtr`**: in C#, `void`, `int`, `byte` and `nint`
are nicknames for these structs. They can be **empty**
(`public struct Int32 { }`): the compiler and ILC already know an `Int32` is 4
bytes and how to add two of them. The struct only needs to *exist* so the name
resolves.

**`String`**: a sealed class with one field, `public readonly int Length;`.
In memory, a string is: MethodTable pointer (8 bytes, from `Object`), then the
length (4 bytes), then the characters. Declaring `Length` as the first field
puts it exactly where ILC stores it.

<details>
<summary>Hint</summary>

```csharp
namespace System
{
    public class Object
    {
#pragma warning disable 169   // "field is never used": it's used by the runtime layout
        private IntPtr m_pMethodTable;
#pragma warning restore 169
    }

    public struct Void { }
    public struct Byte { }
    public struct Int32 { }
    public struct IntPtr { }

    public sealed class String
    {
        public readonly int Length;
    }
}
```

</details>

### Round 2: what a struct is

Next: `Predefined type 'System.ValueType'`. Every `struct` in C# secretly
inherits from `System.ValueType` (that's how the compiler knows it's a value
type and not a class). Add it as a public **abstract class**.

### Round 3: attributes are classes too

Next you'll see errors about `System.Attribute` and
`AttributeUsageAttribute`. Attributes like `[DllImport]` are just classes that
inherit from `System.Attribute`. And to *define* an attribute class at all, the
compiler needs `AttributeUsageAttribute`, which describes where an attribute
may be used.

Add:

- `Attribute`: an empty public class.
- `AttributeTargets`: an empty public `enum`.
- `AttributeUsageAttribute`: a sealed class inheriting `Attribute`, with a
  constructor taking an `AttributeTargets`, and two `bool` properties with
  `get; set;`: `AllowMultiple` and `Inherited`.

### Round 4: two more basics

`Boolean` (because you just used `bool`) and `Enum` (because you just declared
an `enum`). Every enum secretly inherits `System.Enum`, which is itself a
`ValueType`. So: `public struct Boolean { }` and
`public abstract class Enum : ValueType { }`.

### Round 5: the interop attributes

Now the compiler gets to your kernel code and complains that `DllImport` and
`UnmanagedCallersOnly` don't exist, and that the namespace
`System.Runtime.InteropServices` doesn't exist either.

In a new `namespace System.Runtime.InteropServices { ... }` block, add two
attribute classes:

- `DllImportAttribute`, with a constructor taking a `string dllName`.
- `UnmanagedCallersOnlyAttribute`, with a public **field** `public string EntryPoint;`
  (it's a field, not a property, because that's how the real one is defined,
  and ILC reads it by name).

The attribute bodies don't *do* anything. The compiler and ILC only look at
the attribute's name and arguments.

### Round 6 and 7: characters, and where they live

`Predefined type 'System.Char'`: `char` is a nickname for `System.Char`, an
empty struct like the others.

Then: `Missing compiler required member
'System.Runtime.CompilerServices.RuntimeHelpers.get_OffsetToStringData'`.

This one is interesting. To compile `fixed (char* p = s)`, the compiler needs
to know **how many bytes from the start of a string object its first character
is**. It asks `RuntimeHelpers.OffsetToStringData`. Look at the layout again:

```
string object:  [ MethodTable pointer ][ Length ][ 'H' ][ 'e' ][ 'l' ]...
bytes:            8                      4         2      2      2
                 ^ object starts here               ^ characters start at 8 + 4 = 12
```

Add a `public static class RuntimeHelpers` in the namespace
`System.Runtime.CompilerServices`, with a static property `OffsetToStringData`
that returns 12. Better than hard-coding 12: return
`sizeof(IntPtr) + sizeof(int)`, which says *why* it's 12. (`sizeof` on these
types needs an `unsafe` context.)

<details>
<summary>Hint</summary>

```csharp
namespace System.Runtime.CompilerServices
{
    public static class RuntimeHelpers
    {
        public static unsafe int OffsetToStringData => sizeof(IntPtr) + sizeof(int);
    }
}
```

</details>

### Round 8: the linker wants four functions

Now `dotnet build` succeeds, and so does ILC. Instead, **the linker** fails:

```
ld.lld: error: undefined symbol: RhpPInvoke
ld.lld: error: undefined symbol: RhpPInvokeReturn
ld.lld: error: undefined symbol: RhpReversePInvoke
ld.lld: error: undefined symbol: RhpReversePInvokeReturn
```

These are **runtime functions** that NativeAOT code calls whenever execution
crosses between C# and native code. `RhpPInvoke`/`RhpPInvokeReturn` wrap every
call from C# *to* C (your `uart_putc`). `RhpReversePInvoke`/`…Return` wrap
every call *from* C into C# (`kernel_main`).

Why do they exist? In normal .NET, the garbage collector must be able to pause
all threads that are running C# code. While a thread is off in native code,
the GC doesn't need to wait for it. These functions keep track of "this thread
is in C# / is in native code right now". **We have no GC**, so they can
do nothing at all. But they must exist.

You'll write them in C#. ILC understands an attribute called
`System.Runtime.RuntimeExportAttribute`: a method marked
`[RuntimeExport("SomeName")]` gets exported under exactly that name, so the
linker can find it. (It's a lower-level cousin of `UnmanagedCallersOnly`, meant
for the runtime itself.)

1. In `namespace System.Runtime`, add an **internal** sealed attribute class
   `RuntimeExportAttribute` with a constructor taking `string entry`.
2. In a namespace of your choice (I'd suggest `Internal.Runtime`), add an
   internal static class with four empty static methods, each taking one
   `IntPtr frame` parameter, each marked `[RuntimeExport("…")]` with one of the
   four names.

<details>
<summary>Hint</summary>

```csharp
namespace System.Runtime
{
    internal sealed class RuntimeExportAttribute : Attribute
    {
        public RuntimeExportAttribute(string entry) { }
    }
}

namespace Internal.Runtime
{
    // No GC, so there is nothing to track on transitions between C# and C.
    internal static class Transitions
    {
        [System.Runtime.RuntimeExport("RhpPInvoke")]
        static void RhpPInvoke(System.IntPtr frame) { }

        [System.Runtime.RuntimeExport("RhpPInvokeReturn")]
        static void RhpPInvokeReturn(System.IntPtr frame) { }

        [System.Runtime.RuntimeExport("RhpReversePInvoke")]
        static void RhpReversePInvoke(System.IntPtr frame) { }

        [System.Runtime.RuntimeExport("RhpReversePInvokeReturn")]
        static void RhpReversePInvokeReturn(System.IntPtr frame) { }
    }
}
```

</details>

`make` should now finish. Look at the size it reports: around 2.5 KB for the
whole kernel, C# included. A normal NativeAOT "Hello World" is over 1 MB,
almost all of it runtime and library you've just replaced with ~60 lines.

<details>
<summary>Full solution: runtime/CoreLib.cs</summary>

```csharp
namespace System
{
    public class Object
    {
#pragma warning disable 169
        // Every object starts with a pointer to its type information.
        private IntPtr m_pMethodTable;
#pragma warning restore 169
    }

    public abstract class ValueType { }
    public abstract class Enum : ValueType { }

    // Primitive types: the compiler knows their size and operations already.
    public struct Void { }
    public struct Boolean { }
    public struct Byte { }
    public struct Char { }
    public struct Int32 { }
    public struct IntPtr { }

    public sealed class String
    {
        // Layout: [MethodTable][Length][chars...]
        public readonly int Length;
    }

    public class Attribute { }
    public enum AttributeTargets { }

    public sealed class AttributeUsageAttribute : Attribute
    {
        public AttributeUsageAttribute(AttributeTargets validOn) { }
        public bool AllowMultiple { get; set; }
        public bool Inherited { get; set; }
    }
}

namespace System.Runtime.InteropServices
{
    public sealed class DllImportAttribute : Attribute
    {
        public DllImportAttribute(string dllName) { }
    }

    public sealed class UnmanagedCallersOnlyAttribute : Attribute
    {
        public string EntryPoint;
    }
}

namespace System.Runtime.CompilerServices
{
    public static class RuntimeHelpers
    {
        public static unsafe int OffsetToStringData => sizeof(IntPtr) + sizeof(int);
    }
}

namespace System.Runtime
{
    internal sealed class RuntimeExportAttribute : Attribute
    {
        public RuntimeExportAttribute(string entry) { }
    }
}

namespace Internal.Runtime
{
    internal static class Transitions
    {
        [System.Runtime.RuntimeExport("RhpPInvoke")]
        static void RhpPInvoke(System.IntPtr frame) { }
        [System.Runtime.RuntimeExport("RhpPInvokeReturn")]
        static void RhpPInvokeReturn(System.IntPtr frame) { }
        [System.Runtime.RuntimeExport("RhpReversePInvoke")]
        static void RhpReversePInvoke(System.IntPtr frame) { }
        [System.Runtime.RuntimeExport("RhpReversePInvokeReturn")]
        static void RhpReversePInvokeReturn(System.IntPtr frame) { }
    }
}
```

</details>

## Step 4: Call C# from C

The C# side exports `kernel_main`. Now call it from `boot_main` in
`boot/main.c`:

1. **Declare** it above `boot_main`, exactly like a function from another C file:
   `int kernel_main(void);`. C doesn't know or care that it was written in C#.
2. **Call** it after the UART is initialised (C# prints through it!), store the
   result, and print it with `uart_puthex`.

`make run`. You should see the output at the top of this lesson. 🎉

<details>
<summary>Solution</summary>

```c
#include "uart.h"

int kernel_main(void);   /* defined in kernel/Kernel.cs */

void boot_main(void)
{
    uart_init();
    uart_puts("Hello from RHOS!\n");

    int result = kernel_main();
    uart_puts("C# returned ");
    uart_puthex(result);
    uart_puts("\n");

    while (1)
        uart_putc(uart_getc());
}
```

</details>

**Look at the machine code ILC made from your C#:** run `make disasm` and search
`build/kernel8.lst` for `Kernel__Print`. ILC names functions
`<Namespace>_<Class>__<Method>`, here `Kernel_Kernel__Print`. You'll find the
loop, the `ldrh` (load 2 bytes) for each `char`, and the `bl uart_putc`.

## Step 5: Let C# use floating point

Here's a trap that will bite you later if we don't defuse it now.

Add this to `Kernel.Main`, before the `return`:

```csharp
double total = 0;
for (int i = 0; i < 10; i++)
    total += i * 0.5;
if (total > 20)
    Print("Float OK\n");
```

(`double` is a nickname for `System.Double`, so add an empty
`public struct Double { }` to your CoreLib first. And why a loop, rather than
just `double d = 3 * 1.5;`? Because the compiler would calculate `3 * 1.5` at
build time and never emit a floating-point instruction at all.)

`make run`. It prints `Hello from C#!` and then… nothing. Frozen.

**Why:** the CPU has a second set of registers for floating-point and SIMD
math (`v0`–`v31`). Out of reset, the CPU **blocks** them: any instruction that
touches them causes an *exception* (a CPU-level error, not a C# exception).
We don't have exception handlers yet (Lesson 5), so the CPU jumps to garbage
and hangs. C was built with `-mgeneral-regs-only` so it never touches those
registers, but ILC uses them freely, even without `double` (for example, to
copy structs quickly). So we need to unblock them.

Two settings control this, one per exception level:

- **`CPACR_EL1`**: bits 20–21 (`FPEN`) must both be 1, otherwise EL1 traps
  floating-point instructions. Set it to `3 << 20`.
- **`CPTR_EL2`**: controls whether EL2 traps them. Set it to `0x33FF`
  (= "trap nothing", with the bits that must be 1 set to 1). QEMU doesn't
  trap here by default, but real hardware might.

**Your task:**

1. In `start.S`, in the EL2 section (next to the other `msr ..._el2` lines),
   write `0x33FF` to `cptr_el2`. Use `ldr x1, =0x33FF` then `msr`.
2. Right after the `in_el1:` label, write `3 << 20` to `cpacr_el1`, followed by
   the instruction **`isb`**. `isb` ("instruction synchronization barrier")
   means "make sure the setting I just changed takes effect before the next
   instruction". You'll need it after many system register changes.

Run again: `Float OK` should appear. You can remove the test lines afterwards.

<details>
<summary>Solution</summary>

In the EL2 part:

```asm
    ldr     x1, =0x33FF        // CPTR_EL2: don't trap floating point to EL2
    msr     cptr_el2, x1
```

At the start of EL1:

```asm
in_el1:
    mov     x1, #(3 << 20)     // CPACR_EL1.FPEN: allow floating point / SIMD
    msr     cpacr_el1, x1
    isb
```

</details>

## Step 6: Give ILC's sections a seat

Open `build/kernel8.map` and find `kernel.o`. ILC puts code and data in some
sections with unusual names: `__managedcode` (C# machine code),
`.dotnet_eh_table`, and `__modules`. Your linker script doesn't mention them,
so `ld.lld` places them wherever it sees fit ("orphan sections"). It happens to
choose sensibly, but a kernel shouldn't rely on luck.

**Your task:** in `boot/linker.ld`, add:

- `*(__managedcode)` to `.text`, after the other code,
- `*(.dotnet_eh_table)` to `.rodata`,
- `*(__modules)` to `.data`.

Rebuild and check in `build/kernel8.map` that they now appear *inside* the
right output sections.

---

## Checkpoint

You're done when `make run` prints `Hello from C#!` and the return value, and
the floating-point test from step 5 doesn't freeze.

What to take away:

1. C# compiles to IL; **NativeAOT's ILC** turns IL into a normal object file.
2. Every C# program needs a **base library**. Ours is tiny and has no runtime
   behind it, so most of its types are empty: the compiler only needs them to
   exist.
3. **Objects start with a MethodTable pointer**, and strings are
   `[MethodTable][Length][chars]`.
4. `DllImport("*")` calls C from C#; `UnmanagedCallersOnly` lets C call C#.
5. The CPU's floating-point registers must be unlocked before C# runs.

Commit, then tell me how it went. **Lesson 4** builds a real runtime: a memory
allocator so `new` works, plus static variables, arrays and your own
`String` methods.

---

## Exercises

### Exercise 1: Break the object layout

In `CoreLib.cs`, delete the `m_pMethodTable` field from `Object`. Rebuild and
run. What happens, and why? Put the field back afterwards.

<details>
<summary>Answer</summary>

`Hello from C#!` prints, followed by a stream of garbage. Without the hidden
field, the compiler thinks `String.Length` is at offset 0, the very start of
the object. But at offset 0, ILC put the MethodTable pointer. So `s.Length`
reads (part of) a memory address, a huge number, and `Print` keeps going far
past the end of the string, printing whatever memory follows.

This is the core idea of a runtime: **the compiler, the runtime and the base
library must all agree on how objects are laid out in memory.** Nothing checks
it for you.

</details>

### Exercise 2: Make `s[i]` work

Change `Print` to use `s[i]` instead of a `fixed` pointer. The compiler will
say `Cannot apply indexing with [] to an expression of type 'string'`. That's
because `String` has no indexer yet.

Rework `String` in `CoreLib.cs`:

1. Make the length field private: `private readonly int _length;`, and add a
   public property `Length` that returns it.
2. Add a second field right after it: `private char _firstChar;`. It sits
   exactly where the first character is.
3. Add an indexer: `public unsafe char this[int index]` whose getter takes
   `fixed (char* p = &_firstChar)` and returns `p[index]`.

You'll meet one more missing type (`System.Reflection.DefaultMemberAttribute`,
which the compiler attaches to any class with an indexer). Add it: an attribute
with a constructor taking `string memberName`.

Note there's no bounds check: `s[1000]` happily reads past the end. Real .NET
would throw `IndexOutOfRangeException`, and we have no exceptions yet. What
could you do instead? (There's no single right answer.)

<details>
<summary>Solution</summary>

```csharp
public sealed class String
{
    private readonly int _length;
    private char _firstChar;

    public int Length => _length;

    public unsafe char this[int index]
    {
        get
        {
            fixed (char* p = &_firstChar)
                return p[index];
        }
    }
}
```

```csharp
namespace System.Reflection
{
    public sealed class DefaultMemberAttribute : Attribute
    {
        public DefaultMemberAttribute(string memberName) { }
    }
}
```

For out-of-range indexes, a kernel usually **panics**: prints an error over the
UART and halts. You could write a `Panic(string message)` method that prints
and then loops forever, and call it when `index` is out of range. We'll build
a proper panic in Lesson 4.

</details>

### Exercise 3: Print numbers from C#

Write `static void PrintHex(ulong value)` in C#, the same algorithm as your
`uart_puthex` in C. You'll need to add `UInt64` to your CoreLib (`ulong` is its
nickname). Indexing into the literal `"0123456789ABCDEF"` needs Exercise 2.

Call it from `Main` with `0xDEADBEEF`.

<details>
<summary>Solution</summary>

```csharp
static void PrintHex(ulong value)
{
    Print("0x");
    for (int shift = 60; shift >= 0; shift -= 4)
        uart_putc((byte)"0123456789ABCDEF"[(int)((value >> shift) & 0xF)]);
}
```

</details>

### Exercise 4 (stretch): Print decimal numbers, and meet the stack canary

Write `static void PrintDec(int value)` that prints a signed number in decimal.
The usual approach: take digits off the end with `% 10` and `/ 10`, store them
in a small buffer, then print the buffer backwards. For the buffer, use
`byte* buf = stackalloc byte[10];` (a C#-style local array on the stack, no
`new` needed).

You'll need a few more types (the compiler will tell you which), and then the
**linker** will ask for two new symbols:

- **`__security_cookie`**: when a method uses `stackalloc`, ILC adds a
  **stack canary**: a secret value written next to the buffer, checked before
  the method returns. If a bug wrote past the end of the buffer, the canary is
  damaged and the program stops instead of continuing with corrupted memory.
  `__security_cookie` is that secret value. Define it in C (in `main.c`):
  `unsigned long __security_cookie = 0x2B992DDFA232UL;` (any value that isn't 0
  works; in a real OS it'd be random at boot).
- **`RhpFallbackFailFast`**: what to call when the canary is damaged. Add a
  fourth `[RuntimeExport]` method that loops forever (`while (true) { }`).

<details>
<summary>Solution</summary>

```csharp
static void PrintDec(int value)
{
    if (value < 0)
    {
        uart_putc((byte)'-');
        value = -value;
    }

    byte* buf = stackalloc byte[10];   // int has at most 10 digits
    int n = 0;
    do
    {
        buf[n++] = (byte)('0' + value % 10);
        value /= 10;
    } while (value != 0);

    while (n > 0)
        uart_putc(buf[--n]);
}
```

Types to add: `UInt32` and `UIntPtr` (empty structs).

```csharp
[System.Runtime.RuntimeExport("RhpFallbackFailFast")]
static void RhpFallbackFailFast() { while (true) { } }
```

(`int.MinValue` breaks this function: `-int.MinValue` doesn't fit in an
`int`. Spotting that is a bonus point.)

</details>

---

## Glossary

| Term | Plain meaning |
|------|---------------|
| **IL** | Intermediate Language: the bytecode in a `.dll`, before it becomes real machine code |
| **JIT** | Just-in-time compiler: turns IL into machine code while the program runs (normal .NET) |
| **NativeAOT** | Ahead-of-time compilation: turns IL into machine code at build time |
| **ILC** | The NativeAOT compiler program |
| **CoreLib** | `System.Private.CoreLib`: the base library that defines `Object`, `String`, `Int32`… |
| **Runtime** | The program that supports running C#: GC, type checks, exceptions. RHOS's is tiny |
| **MethodTable** | A type's information block. Every object starts with a pointer to its type's MethodTable |
| **P/Invoke** | Calling native (C) code from C#: `DllImport` |
| **Reverse P/Invoke** | Native (C) code calling C#: `UnmanagedCallersOnly` |
| **`RuntimeExport`** | ILC attribute: export a C# method under a fixed symbol name |
| **Stack canary** | A secret value next to a stack buffer, checked to detect overflows |
| **`isb`** | "Make my last system register change take effect now" |
