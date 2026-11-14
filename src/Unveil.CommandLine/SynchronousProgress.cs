namespace Unveil.CommandLine;

/// <summary>IProgress that reports on the calling thread (Progress&lt;T&gt; posts to the thread pool in a console app).</summary>
public sealed class SynchronousProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
