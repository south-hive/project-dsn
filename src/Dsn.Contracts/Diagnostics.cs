namespace Dsn.Contracts;

public sealed record ErrorBody(string Code, string Component, string? SourceId = null, string? Workspace = null, string? Detail = null);
public sealed record ErrorAggregate(ErrorBody Body, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, long Count);
public interface IErrorSink { void Report(ErrorBody body); }
