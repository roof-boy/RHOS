# runtime/ — the minimal C# runtime

NativeAOT normally links against a full .NET runtime (GC, type system,
exceptions, threads). None of that exists on bare metal, so RHOS ships its
own tiny replacement, in the style of
[zerosharp](https://github.com/MichalStrehovsky/zerosharp).
[Lesson 4](../docs/lessons/04-new-and-the-heap.md) is a tour of it.

- `System/`: the types C# needs (`Object`, `String`, `Array`, primitives,
  `Console`, `Exception`...)
- `Internal/`: what compiled code calls behind your back (the allocator,
  casts, panics, number formatting)
- `native/`: write barriers, in assembly

Not supported yet: interfaces, generics collections, `try`/`catch`
(`throw` panics), delegates, garbage collection.
