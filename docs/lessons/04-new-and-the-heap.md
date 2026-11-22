# Lesson 4: Making `new` work

**Goal:** normal-looking C# runs in your kernel: classes, `new`, virtual
methods, arrays, and static variables.

```csharp
shapes = new Shape[4];
for (int i = 0; i < shapes.Length; i++)
    shapes[i] = Make(i);          // new Square(...) or new Circle(...)
total += shapes[i].Area();        // virtual call
```

**This lesson is in four parts.** Each one ends with something working, and
with a ⏸️ **good place to stop** marker. Do one part per sitting if you like.

| Part | You build | Feels like |
|------|-----------|------------|
| 1. Statics | Nothing! See that they already work, and why | 10 minutes |
| 2. The heap | A memory allocator in C# | 30–45 minutes |
| 3. Objects | `new`, by writing the function the compiler calls for it | 30–45 minutes |
| 4. Arrays | `new int[10]`, bounds checks, a kernel panic | 45 minutes |

**Already done for you:** the Makefile now also builds assembly files in
`runtime/native/` (you'll write one in part 3). Run `git pull`.

---

## Part 1: Static variables

Add a static field to your `Kernel` class and use it:

```csharp
static int bootCount;

// in Main:
bootCount++;
bootCount++;
PrintDec(bootCount);
```

`make run`: it prints `2`. Statics just work. But *why* do they work, when
there's no runtime?

**Where they live:** ILC gives each class's static fields a fixed spot in the
image, like a C global variable. Run `llvm-nm build/kernel8.elf | grep -i statics`
and you'll find a symbol like `__NONGCSTATICSKernel_Kernel`, in the **`.bss`**
section (lowercase `b` in the `nm` output means `.bss`).

**Why they start at 0:** because **your** zeroing loop in `start.S` (Lesson 2)
clears `.bss` before any C# runs. That's the loop paying off: in C#, every
static field is guaranteed to start at `0`/`null`/`false`, and on bare metal
that guarantee is *your code*.

> One limit: a static field **with an initializer** (`static int x = 5;`) or a
> static constructor needs extra runtime support. ILC can often pre-compute
> simple ones at build time, but not always. Stick to "starts at 0, set it in
> code" for now.

⏸️ **Good place to stop.**

---

## Part 2: The heap

### Stack vs heap

You know this from C#, but here's what it means at the hardware level.

- **The stack** is the memory `sp` points at. Every method call takes a chunk
  for its local variables and gives it back when it returns. It's fast and
  automatic, but anything on it disappears when the method returns.
- **The heap** is a big pool of memory for things that must outlive the method
  that created them. In C#, `new` gets memory from the heap.

There is no heap yet. In .NET, the runtime gets a big block of memory from the
OS and manages it. **We are the OS**, so we just pick some free RAM and declare
it "the heap".

### Which memory?

Your memory map right now:

```
0x00000000  ┌──────────────┐
            │ (firmware)   │
            │   stack ↓    │  grows down from 0x80000
0x00080000  ├──────────────┤
            │ kernel code, │
            │ data, .bss   │
            ├──────────────┤  ← __heap_start (you'll add this)
            │    heap      │  16 MB
            ├──────────────┤
            │  free RAM    │
            ...
0x3F000000  └──────────────┘  devices start here
```

The heap goes right after the kernel. The linker knows where the kernel ends,
so ask it for a label there.

### The simplest allocator there is: a bump allocator

A **bump allocator** is one pointer, `next`, that starts at the beginning of
the heap. To allocate `n` bytes, you return the current `next` and move it
forward by `n`. That's it.

```
before:  [used used used|next→ free free free free ........ end]
alloc(8)                 ^ returned
after:   [used used used used|next→ free free free ........ end]
```

It can never free anything. That sounds bad, but a kernel allocates a lot of
things at boot that live forever, so it'll serve us well for a while. A smarter
allocator can come later.

Two details:

- **Alignment.** Round every size up to a multiple of 8, so every allocation
  starts at an address divisible by 8. The CPU requires 8-byte values (like
  pointers) to be at such addresses while the MMU is off, otherwise it faults.
  The trick to round up to a multiple of 8: `(size + 7) & ~7UL`.
- **Zeroing.** C# promises that a new object's fields start at 0. Real RAM is
  full of leftovers, so the allocator clears each allocation.

### Your tasks

**1. Mark the heap in the linker script.** At the end of `SECTIONS` in
`boot/linker.ld`, after `__bss_end`, add:

```ld
    . = ALIGN(4096);
    __heap_start = .;
```

(4096-byte alignment isn't required yet, but memory pages are 4096 bytes, and
it'll be convenient in Lesson 5.)

**2. Pass the heap to C#.** C# can't see linker symbols directly, but C can,
and C can pass them as arguments. In `boot/main.c`:

```c
extern char __heap_start[];   /* defined by the linker script */
```

This declares `__heap_start` as "an array of bytes somewhere". Using its name
gives you its address, which is exactly the label's address.

Change `kernel_main`'s declaration to take two arguments, the heap's start and
its size, and pass `__heap_start` and `16 * 1024 * 1024` (16 MB). In C#, change
`Main`'s signature to match: `public static int Main(void* heapStart, ulong heapSize)`.

**3. Write the allocator.** Create `runtime/Allocator.cs` (the runtime project
picks up new files automatically) with:

```csharp
namespace Internal.Runtime
{
    public static unsafe class Allocator
    {
        static byte* _next;
        static byte* _end;

        public static void Init(void* start, ulong size) { /* you write this */ }
        public static void* Alloc(ulong size)              { /* and this */ }
    }
}
```

`Alloc` should: round `size` up to a multiple of 8, check it fits before
`_end` (if not, loop forever for now; you'll add a real panic in part 4), zero
the bytes, move `_next` forward, and return the old `_next`.

**4. Try it.** In `Main`, call `Allocator.Init(heapStart, heapSize)` first, then
allocate a few blocks and print their addresses with `PrintHex`:

```csharp
PrintHex((ulong)Internal.Runtime.Allocator.Alloc(5));  Print("\n");
PrintHex((ulong)Internal.Runtime.Allocator.Alloc(16)); Print("\n");
```

The first address should equal `__heap_start` (check it in
`build/kernel8.map`), and the second should be 8 bytes later, not 5, because
of the rounding.

<details>
<summary>Solution</summary>

`boot/main.c`:

```c
int kernel_main(void *heap_start, uint64_t heap_size);
extern char __heap_start[];

/* in boot_main: */
uint32_t result = kernel_main(__heap_start, 16 * 1024 * 1024);
```

`runtime/Allocator.cs`:

```csharp
namespace Internal.Runtime
{
    public static unsafe class Allocator
    {
        static byte* _next;
        static byte* _end;

        public static void Init(void* start, ulong size)
        {
            _next = (byte*)start;
            _end = (byte*)start + size;
        }

        public static void* Alloc(ulong size)
        {
            size = (size + 7) & ~7UL;          // round up to a multiple of 8

            if (_next + size > _end)
                while (true) { }               // out of memory (panic in part 4)

            byte* block = _next;
            _next += size;

            for (ulong i = 0; i < size; i++)   // C# promises zeroed memory
                block[i] = 0;

            return block;
        }
    }
}
```

</details>

⏸️ **Good place to stop.** You have a working memory allocator.

---

## Part 3: `new` for objects

### What `new` compiles to

When you write `new Point(3, 4)`, ILC turns it into two steps:

1. **Call a runtime function named `RhpNewFast`**, passing it a pointer to
   `Point`'s **MethodTable**. It must return a pointer to fresh, zeroed memory
   big enough for a `Point`, with the MethodTable pointer already written into
   the first 8 bytes.
2. Call `Point`'s constructor on that memory.

Step 2 is normal compiled code. Step 1 is yours to write, and your allocator
does most of it. (Remember Lesson 3: every object starts with a MethodTable
pointer. Here's where it gets written.)

### What's in a MethodTable?

A MethodTable is a block of type information that ILC builds into the image,
one per type. It's how `GetType()`, virtual methods and type checks work. You
only need its first 8 bytes:

| Offset | Size | Field | Meaning |
|--------|------|-------|---------|
| 0 | 2 bytes | `ComponentSize` | For arrays: the size of one element. For normal classes: 0 |
| 2 | 2 bytes | `Flags` | Various flags; ignore for now |
| 4 | 4 bytes | `BaseSize` | How many bytes one instance needs, MethodTable pointer included |

So `new Point` needs `BaseSize` bytes.

### Your tasks

**1. Something to allocate.** Add a small class to `Kernel.cs`, outside the
`Kernel` class:

```csharp
class Point
{
    public int X, Y;
    public Point(int x, int y) { X = x; Y = y; }
    public int Sum() => X + Y;
}
```

And in `Main`, store a new one in a **static field** (`static Point saved;`):

```csharp
saved = new Point(3, 4);
PrintDec(saved.Sum());
```

Why a static field? If you only used a local variable, ILC is clever enough to
notice the object never leaves `Main`, and puts it on the **stack** instead.
No `new` call would happen, and you'd learn nothing. Storing it in a static
forces a real heap allocation.

**2. Build, and read the linker errors.**

```
ld.lld: error: undefined symbol: RhpNewFast
ld.lld: error: undefined symbol: RhpAssignRefArm64
```

Two runtime functions are missing. One at a time.

**3. `RhpNewFast`.** In `runtime/Allocator.cs`:

- Add a struct describing the start of a MethodTable, with the three fields
  from the table above, in that order: `ushort ComponentSize`, `ushort Flags`,
  `uint BaseSize`. (`ushort` means you need `UInt16` in your CoreLib.) Make it
  `internal struct MethodTable` in the `Internal.Runtime` namespace.
- Add a method exported with `[RuntimeExport("RhpNewFast")]` (you'll need
  `using System.Runtime;` at the top) that takes a `MethodTable* type`,
  allocates `type->BaseSize` bytes, writes `type` into the first 8 bytes of the
  new block, and returns the block.

What should it return? In real .NET it returns `object`, but you can't turn a
raw pointer into an `object` in C# without more machinery. Return `void*`
instead. At the machine level both are just an address in register `x0`, so
the compiled code calling it doesn't notice the difference.

The `->` operator is C#'s (and C's) "field of the struct this pointer points
at": `type->BaseSize` is `(*type).BaseSize`.

Writing the MethodTable pointer: `*(MethodTable**)block = type;`. Read it as
"treat `block` as a pointer to a `MethodTable*`, and store `type` there".

<details>
<summary>Solution</summary>

```csharp
using System.Runtime;

namespace Internal.Runtime
{
    // The first 8 bytes of every MethodTable.
    internal struct MethodTable
    {
        public ushort ComponentSize;   // arrays: size of one element; otherwise 0
        public ushort Flags;
        public uint BaseSize;          // bytes per instance, MethodTable pointer included
    }

    public static unsafe class Allocator
    {
        // ... _next, _end, Init, Alloc from part 2 ...

        // Called by compiled code for every `new SomeClass(...)`.
        [RuntimeExport("RhpNewFast")]
        static void* RhpNewFast(MethodTable* type)
        {
            void* obj = Alloc(type->BaseSize);
            *(MethodTable**)obj = type;
            return obj;
        }
    }
}
```

</details>

**4. `RhpAssignRefArm64`: the write barrier.** This is called every time your
code stores a **reference** (an object) into the heap or into a static field,
like `saved = new Point(...)`.

Why does such a function exist? In .NET, the garbage collector needs to know
when one object starts pointing at another, so every reference store goes
through this function and it takes notes. It's called a **write barrier**. We
have no GC, so our write barrier only has to do the store itself.

It has to be written in **assembly**, because it doesn't use the normal calling
convention. To make it as fast as possible, the compiler passes its arguments
in unusual registers:

- `x14` = the address to store into,
- `x15` = the reference to store,
- and afterwards, `x14` must point 8 bytes further (some compiled code relies
  on that to store several references in a row).

Create `runtime/native/barriers.S` with a function `RhpAssignRefArm64` that
does exactly that, in two instructions. You already know both from Lesson 2:
a `str` that stores **and** moves the address forward, then `ret` (return).
Remember `.global` so the linker can see the name.

While you're there, ILC sometimes uses two close relatives. Give
`RhpCheckedAssignRefArm64` the same code (you can put two labels on the same
instruction), and add `RhpByRefAssignRefArm64`, which **copies** a reference:
load from address `x13` (moving `x13` forward 8), then store to `x14` (moving
`x14` forward 8).

<details>
<summary>Solution</summary>

```asm
// Write barriers: called for every store of an object reference.
// No GC, so all they do is the store.
.text

// x14 = where to store, x15 = what to store. x14 moves forward by 8.
.global RhpAssignRefArm64
.global RhpCheckedAssignRefArm64
RhpAssignRefArm64:
RhpCheckedAssignRefArm64:
    str     x15, [x14], #8
    ret

// Copy a reference from [x13] to [x14]. Both move forward by 8.
.global RhpByRefAssignRefArm64
RhpByRefAssignRefArm64:
    ldr     x15, [x13], #8
    str     x15, [x14], #8
    ret
```

</details>

**5. Run it.** `make run` should print `7`. Your C# just used `new`. 🎉

**6. Try virtual methods.** Add these classes:

```csharp
abstract class Shape
{
    public abstract int Area();
    public virtual int Sides() => 0;
}

class Square : Shape
{
    int side;
    public Square(int side) { this.side = side; }
    public override int Area() => side * side;
    public override int Sides() => 4;
}
```

Store a `new Square(3)` in a `static Shape` field and print `Area()` and
`Sides()`. It just works, with nothing new in the runtime. Why? A virtual call
reads the object's MethodTable pointer, then jumps to an address stored in the
MethodTable (its **vtable**, the table of virtual method addresses). ILC built
all of that into the image at compile time. The only thing the runtime had to
do was write the MethodTable pointer into each new object, which your
`RhpNewFast` does.

⏸️ **Good place to stop.** You have objects and virtual methods.

---

## Part 4: Arrays, and a kernel panic

### How an array looks in memory

```
int[] with 3 elements:
[ MethodTable pointer ][ Length ][ padding ][ elem 0 ][ elem 1 ][ elem 2 ]
  8 bytes                4 bytes   4 bytes    4 bytes   4 bytes   4 bytes
                                             ^ elements start at offset 16
```

Its MethodTable's `ComponentSize` is the size of one element (4 for `int`, 8
for a reference), and `BaseSize` is the size before the elements. So an array
of `length` elements needs `BaseSize + length × ComponentSize` bytes.

### Your tasks

**1. Use an array.** Add a static field `static int[] squares;` to `Kernel`,
and in `Main`:

```csharp
squares = new int[10];
for (int i = 0; i < squares.Length; i++)
    squares[i] = i * i;
PrintDec(squares[9]);
```

(A static field again, for the same reason as `saved` in part 3. With a local
`int[]`, ILC puts the array on the stack and even removes the bounds checks,
because it can prove `i` is always in range. Clever, but then you'd skip half
of this part.)

**2. Follow the errors.** In order, you'll meet:

- **`Predefined type 'System.Array'`** and **`'int[]' does not contain a
  definition for 'Length'`**. Every array type inherits `System.Array`. Add a
  public abstract class `Array` with a private `int` field for the length and a
  public `Length` property returning it. Like `String`'s `Length` in Lesson 3,
  the field's position (right after the MethodTable pointer) is what makes it
  line up with the real length in memory.
- From ILC: **`Failed to load type 'System.Array`1'`** and **`Expected type
  'Internal.Runtime.CompilerHelpers.ThrowHelpers'`**. ILC describes `int[]`
  internally as `Array<int>`, so add `internal class Array<T> : Array { }`. And
  it needs a class `ThrowHelpers` in the `Internal.Runtime.CompilerHelpers`
  namespace (an empty `internal static class` for now).
- **`Expected method 'ThrowIndexOutOfRangeException' not found on type
  ThrowHelpers`**. Every `squares[i]` is **bounds-checked**: if `i` is out of
  range, compiled code calls `ThrowHelpers.ThrowIndexOutOfRangeException()`.
  In normal .NET that throws an exception. We have no exceptions, so we
  **panic** instead (next task). Make it an `internal static void` method.
- Linker: **`undefined symbol: RhpNewArrayFast`**. The array version of
  `RhpNewFast`. It takes `(MethodTable* type, int length)`. Allocate
  `BaseSize + length × ComponentSize` bytes, write the MethodTable pointer at
  offset 0 and the length at offset 8 (`*(int*)(obj + 8) = length;`, with `obj`
  as a `byte*`).

**3. A kernel panic.** A **panic** is what a kernel does when something is so
wrong it can't continue: print a message, then stop. Linux and macOS both have
one. Create `runtime/Panic.cs`:

```csharp
namespace Internal.Runtime
{
    public static class Panic
    {
        public static void Halt(string message) { /* you write this */ }
    }
}
```

It should print `PANIC: ` followed by the message and a newline, then loop
forever. To print from the runtime project, give it its own
`[DllImport("*")] static extern void uart_putc(byte c);`. (It's a bit untidy for
the runtime to call the UART directly; we'll give it a proper console later.)

Use it in `ThrowIndexOutOfRangeException` (`"index out of range"`) and in your
allocator's out-of-memory case (`"out of memory"`).

Then **test it**: after your loop, write to `squares[100]`. You should see
`PANIC: index out of range`. In normal .NET, that line would have been an
`IndexOutOfRangeException`. In your kernel, it's a clean, loud stop instead of
silently corrupting memory.

<details>
<summary>Solution: CoreLib additions</summary>

```csharp
namespace System
{
    public abstract class Array
    {
        private int _length;               // right after the MethodTable pointer
        public int Length => _length;
    }

    // How ILC describes T[] internally.
    internal class Array<T> : Array { }
}

namespace Internal.Runtime.CompilerHelpers
{
    // Called by compiled code when something goes wrong.
    internal static class ThrowHelpers
    {
        internal static void ThrowIndexOutOfRangeException()
            => Internal.Runtime.Panic.Halt("index out of range");
    }
}
```

</details>

<details>
<summary>Solution: RhpNewArrayFast and Panic</summary>

In `Allocator`:

```csharp
// Called by compiled code for every `new T[length]`.
[RuntimeExport("RhpNewArrayFast")]
static void* RhpNewArrayFast(MethodTable* type, int length)
{
    ulong size = type->BaseSize + (ulong)length * type->ComponentSize;
    byte* obj = (byte*)Alloc(size);
    *(MethodTable**)obj = type;
    *(int*)(obj + 8) = length;
    return obj;
}
```

`runtime/Panic.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Internal.Runtime
{
    public static class Panic
    {
        [DllImport("*")]
        static extern void uart_putc(byte c);

        static void Print(string s)
        {
            for (int i = 0; i < s.Length; i++)
                uart_putc((byte)s[i]);
        }

        public static void Halt(string message)
        {
            Print("PANIC: ");
            Print(message);
            Print("\n");
            while (true) { }
        }
    }
}
```

</details>

**4. Arrays of objects.** Last one. Back to shapes:

```csharp
static Shape[] shapes;

static Shape Make(int i)
{
    if (i % 2 == 0) return new Square(i);
    return new Circle(i);    // write a Circle : Shape class, Area() => 3 * r * r
}

// in Main:
shapes = new Shape[4];
for (int i = 0; i < shapes.Length; i++)
    shapes[i] = Make(i);
```

Two more things to add:

- ILC: **`Expected type 'System.Runtime.TypeCast'`**. This is a fun one.
  C# arrays are **covariant**: a `Shape[]` variable might really hold a
  `Square[]`. Then storing a `Circle` into it must fail. So when the compiler
  can't be sure, a store into a reference array goes through a helper,
  `TypeCast.StelemRef(object[] array, nint index, object obj)`, that checks
  the type and then stores.

  We'll skip the type check for now (and leave a comment saying so), but we
  still need the store. Careful: you **can't** write `array[index] = obj;` inside
  `StelemRef`, because that line *itself* compiles to a call to `StelemRef`.
  It would call itself forever. Instead, do the store with pointers:

  ```csharp
  byte* arrayAddress = *(byte**)&array;                       // the array's address
  *(void**)(arrayAddress + 16 + index * 8) = *(void**)&obj;   // elements start at 16
  ```

  `&array` is "the address of the local variable `array`", and `*(byte**)` reads
  the reference stored in it as a plain pointer. The compiler will warn
  (`CS8500`) that this takes the address of a managed type. Here, that's
  exactly what we mean, so it's safe to ignore. Don't forget the bounds check
  first: call `ThrowIndexOutOfRangeException` if `index` is out of range.
- Linker: **`RhpNewPtrArrayFast`**: arrays whose elements are references get
  their own allocation function. It's identical to `RhpNewArrayFast`; export a
  second method that just calls the first.

<details>
<summary>Solution</summary>

```csharp
namespace System.Runtime
{
    internal static unsafe class TypeCast
    {
        // Called for `array[index] = obj` when the array holds references.
        // TODO: real .NET checks obj's type is allowed in this array (covariance).
        static void StelemRef(object[] array, nint index, object obj)
        {
            if ((uint)index >= (uint)array.Length)
                Internal.Runtime.CompilerHelpers.ThrowHelpers.ThrowIndexOutOfRangeException();

            byte* arrayAddress = *(byte**)&array;
            *(void**)(arrayAddress + 16 + index * 8) = *(void**)&obj;
        }
    }
}
```

```csharp
// In Allocator:
[RuntimeExport("RhpNewPtrArrayFast")]
static void* RhpNewPtrArrayFast(MethodTable* type, int length)
    => RhpNewArrayFast(type, length);
```

</details>

Print the sum of every shape's `Area()`. For `Make(0..3)` that's
`0 + 3 + 4 + 27 = 34`.

---

## Checkpoint

You're done when classes, virtual methods, `int[]` and `Shape[]` all work, and
an out-of-range index prints `PANIC: index out of range`.

What to take away:

1. Static fields live in `.bss`, and your boot code's zeroing is what makes
   them start at 0.
2. A **heap** is just memory you've decided to hand out. A **bump allocator**
   hands it out in order and never takes it back.
3. `new` is a call to **`RhpNewFast`** with the type's **MethodTable**; the
   runtime allocates `BaseSize` bytes and writes the MethodTable pointer.
4. **Write barriers** exist for the GC; without a GC they're a plain store.
5. Arrays are `[MethodTable][Length][elements]`, and bounds checks call a
   helper that, in our kernel, **panics**.

Commit, and tell me how it went. Next up is **Lesson 5, the kernel proper**:
exception handlers (so crashes print a message instead of freezing), the
timer, and interrupts. That's where RHOS starts to feel like an OS.

---

## Optional exercises

**A. Allocator statistics.** Add `public static ulong BytesUsed` to `Allocator`
(how far `_next` has moved from the start) and print it after creating the
shapes. How many bytes does one `Square` take? Does that match its `BaseSize`?

<details>
<summary>Answer</summary>

A `Square` takes **24 bytes**: 8 for the MethodTable pointer, 4 for `side`,
then padding. NativeAOT never makes an object smaller than 24 bytes on 64-bit,
so even a class with no fields takes 24. A `Shape[4]` takes 24 + 4 × 8 = 56.

</details>

**B. Run out of memory on purpose.** Pass a tiny heap size (say 64 bytes) from
`main.c` and allocate in a loop. You should get `PANIC: out of memory`.

**C. `null`.** What happens if you call `saved.Sum()` while `saved` is still
`null`? Try it, and explain what you see.

<details>
<summary>Answer</summary>

It prints a meaningless number instead of crashing. `null` is address 0, and
`Sum()` reads `X` and `Y` from addresses `0 + 8` and `0 + 12`. With the MMU
off, address 0 is ordinary RAM (QEMU's boot stub lives there), so the reads
succeed and return whatever bytes happen to be there. Normal .NET throws
`NullReferenceException` because the OS deliberately leaves the first page of
memory unmapped, so touching it causes a fault. Once we turn on the MMU in
Lesson 5, we can do the same, and catch the fault.

</details>
