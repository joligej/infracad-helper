using System.Reflection;
using NlcsLegenda.Core;
using Xunit;

namespace NlcsLegenda.Core.Tests;

public sealed class FailureHandlingTests
{
    [Fact]
    public void DiagnosticReportsUnexpectedFailureWithStackAndCanRunNextTest()
    {
        var messages = new List<string>();
        Assert.False(DiagnosticRun.Execute(() => throw new NullReferenceException("sentinel"), messages.Add, "proef"));
        string failure = Assert.Single(messages);
        Assert.Contains("FAIL: proef", failure);
        Assert.Contains("NullReferenceException: sentinel", failure);
        Assert.Contains(nameof(DiagnosticReportsUnexpectedFailureWithStackAndCanRunNextTest), failure);
        bool ran = false;
        Assert.True(DiagnosticRun.Execute(() => ran = true, messages.Add));
        Assert.True(ran);
        Assert.Single(messages);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FatalErrorsAreNeverReportedAsOrdinaryTestFailures(int wrapper)
    {
        Exception error = new OutOfMemoryException("simulatie");
        error = wrapper switch
        {
            1 => new TargetInvocationException(error),
            2 => new InvalidOperationException("context", error),
            3 => new AggregateException(new IOException("opruimen"), error),
            _ => error
        };
        var messages = new List<string>();
        Assert.Same(error, Record.Exception(() => DiagnosticRun.Execute(() => throw error, messages.Add)));
        Assert.Empty(messages);
    }

    [Fact]
    public void ReportingFailureIsNotSwallowed()
    {
        var reporting = new IOException("rapportage");
        Assert.Same(reporting, Record.Exception(() => DiagnosticRun.Execute(
            () => throw new InvalidOperationException("proef"), _ => throw reporting)));
    }

    [Fact]
    public void CleanupAttemptsEveryResourceOnceInReverseOrder()
    {
        var order = new List<int>();
        var first = new IOException("eerste");
        var second = new InvalidOperationException("tweede");
        using var scope = new ResourceScope();
        scope.Defer(() => { order.Add(1); throw first; });
        scope.Defer(() => order.Add(2));
        scope.Defer(() => { order.Add(3); throw second; });
        var error = Assert.Throws<AggregateException>(scope.Dispose);
        Assert.Equal(new[] { 3, 2, 1 }, order);
        Assert.Equal(new Exception[] { second, first }, error.InnerExceptions);
        scope.Dispose();
        Assert.Equal(3, order.Count);
        Assert.Throws<ObjectDisposedException>(() => scope.Defer(() => { }));
    }

    [Fact]
    public void SingleCleanupFailureRetainsIdentityAndStack()
    {
        var error = new IOException("opruimen");
        using var scope = new ResourceScope();
        scope.Defer(() => throw error);
        Assert.Same(error, Assert.Throws<IOException>(scope.Dispose));
        Assert.Contains(nameof(SingleCleanupFailureRetainsIdentityAndStack), error.StackTrace);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedReplacementAndCleanupKeepOriginalFailureAndAttemptAllCleanup(bool restoreFails)
    {
        var files = new SimulatedFiles();
        files.Bytes["a"] = new byte[] { 1, 2 };
        files.Bytes["b"] = new byte[] { 3, 4 };
        var original = new IOException("vervanging");
        var restore = new UnauthorizedAccessException("herstel");
        var cleanup = new IOException("opruimen");
        int moves = 0, deletes = 0;
        files.BeforeMove = (_, target) =>
        {
            moves++;
            if (target == "b") throw original;
            if (moves == 3 && restoreFails) throw restore;
        };
        files.BeforeDelete = _ => { if (++deletes == 1) throw cleanup; };
        var error = Assert.Throws<AggregateException>(() => AtomicFiles.Write(new Dictionary<string, string> { ["a"] = "nieuw a", ["b"] = "nieuw b" }, files));
        Assert.Same(original, error.InnerExceptions[0]);
        Assert.Contains(cleanup, error.InnerExceptions);
        Assert.Equal(restoreFails ? 3 : 2, error.InnerExceptions.Count);
        if (restoreFails) Assert.Contains(restore, error.InnerExceptions);
        else Assert.Equal(new byte[] { 1, 2 }, files.Bytes["a"]);
        Assert.Equal(new byte[] { 3, 4 }, files.Bytes["b"]);
        Assert.Equal(3, deletes);
    }

    [Fact]
    public void StagingFailureDoesNotReplaceAnyFileAndRemovesPartialTemporaryFile()
    {
        var files = new SimulatedFiles();
        files.Bytes["a"] = new byte[] { 1 };
        var error = new IOException("schijf vol");
        files.AfterWrite = path => { if (path.StartsWith("b.")) throw error; };
        Assert.Same(error, Assert.Throws<IOException>(() => AtomicFiles.Write(new Dictionary<string, string> { ["a"] = "nieuw", ["b"] = "nieuw" }, files)));
        Assert.Equal(new byte[] { 1 }, files.Bytes["a"]);
        Assert.Single(files.Bytes);
    }

    private sealed class SimulatedFiles : FileOperations
    {
        public Dictionary<string, byte[]> Bytes { get; } = new();
        public Action<string, string>? BeforeMove { get; set; }
        public Action<string>? BeforeDelete { get; set; }
        public Action<string>? AfterWrite { get; set; }
        public override byte[]? Read(string path) => Bytes.GetValueOrDefault(path);
        public override void Write(string path, byte[] bytes) { Bytes[path] = bytes; AfterWrite?.Invoke(path); }
        public override void Move(string source, string target)
        { BeforeMove?.Invoke(source, target); Bytes[target] = Bytes[source]; Bytes.Remove(source); }
        public override void Delete(string path) { BeforeDelete?.Invoke(path); Bytes.Remove(path); }
    }
}
