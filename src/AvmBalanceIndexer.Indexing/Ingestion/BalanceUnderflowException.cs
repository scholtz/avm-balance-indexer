using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Indexing.Ingestion;

/// <summary>
/// A native or ASA delta would make a balance negative. The chain cannot do that, so the indexed state is missing a credit
/// (or a debit was counted twice); retrying cannot fix it. The message carries everything needed to find the cause.
/// </summary>
public sealed class BalanceUnderflowException : InvalidOperationException
{
    public BalanceUnderflowException(BalanceDelta delta, Amount balance)
        : base(Describe(delta, balance))
    {
        Delta = delta;
        Balance = balance;
    }

    /// <summary>The delta that could not be applied.</summary>
    public BalanceDelta Delta { get; }

    /// <summary>The balance the indexer had for the account and token before the delta.</summary>
    public Amount Balance { get; }

    private static string Describe(BalanceDelta delta, Amount balance)
    {
        var id = delta.Id;
        var where = id.TransactionId.Length == 0 ? "a block-level effect" : $"transaction {id.TransactionId}";
        var counterparty = delta.Counterparty is { } other ? $", counterparty {Text(other)}" : string.Empty;
        var inner = id.InnerPath.Count == 0 ? string.Empty : $", inner path {string.Join('.', id.InnerPath)}";
        return $"Balance underflow in round {id.Round}: {where}{inner} (effect {id.Ordinal}, {delta.Type}) applies {delta.Delta} of {delta.Token} " +
            $"to account {Text(delta.Account)}{counterparty}, which the indexer holds at {balance.Value}. The indexed state is missing {-delta.Delta - balance.Value} " +
            "unit(s), typically a credit that was not indexed earlier: compare the account's history for this token with the chain.";
    }

    private static string Text(Address address) => new Algorand.Address(address.ToArray()).EncodeAsString();
}
