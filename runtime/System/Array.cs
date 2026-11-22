namespace System
{
    // Layout: [MethodTable*][int length][4 bytes padding][elements...]
    public abstract class Array
    {
        private readonly int _length;
        public int Length => _length;
    }

    // How ILC describes T[] internally.
    internal class Array<T> : Array { }
}
