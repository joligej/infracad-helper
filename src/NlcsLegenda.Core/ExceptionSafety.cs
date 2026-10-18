using System.Reflection;

namespace NlcsLegenda.Core;

public static class ExceptionSafety
{
    public static bool IsFatal(Exception error) => error switch
    {
        OutOfMemoryException or StackOverflowException or AccessViolationException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsFatal),
        TargetInvocationException { InnerException: { } inner } => IsFatal(inner),
        TypeInitializationException { InnerException: { } inner } => IsFatal(inner),
        _ => false
    };
}
