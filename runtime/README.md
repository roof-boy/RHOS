# runtime/ — the minimal C# runtime

NativeAOT normally links against a full .NET runtime (GC, type system,
exceptions, threads). None of that exists on bare metal, so RHOS ships its
own tiny replacement, in the style of
[zerosharp](https://github.com/MichalStrehovsky/zerosharp).

Built up in Lessons 3 and 4:

- `System.Private.CoreLib` stand-in: `Object`, `String`, `Array`, primitive types
- object layout (`MethodTable` pointer + fields) and allocation helpers
- a bump allocator first, a real heap later
- the runtime helpers the compiler calls into (`RhpNewFast`, etc.)
