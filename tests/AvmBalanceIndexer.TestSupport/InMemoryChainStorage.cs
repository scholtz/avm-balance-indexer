using System.Globalization;
using System.Numerics;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Domain.Effects;
using AvmBalanceIndexer.Domain.Storage;
using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.TestSupport;

/// <summary>
/// Reference implementation of the storage contract backed by plain collections. It defines the expected behaviour
/// that every real provider is compared against in the contract suite, and keeps indexer tests hermetic.
/// </summary>
public class InMemoryChainStorage(ChainContext context) : IChainStorage
{
    private readonly object _gate = new();
    private readonly Dictionary<HoldingKey, List<TokenEffect>> _effects = [];
    private readonly Dictionary<HoldingKey, Holding> _holdings = [];
    private readonly Dictionary<TokenKey, TokenInfo> _tokens = [];
    private ChainStatus? _status;

    /// <summary>Total number of stored effect rows.</summary>
    public int EffectCount
    {
        get
        {
            lock (_gate)
            {
                return _effects.Values.Sum(static l => l.Count);
            }
        }
    }

    public virtual Task EnsureSchemaAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<ChainStatus?> GetAsync(string chain, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_status is not null && _status.Chain == chain ? _status : null);
        }
    }

    public Task UpdateProgressAsync(ChainStatus status, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_status is not null && _status.IndexedRound == status.IndexedRound)
            {
                _status = status;
            }
        }

        return Task.CompletedTask;
    }

    public Task<TokenInfo?> GetAsync(TokenKey key, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(_tokens.GetValueOrDefault(key));
        }
    }

    public Task UpsertAsync(TokenInfo token, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _tokens[token.Key] = token;
        }

        return Task.CompletedTask;
    }

    public Task<bool> WriteRoundAsync(ChainStatus roundStatus, IReadOnlyList<TokenEffect> effects, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(roundStatus);
        ArgumentNullException.ThrowIfNull(effects);
        if (roundStatus.Chain != context.Chain)
        {
            throw new ArgumentException("Wrong chain.", nameof(roundStatus));
        }

        var round = roundStatus.IndexedRound;
        if (effects.Any(e => e.Id.Round != round))
        {
            throw new ArgumentException($"All effects must belong to round {round}.", nameof(effects));
        }

        lock (_gate)
        {
            if (_status is null)
            {
                if (round != 0)
                {
                    throw new ChainDivergenceException($"The first round written must be 0 but round {round} was given.");
                }
            }
            else if (round == _status.IndexedRound)
            {
                if (!roundStatus.IndexedBlockHash.Span.SequenceEqual(_status.IndexedBlockHash.Span))
                {
                    throw new ChainDivergenceException($"Round {round} is already published with a different block hash.");
                }

                return Task.FromResult(false);
            }
            else if (round < _status.IndexedRound)
            {
                throw new ChainDivergenceException(
                    $"Round {round} is older than the published round {_status.IndexedRound}; its block hash can no longer be verified, so it is refused.");
            }
            else if (round != _status.IndexedRound + 1)
            {
                throw new ChainDivergenceException($"Round {round} would leave a gap after published round {_status.IndexedRound}.");
            }

            var seen = new HashSet<(HoldingKey, int, int)>();
            foreach (var effect in effects)
            {
                if (!seen.Add((new HoldingKey(effect.Account, effect.Token), effect.Id.IntraRoundOrder, effect.Id.Ordinal)))
                {
                    throw new InvalidOperationException($"Round {round} contains two effects with the same identity (ordinal {effect.Id.Ordinal}).");
                }
            }

            foreach (var effect in effects)
            {
                var key = new HoldingKey(effect.Account, effect.Token);
                if (!_effects.TryGetValue(key, out var list))
                {
                    _effects[key] = list = [];
                }

                list.Add(effect);
            }

            foreach (var group in effects.GroupBy(static e => new HoldingKey(e.Account, e.Token)))
            {
                var last = group.MaxBy(static e => e.Id.Ordinal)!;
                _holdings[group.Key] = new Holding(group.Key.Account, group.Key.Token, last.BalanceAfter, last.Type != EffectType.AssetOptOut, round);
            }

            _status = roundStatus;
        }

        return Task.FromResult(true);
    }

    public Task<IReadOnlyDictionary<HoldingKey, Holding>> GetCurrentHoldingsAsync(IReadOnlyCollection<HoldingKey> keys, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyDictionary<HoldingKey, Holding> result = keys
                .Where(_holdings.ContainsKey)
                .Distinct()
                .ToDictionary(static k => k, k => _holdings[k]);
            return Task.FromResult(result);
        }
    }

    public Task<Page<Holding>> GetHoldingsAsync(Address account, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var after = ParseHoldingCursor(cursor);
            var indexed = IndexedRound();
            var rows = _holdings.Values
                .Where(h => h.Account == account && (includeZero || !h.Amount.IsZero))
                .OrderBy(static h => h.Token.Kind.ToStorageName(), StringComparer.Ordinal)
                .ThenBy(static h => h.Token.TokenId)
                .Where(h => after is not { } key || CompareHolding(h, key) > 0)
                .Take(Clamp(limit) + 1)
                .ToList();
            return Task.FromResult(ToPage(rows, Clamp(limit), indexed, static h => $"{h.Token.Kind.ToStorageName()}:{h.Token.TokenId.ToString(CultureInfo.InvariantCulture)}"));
        }
    }

    public Task<Amount> GetBalanceAtRoundAsync(Address account, TokenKey token, ulong round, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var indexed = IndexedRound();
            if (round > indexed)
            {
                throw new RoundNotIndexedException(round, indexed);
            }

            var last = _effects.GetValueOrDefault(new HoldingKey(account, token))?
                .Where(e => e.Id.Round <= round)
                .MaxBy(static e => (e.Id.Round, e.Id.IntraRoundOrder, e.Id.Ordinal));
            return Task.FromResult(last?.BalanceAfter ?? Amount.Zero);
        }
    }

    public Task<Page<Holding>> GetHoldersAtRoundAsync(TokenKey token, ulong round, bool includeZero, string? cursor, int limit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var indexed = IndexedRound();
            if (round > indexed)
            {
                throw new RoundNotIndexedException(round, indexed);
            }

            var after = ParseHolderCursor(cursor);
            var rows = _effects
                .Where(kv => kv.Key.Token == token)
                .Select(kv => (kv.Key.Account, Effects: kv.Value.Where(e => e.Id.Round <= round).ToList()))
                .Where(static x => x.Effects.Count > 0)
                .Select(static x => new Holding(
                    x.Account,
                    x.Effects[0].Token,
                    new Amount(x.Effects.Aggregate(BigInteger.Zero, static (sum, e) => sum + e.Delta)),
                    true,
                    x.Effects.Max(static e => e.Id.Round)))
                .Where(h => includeZero || !h.Amount.IsZero)
                .Where(h => h.Account.Bytes.SequenceCompareTo(after) > 0)
                .OrderBy(static h => h.Account.ToArray(), ByteArrayComparer.Instance)
                .Take(Clamp(limit) + 1)
                .ToList();
            return Task.FromResult(ToPage(rows, Clamp(limit), indexed, static h => Convert.ToHexString(h.Account.Bytes)));
        }
    }

    public Task<Page<TokenEffect>> GetEffectsAsync(Address account, TokenKey token, ulong? fromRound, ulong? toRound, string? cursor, int limit, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var before = ParseEffectCursor(cursor);
            var indexed = IndexedRound();
            var upper = Math.Min(toRound ?? indexed, indexed);
            var rows = (_effects.GetValueOrDefault(new HoldingKey(account, token)) ?? [])
                .Where(e => e.Id.Round >= (fromRound ?? 0) && e.Id.Round <= upper)
                .OrderByDescending(static e => (e.Id.Round, e.Id.IntraRoundOrder, e.Id.Ordinal))
                .Where(e => before is not { } key || CompareEffect(e, key) < 0)
                .Take(Clamp(limit) + 1)
                .ToList();
            return Task.FromResult(ToPage(
                rows,
                Clamp(limit),
                indexed,
                static e => string.Create(CultureInfo.InvariantCulture, $"{e.Id.Round}:{e.Id.IntraRoundOrder}:{e.Id.Ordinal}")));
        }
    }

    private ulong IndexedRound() => _status?.IndexedRound ?? throw new ChainNotStartedException(context.Chain);

    private static byte[] ParseHolderCursor(string? cursor)
    {
        if (cursor is null)
        {
            return [];
        }

        try
        {
            return Convert.FromHexString(cursor);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException("Invalid cursor.", nameof(cursor), ex);
        }
    }

    private static int Clamp(int limit) => limit < 1 ? 1 : Math.Min(limit, 1000);

    private static (string Kind, ulong Id)? ParseHoldingCursor(string? cursor)
    {
        if (cursor is null)
        {
            return null;
        }

        var parts = cursor.Split(':');
        return parts.Length == 2 && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id <= long.MaxValue
            ? (parts[0], id)
            : throw new ArgumentException("Invalid cursor.", nameof(cursor));
    }

    private static (ulong Round, int Order, int Ordinal)? ParseEffectCursor(string? cursor)
    {
        if (cursor is null)
        {
            return null;
        }

        var parts = cursor.Split(':');
        return parts.Length == 3
            && ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var round) && round <= long.MaxValue
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var order)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal)
            ? (round, order, ordinal)
            : throw new ArgumentException("Invalid cursor.", nameof(cursor));
    }

    private static int CompareHolding(Holding holding, (string Kind, ulong Id) cursor)
    {
        var kindOrder = string.CompareOrdinal(holding.Token.Kind.ToStorageName(), cursor.Kind);
        return kindOrder != 0 ? kindOrder : holding.Token.TokenId.CompareTo(cursor.Id);
    }

    private static int CompareEffect(TokenEffect effect, (ulong Round, int Order, int Ordinal) cursor) =>
        (effect.Id.Round, effect.Id.IntraRoundOrder, effect.Id.Ordinal).CompareTo(cursor);

    private static Page<T> ToPage<T>(List<T> fetched, int pageSize, ulong indexed, Func<T, string> cursorOf)
    {
        if (fetched.Count <= pageSize)
        {
            return new Page<T>(fetched, null, indexed);
        }

        fetched.RemoveAt(fetched.Count - 1);
        return new Page<T>(fetched, cursorOf(fetched[^1]), indexed);
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
