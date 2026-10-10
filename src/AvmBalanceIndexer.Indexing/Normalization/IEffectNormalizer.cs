using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Indexing.Adapters;

namespace AvmBalanceIndexer.Indexing.Normalization;

/// <summary>Derives deterministic <see cref="TokenEffect"/>s from a block, including inner transactions and ARC-200 logs.</summary>
public interface IEffectNormalizer
{
    IReadOnlyList<TokenEffect> Normalize(RawBlock block);
}
