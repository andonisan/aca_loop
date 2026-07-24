namespace Orchestrator.BuildingBlocks.CQS;

/// <summary>A read request. <typeparamref name="TResponse"/> is expected to be <c>ErrorOr&lt;T&gt;</c>.</summary>
public interface IQuery<TResponse>;
