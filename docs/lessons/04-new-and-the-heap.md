# Lesson 4: A tour of the runtime

**Goal:** know what the C# runtime in `runtime/` does, well enough to use it
and to find your way around when something breaks. You don't write any of it.

In Lesson 3 you built the smallest possible CoreLib by hand. Doing the rest
the same way (strings, arrays, casts...) teaches C# compiler trivia more than
OS internals, so it's now finished for you. This lesson is a 20 minute
read, plus a few small things to try.

**First:** `git pull`, then `make run`. You should see:

```
Exception level: 0x0000000000000001
Hello from C#!
Heap: 0x0000000000082000, 16383 KB free
Square 3
Rect 2x5
Square 4
Total area: 35
```

Open `kernel/Kernel.cs`: that's ordinary C#. `new`, arrays, `foreach`,
virtual methods, `ToString()`, and `"Rect " + w + "x" + h` all work.

---

## What works, and what doesn't (yet)

| Works | Doesn't work yet |
|-------|------------------|
| classes, structs, `new`, inheritance, `virtual`/`override`/`abstract` | interfaces (casting to one panics) |
| static fields | generics like `List<T>` (no collections library yet) |
| arrays of anything, `foreach` over arrays, `.Length` | `try`/`catch`: `throw` prints the message and stops |
| strings: `+`, `==`, `.Length`, `[i]` | lambdas and delegates |
| `ToString()` on numbers, `bool`, `char` and your own classes | `float`/`double` formatting (the maths works) |
| `is`, `as` and casts between classes | garbage collection: memory is never freed |
| `Console.Write`, `Console.WriteLine`, `Console.WriteHex` | |

That's plenty to write a kernel with. The missing pieces arrive when we need
them.

---

## The map

```
runtime/
  System/                 the types C# itself needs
    Object.cs             base class of everything
    String.cs             string, + (Concat), ==
    Array.cs              base class of every T[]
    Primitives.cs         int, bool, char... and their ToString()
    Console.cs            Write / WriteLine over the UART
    Exception.cs          Exception and friends
    Attributes.cs, Type.cs, Delegate.cs, Collections.cs
                          empty types the compiler insists exist
  Internal/               the machinery underneath
    Allocator.cs          the heap (your bump allocator, finished)
    MethodTable.cs        the type header every object points to
    Number.cs             turns numbers into strings
    Panic.cs              print a message and stop
    TypeCast.cs           is / as / casts, storing into object[]
    RuntimeExports.cs     functions compiled code calls by name
  native/barriers.S       "write barriers" (see below), in assembly
```

Two folders, two jobs: **`System/`** is what *you* see from C#.
**`Internal/`** is what *the compiler* calls behind your back.

⏸️ **Good place to stop.**

---

## Follow one line: `shapes = new Shape[] { new Square(3), ... }`

The best way to understand a runtime is to watch it work. Here's what
happens when that line runs.

### 1. `new Square(3)`: an object

ILC turns `new Square(3)` into two steps:

1. Call **`RhpNewFast(squareType)`**. `squareType` is a pointer to Square's
   **MethodTable**: a small block ILC wrote into the image that describes the
   type (how big an object is, its parent class, its virtual methods).
2. Call the constructor `Square(3)` on the memory it got back.

`RhpNewFast` is in `Internal/Allocator.cs`. It's 3 lines:

```csharp
void* obj = Alloc(type->BaseSize);   // get BaseSize bytes from the heap
*(MethodTable**)obj = type;          // first 8 bytes: "I am a Square"
return obj;
```

So a `Square` in memory is:

```
offset 0   pointer to Square's MethodTable   (every object starts with this)
offset 8   int side = 3
offset 12  (padding; objects are at least 24 bytes)
```

That first pointer is how virtual calls work: `s.Area()` reads the object's
MethodTable pointer, finds `Area` in its list of methods, and calls it. A
`Square` and a `Rect` have different MethodTables, so the same line calls
different code.

### 2. `Alloc`: the heap

`Alloc` is the bump allocator from your Part 2. The heap is the RAM right
after the kernel image (`__heap_start` in `boot/linker.ld`), and `Alloc` just
moves a pointer forward:

```
before:  [kernel image][ _next ->                           ]
after:   [kernel image][ Square ][ _next ->                 ]
```

It rounds sizes up to 8, zeroes the memory (C# promises fields start at 0),
and panics with `out of memory` if the heap is full. Nothing is ever freed.
That's fine for a long time: 16 MB is a lot of `new`.

### 3. `new Shape[3]`: an array

Arrays go through **`RhpNewPtrArrayFast(type, length)`** (or
`RhpNewArrayFast` for arrays of numbers). Both call `NewArray`, which
allocates `BaseSize + length × ComponentSize` and writes the length:

```
offset 0   MethodTable pointer (for Shape[])
offset 8   int length = 3
offset 16  element 0   (an 8-byte pointer to a Square)
offset 24  element 1
offset 32  element 2
```

`shapes[5]` compiles to "compare 5 with the length at offset 8; if it's not
smaller, call `ThrowIndexOutOfRangeException`". That's in
`Internal/RuntimeExports.cs`, and it calls `Panic.Halt("index out of range")`.

### 4. Storing into the array: write barriers

Storing a reference (`shapes[0] = square`) doesn't compile to a plain store.
It calls **`RhpAssignRefArm64`** in `runtime/native/barriers.S`. In real
.NET, this is where the garbage collector gets told "this array now points
at that object". We have no GC, so ours is just the store. It's in assembly
because the compiler calls it with an unusual register convention.

### 5. `"Rect " + w + "x" + h`: strings

The C# compiler turns this into `string.Concat("Rect ", w.ToString(), "x",
h.ToString())`. `int.ToString()` lives in `Primitives.cs` and asks
`Number.FormatSigned` for the digits. `Concat` (in `String.cs`) allocates a
new string of the total length and copies the pieces in.

A string in memory is like an array of `char`, with the characters right
after the length:

```
offset 0   MethodTable pointer (for String)
offset 8   int length
offset 12  'R' 'e' 'c' 't' ...   (2 bytes each)
```

### 6. `Console.WriteLine(s)`

`Console.cs` walks the characters and calls `uart_putc` from your
`boot/uart.c` with `[DllImport("*")]`. Your Lesson 2 code is still the only
thing that actually talks to hardware.

⏸️ **Good place to stop.**

---

## When things go wrong: panics

The runtime can't recover from errors yet, so every error ends in
`Panic.Halt`, which prints `PANIC: <message>` and stops the CPU:

| You do | You see |
|--------|---------|
| `arr[10]` on a 3-element array | `PANIC: index out of range` |
| `throw new InvalidOperationException("oops")` | `PANIC: oops` |
| `(Rect)someSquare` | `PANIC: invalid cast` |
| allocate more than 16 MB | `PANIC: out of memory` |

One error is missing from that table: using a `null` object. On a PC, that
crashes because address 0 is protected. On our Pi there's no protection yet
(that needs the MMU, a later lesson), so reading through `null` silently
reads whatever is at address 0. In Lesson 5 you'll make the CPU report bad
memory accesses instead of ignoring them.

---

## Try it

Edit `kernel/Kernel.cs`, `make run`, see what happens. No answers needed:
the output is the answer.

1. Add a `Circle` class with an `Area()` that returns `3 * r * r`, and put one
   in the array.
2. Print `shapes[3]`. Read the panic message.
3. Print `Internal.Runtime.Allocator.Free` before and after creating 1000
   `new Square(1)` objects in a loop. How many bytes did each one take?
   (Remember the minimum object size.)
4. Write `throw new ArgumentException("my first kernel panic");` at the end of
   `Main`.

<details>
<summary>Answer to 3</summary>

24 bytes each, so `Free` drops by 24000. A `Square` needs only 12 bytes (8
for the MethodTable pointer, 4 for `side`) but every object is at least 24
bytes, because the real .NET runtime expects that and ILC lays types out the
same way.

</details>

---

## What's next

Lesson 5 is real OS work: the CPU's **exception vectors**, **system calls**,
and **timer interrupts**. By the end your kernel will count time on its own
and catch its own crashes.
