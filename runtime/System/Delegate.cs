namespace System
{
    // The C# compiler wants these to exist (it checks them while resolving
    // `==`). Delegates and lambdas don't work in RHOS yet.
    public abstract class Delegate { }
    public abstract class MulticastDelegate : Delegate { }
}
