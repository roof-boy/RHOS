namespace System.Collections
{
    // `foreach` over an array compiles to a plain index loop, but the C#
    // compiler still wants these interfaces to exist.
    public interface IEnumerable
    {
        IEnumerator GetEnumerator();
    }

    public interface IEnumerator
    {
        object Current { get; }
        bool MoveNext();
    }
}
