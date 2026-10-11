using System.Buffers;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using MessagePack;

namespace AvmBalanceIndexer.TestSupport;

/// <summary>A payment transaction to place in a synthetic block, optionally with inner payments and apply data.</summary>
public sealed record TestPayment(
    Address Sender,
    Address Receiver,
    ulong Amount,
    ulong Fee = 1000,
    Address? CloseTo = null,
    ulong ClosingAmount = 0,
    ulong SenderRewards = 0,
    ulong ReceiverRewards = 0,
    ulong CloseRewards = 0,
    IReadOnlyList<TestPayment>? Inner = null,
    TestAsset? Asset = null);

/// <summary>
/// An asset transaction placed in a synthetic block in the place of a payment: the payment's sender, receiver (the asset
/// receiver) and fee are used, the fields here describe the asset part.
/// </summary>
/// <param name="Type"><c>axfer</c> (transfer, opt-in, close, clawback) or <c>acfg</c> (create, destroy).</param>
/// <param name="AssetId">Asset moved (<c>axfer</c>) or destroyed (<c>acfg</c>, with <paramref name="Create"/> false).</param>
/// <param name="Amount">Units moved.</param>
/// <param name="AssetSender">Clawback source.</param>
/// <param name="AssetCloseTo">Close-out destination.</param>
/// <param name="AssetClosingAmount">Units moved by the close-out (apply data).</param>
/// <param name="Create">True for an asset creation; <paramref name="CreatedAssetId"/> is its id in the apply data.</param>
/// <param name="CreatedAssetId">Id the ledger assigned to a created asset (<c>caid</c> in the apply data).</param>
/// <param name="Total">Total supply of a created asset.</param>
/// <param name="Decimals">Decimals of a created asset.</param>
/// <param name="UnitName">Unit name of a created asset.</param>
/// <param name="Name">Name of a created asset.</param>
public sealed record TestAsset(
    string Type,
    ulong AssetId = 0,
    ulong Amount = 0,
    Address? AssetSender = null,
    Address? AssetCloseTo = null,
    ulong AssetClosingAmount = 0,
    bool Create = false,
    ulong CreatedAssetId = 0,
    ulong Total = 0,
    uint Decimals = 0,
    string? UnitName = null,
    string? Name = null)
{
    public static TestAsset Transfer(ulong assetId, ulong amount) => new("axfer", assetId, amount);

    public static TestAsset OptIn(ulong assetId) => new("axfer", assetId);

    public static TestAsset Clawback(ulong assetId, ulong amount, Address from) => new("axfer", assetId, amount, AssetSender: from);

    public static TestAsset CloseOut(ulong assetId, Address closeTo, ulong closingAmount, ulong amount = 0) =>
        new("axfer", assetId, amount, AssetCloseTo: closeTo, AssetClosingAmount: closingAmount);

    public static TestAsset Creation(ulong createdId, ulong total, uint decimals = 0, string? unit = null, string? name = null) =>
        new("acfg", Create: true, CreatedAssetId: createdId, Total: total, Decimals: decimals, UnitName: unit, Name: name);

    public static TestAsset Destroy(ulong assetId) => new("acfg", assetId);
}

/// <summary>
/// A synthetic chain that mirrors the wire format of real gossip blocks. Blocks are encoded as msgpack and decoded by
/// the production <see cref="BlockDecoder"/>, so tests exercise the same path as live data.
/// </summary>
public sealed class TestChain
{
    public TestChain(string chain = ChainId.AlgorandTestnet)
    {
        Profile = ChainProfile.For(chain);
        GenesisHash = Convert.FromBase64String(Profile.GenesisHash!);
    }

    /// <summary>A protocol id that is not in the pre-v31 list, like every current protocol.</summary>
    public const string DefaultProtocol = "test-protocol";

    private readonly ConcurrentDictionary<ulong, byte[]> _hashes = new();

    public ChainProfile Profile { get; }

    public byte[] GenesisHash { get; }

    public Address FeeSink { get; } = Account(250);

    public Address Proposer { get; } = Account(251);

    public Address Pool { get; } = Account(249);

    /// <summary>When false, blocks omit the rewards pool address from the header.</summary>
    public bool IncludeRewardsPool { get; init; } = true;

    /// <summary>When false, blocks omit the proposer from the header even when they pay a proposer payout.</summary>
    public bool IncludeProposer { get; init; } = true;

    /// <summary>Deterministic account used throughout tests; distinct per index.</summary>
    public static Address Account(int index)
    {
        var bytes = new byte[Address.Length];
        bytes[0] = (byte)(index & 0xFF);
        bytes[1] = (byte)((index >> 8) & 0xFF);
        bytes[^1] = 0x7F;
        return new Address(bytes);
    }

    /// <summary>A deterministic stand-in hash for tests that only need some 32 bytes, such as raw cursor writes.</summary>
    public static byte[] FakeHash(ulong round) => SHA256.HashData(Encoding.UTF8.GetBytes($"block-{round}"));

    /// <summary>Hash of the genesis block, which block 1 links back to.</summary>
    public static byte[] GenesisBlockHash { get; } = FakeHash(0);

    /// <summary>The real hash of a block built by this chain instance.</summary>
    public byte[] HashOf(ulong round) =>
        _hashes.TryGetValue(round, out var hash) ? hash : throw new InvalidOperationException($"Block {round} was not built by this chain.");

    /// <summary>Encodes and decodes a block with the given payments.</summary>
    public BlockData Block(
        ulong round,
        IEnumerable<TestPayment>? payments = null,
        ulong proposerPayout = 0,
        RewardsState? rewards = null,
        string protocol = DefaultProtocol)
    {
        var bytes = Encode(round, payments?.ToArray() ?? [], proposerPayout, rewards, protocol);
        return BlockDecoder.Decode(bytes, round, Profile, GenesisHash);
    }

    /// <summary>Raw wire bytes of a block, for decoder and fetcher tests.</summary>
    public byte[] Encode(
        ulong round,
        IReadOnlyList<TestPayment> payments,
        ulong proposerPayout = 0,
        RewardsState? rewards = null,
        string protocol = DefaultProtocol)
    {
        var previous = round == 1 ? GenesisBlockHash : _hashes.GetValueOrDefault(round - 1) ?? FakeHash(round - 1);

        // The certificate digest must be the real header hash, which does not depend on the certificate itself.
        // The header commits to the payset, which in turn is only known from the encoded transactions.
        var draft = Write(round, payments, proposerPayout, rewards, protocol, previous, new byte[32], new byte[32]);
        var txn = PaysetCommitment.ComputeTxn(draft, ApplyDataReader.ReadBlockTransactions(draft), GenesisHash, Profile.GenesisId);
        var hash = BlockHashCalculator.Compute(Write(round, payments, proposerPayout, rewards, protocol, previous, new byte[32], txn));
        _hashes[round] = hash;
        return Write(round, payments, proposerPayout, rewards, protocol, previous, hash, txn);
    }

    private byte[] Write(
        ulong round,
        IReadOnlyList<TestPayment> payments,
        ulong proposerPayout,
        RewardsState? rewards,
        string protocol,
        byte[] previous,
        byte[] digest,
        byte[] txn)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(2);

        writer.Write("block");
        writer.WriteMapHeader(9 + (IncludeRewardsPool ? 1 : 0) + (proposerPayout > 0 ? 1 + (IncludeProposer ? 1 : 0) : 0) + (rewards is not null ? 3 : 0));
        writer.Write("fees");
        writer.Write(FeeSink.ToArray());
        writer.Write("gen");
        writer.Write(Profile.GenesisId);
        writer.Write("gh");
        writer.Write(GenesisHash);
        writer.Write("prev");
        writer.Write(previous);
        writer.Write("proto");
        writer.Write(protocol);
        writer.Write("rnd");
        writer.Write(round);
        if (IncludeRewardsPool)
        {
            writer.Write("rwd");
            writer.Write(Pool.ToArray());
        }

        writer.Write("ts");
        writer.Write(1_560_000_000UL + round);
        writer.Write("txn");
        writer.Write(txn);
        writer.Write("txns");
        writer.WriteArrayHeader(payments.Count);
        foreach (var payment in payments)
        {
            WriteTransaction(ref writer, payment, topLevel: true);
        }

        if (rewards is { } state)
        {
            writer.Write("earn");
            writer.Write(state.Level);
            writer.Write("frac");
            writer.Write(state.Residue);
            writer.Write("rate");
            writer.Write(state.Rate);
        }

        if (proposerPayout > 0)
        {
            writer.Write("pp");
            writer.Write(proposerPayout);
            if (IncludeProposer)
            {
                writer.Write("prp");
                writer.Write(Proposer.ToArray());
            }
        }

        writer.Write("cert");
        writer.WriteMapHeader(2);
        writer.Write("prop");
        writer.WriteMapHeader(1);
        writer.Write("dig");
        writer.Write(digest);
        writer.Write("rnd");
        writer.Write(round);

        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteTransaction(ref MessagePackWriter writer, TestPayment payment, bool topLevel)
    {
        var entries = 1;
        entries += payment.SenderRewards > 0 ? 1 : 0;
        entries += payment.ReceiverRewards > 0 ? 1 : 0;
        entries += payment.CloseRewards > 0 ? 1 : 0;
        entries += payment.ClosingAmount > 0 ? 1 : 0;
        entries += payment.Asset is { AssetClosingAmount: > 0 } ? 1 : 0;
        entries += payment.Asset is { CreatedAssetId: > 0 } ? 1 : 0;
        entries += topLevel ? 2 : 0;
        entries += payment.Inner is { Count: > 0 } ? 1 : 0;
        writer.WriteMapHeader(entries);

        if (payment.Asset is { AssetClosingAmount: > 0 } asset)
        {
            writer.Write("aca");
            writer.Write(asset.AssetClosingAmount);
        }

        if (payment.ClosingAmount > 0)
        {
            writer.Write("ca");
            writer.Write(payment.ClosingAmount);
        }

        if (payment.Asset is { CreatedAssetId: > 0 } created)
        {
            writer.Write("caid");
            writer.Write(created.CreatedAssetId);
        }

        if (payment.Inner is { Count: > 0 } inner)
        {
            writer.Write("dt");
            writer.WriteMapHeader(1);
            writer.Write("itx");
            writer.WriteArrayHeader(inner.Count);
            foreach (var child in inner)
            {
                WriteTransaction(ref writer, child, topLevel: false);
            }
        }

        if (topLevel)
        {
            writer.Write("hgh");
            writer.Write(true);
            writer.Write("hgi");
            writer.Write(true);
        }

        if (payment.CloseRewards > 0)
        {
            writer.Write("rc");
            writer.Write(payment.CloseRewards);
        }

        if (payment.ReceiverRewards > 0)
        {
            writer.Write("rr");
            writer.Write(payment.ReceiverRewards);
        }

        if (payment.SenderRewards > 0)
        {
            writer.Write("rs");
            writer.Write(payment.SenderRewards);
        }

        writer.Write("txn");
        WritePaymentBody(ref writer, payment);
    }

    private delegate void WriteValue(ref MessagePackWriter writer);

    private static void WriteAssetBody(ref MessagePackWriter writer, TestPayment payment, TestAsset asset)
    {
        var entries = new List<(string Key, WriteValue Write)>
        {
            ("fv", static (ref MessagePackWriter w) => w.Write(1UL)),
            ("lv", static (ref MessagePackWriter w) => w.Write(1001UL)),
            ("snd", (ref MessagePackWriter w) => w.Write(payment.Sender.ToArray())),
            ("type", (ref MessagePackWriter w) => w.Write(asset.Type)),
        };
        if (payment.Fee > 0)
        {
            entries.Add(("fee", (ref MessagePackWriter w) => w.Write(payment.Fee)));
        }

        if (asset.Type == "axfer")
        {
            entries.Add(("xaid", (ref MessagePackWriter w) => w.Write(asset.AssetId)));
            entries.Add(("arcv", (ref MessagePackWriter w) => w.Write(payment.Receiver.ToArray())));
            if (asset.Amount > 0)
            {
                entries.Add(("aamt", (ref MessagePackWriter w) => w.Write(asset.Amount)));
            }

            if (asset.AssetSender is { } from)
            {
                entries.Add(("asnd", (ref MessagePackWriter w) => w.Write(from.ToArray())));
            }

            if (asset.AssetCloseTo is { } closeTo)
            {
                entries.Add(("aclose", (ref MessagePackWriter w) => w.Write(closeTo.ToArray())));
            }
        }
        else if (asset.Create)
        {
            // A real transaction omits apar when every parameter is zero.
            if (asset.Total > 0 || asset.Decimals > 0 || asset.UnitName is not null || asset.Name is not null)
            {
            entries.Add(("apar", (ref MessagePackWriter w) =>
            {
                var fields = 1 + (asset.Decimals > 0 ? 1 : 0) + (asset.UnitName is not null ? 1 : 0) + (asset.Name is not null ? 1 : 0);
                w.WriteMapHeader(fields);
                if (asset.Name is not null)
                {
                    w.Write("an");
                    w.Write(asset.Name);
                }

                if (asset.Decimals > 0)
                {
                    w.Write("dc");
                    w.Write(asset.Decimals);
                }

                w.Write("t");
                w.Write(asset.Total);
                if (asset.UnitName is not null)
                {
                    w.Write("un");
                    w.Write(asset.UnitName);
                }
            }));
            }
        }
        else
        {
            entries.Add(("caid", (ref MessagePackWriter w) => w.Write(asset.AssetId)));
        }

        writer.WriteMapHeader(entries.Count);
        foreach (var (key, write) in entries.OrderBy(static e => e.Key, StringComparer.Ordinal))
        {
            writer.Write(key);
            write(ref writer);
        }
    }

    private static void WritePaymentBody(ref MessagePackWriter writer, TestPayment payment)
    {
        if (payment.Asset is { } asset)
        {
            WriteAssetBody(ref writer, payment, asset);
            return;
        }

        var omitReceiver = payment.Receiver.Bytes.ToArray().All(static b => b == 0);
        var entries = omitReceiver ? 4 : 5;
        entries += payment.Amount > 0 ? 1 : 0;
        entries += payment.Fee > 0 ? 1 : 0;
        entries += payment.CloseTo is not null ? 1 : 0;
        writer.WriteMapHeader(entries);

        if (payment.Amount > 0)
        {
            writer.Write("amt");
            writer.Write(payment.Amount);
        }

        if (payment.CloseTo is { } closeTo)
        {
            writer.Write("close");
            writer.Write(closeTo.ToArray());
        }

        if (payment.Fee > 0)
        {
            writer.Write("fee");
            writer.Write(payment.Fee);
        }

        writer.Write("fv");
        writer.Write(1UL);
        writer.Write("lv");
        writer.Write(1001UL);
        if (!omitReceiver)
        {
            writer.Write("rcv");
            writer.Write(payment.Receiver.ToArray());
        }

        writer.Write("snd");
        writer.Write(payment.Sender.ToArray());
        writer.Write("type");
        writer.Write("pay");
    }
}
