using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using AvmBalanceIndexer.Domain.Chains;
using AvmBalanceIndexer.Indexing.Adapters;
using AvmBalanceIndexer.Indexing.Normalization;
using MessagePack;

namespace AvmBalanceIndexer.TestSupport;

/// <summary>
/// A payment transaction to place in a synthetic block, optionally with inner payments and apply data. <c>RawEntry</c> places a
/// stored block transaction (a <c>txns</c> element) verbatim instead.
/// </summary>
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
    TestAsset? Asset = null,
    TestApplication? Application = null,
    byte[]? RawEntry = null);

/// <summary>An application call placed in a synthetic block in the place of a payment (the payment's sender and fee are used).</summary>
/// <param name="ApplicationId">Application called; zero for a creation.</param>
/// <param name="Logs">Logs the call emitted.</param>
/// <param name="CreatedApplicationId">Id the ledger assigned to a created application (<c>apid</c> in the apply data).</param>
public sealed record TestApplication(ulong ApplicationId, IReadOnlyList<byte[]>? Logs = null, ulong CreatedApplicationId = 0);

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
/// <param name="DefaultFrozen">Holdings of a created asset start frozen.</param>
/// <param name="Manager">Manager address of a created or reconfigured asset.</param>
/// <param name="ClawbackAddress">Clawback address of a created or reconfigured asset.</param>
/// <param name="Reconfigure">True for a reconfiguration of <paramref name="AssetId"/> (manager and clawback only).</param>
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
    string? Name = null,
    bool DefaultFrozen = false,
    Address? Manager = null,
    Address? ClawbackAddress = null,
    bool Reconfigure = false)
{
    /// <summary>Changes the manager and clawback of an existing asset.</summary>
    public static TestAsset Reconfiguration(ulong assetId, Address? manager, Address? clawback) =>
        new("acfg", assetId, Reconfigure: true, Manager: manager, ClawbackAddress: clawback);

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

    /// <summary>
    /// When set, blocks carry the transaction counter <c>tc</c> (this value plus the block's transactions), which is how early
    /// protocols identify created assets.
    /// </summary>
    public ulong? TxnCounterBefore { get; init; }

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
        var stored = ApplyDataReader.ReadBlockTransactions(draft);
        if (ConsensusProtocols.RequiresGenesisHash(protocol))
        {
            // Like the decoder: from v16 on the genesis hash is restored on every transaction whatever its flags say.
            stored = [.. stored.Select(static a => a with { HasGenesisHash = true })];
        }

        var txn = PaysetCommitment.ComputeTxn(draft, stored, GenesisHash, Profile.GenesisId);
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
        writer.WriteMapHeader(9 + (TxnCounterBefore is not null ? 1 : 0) + (IncludeRewardsPool ? 1 : 0) + (proposerPayout > 0 ? 1 + (IncludeProposer ? 1 : 0) : 0) + (rewards is not null ? 3 : 0));
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

        if (TxnCounterBefore is { } before)
        {
            writer.Write("tc");
            writer.Write(before + (ulong)payments.Count);
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
        if (payment.RawEntry is { } raw)
        {
            // A transaction taken from a real block, written as it was stored.
            writer.WriteRaw(raw);
            return;
        }

        var entries = 1;
        entries += payment.SenderRewards > 0 ? 1 : 0;
        entries += payment.ReceiverRewards > 0 ? 1 : 0;
        entries += payment.CloseRewards > 0 ? 1 : 0;
        entries += payment.ClosingAmount > 0 ? 1 : 0;
        entries += payment.Asset is { AssetClosingAmount: > 0 } ? 1 : 0;
        entries += payment.Asset is { CreatedAssetId: > 0 } ? 1 : 0;
        entries += topLevel ? 2 : 0;
        entries += payment.Inner is { Count: > 0 } || payment.Application is { Logs.Count: > 0 } ? 1 : 0;
        entries += payment.Application is { CreatedApplicationId: > 0 } ? 1 : 0;
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

        if (payment.Application is { CreatedApplicationId: > 0 } createdApp)
        {
            writer.Write("apid");
            writer.Write(createdApp.CreatedApplicationId);
        }

        var logs = payment.Application?.Logs;
        if (payment.Inner is { Count: > 0 } || logs is { Count: > 0 })
        {
            writer.Write("dt");
            writer.WriteMapHeader((payment.Inner is { Count: > 0 } ? 1 : 0) + (logs is { Count: > 0 } ? 1 : 0));
            if (payment.Inner is { Count: > 0 } inner)
            {
                writer.Write("itx");
                writer.WriteArrayHeader(inner.Count);
                foreach (var child in inner)
                {
                    WriteTransaction(ref writer, child, topLevel: false);
                }
            }

            if (logs is { Count: > 0 })
            {
                writer.Write("lg");
                writer.WriteArrayHeader(logs.Count);
                foreach (var log in logs)
                {
                    writer.Write(log);
                }
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
        else
        {
            if (!asset.Create)
            {
                entries.Add(("caid", (ref MessagePackWriter w) => w.Write(asset.AssetId)));
            }

            // A real transaction omits apar when every parameter is zero, and for a destroy.
            var hasParameters = asset.Reconfigure
                || (asset.Create && (asset.Total > 0 || asset.Decimals > 0 || asset.UnitName is not null || asset.Name is not null || asset.DefaultFrozen || asset.Manager is not null || asset.ClawbackAddress is not null));
            if (hasParameters)
            {
                entries.Add(("apar", (ref MessagePackWriter w) => WriteAssetParameters(ref w, asset)));
            }
        }

        writer.WriteMapHeader(entries.Count);
        foreach (var (key, write) in entries.OrderBy(static e => e.Key, StringComparer.Ordinal))
        {
            writer.Write(key);
            write(ref writer);
        }
    }

    private static void WriteAssetParameters(ref MessagePackWriter w, TestAsset asset)
    {
        var fields = new List<(string Key, WriteValue Write)>();
        if (asset.Create)
        {
            fields.Add(("t", (ref MessagePackWriter x) => x.Write(asset.Total)));
            if (asset.Decimals > 0)
            {
                fields.Add(("dc", (ref MessagePackWriter x) => x.Write(asset.Decimals)));
            }

            if (asset.UnitName is not null)
            {
                fields.Add(("un", (ref MessagePackWriter x) => x.Write(asset.UnitName)));
            }

            if (asset.Name is not null)
            {
                fields.Add(("an", (ref MessagePackWriter x) => x.Write(asset.Name)));
            }

            if (asset.DefaultFrozen)
            {
                fields.Add(("df", (ref MessagePackWriter x) => x.Write(true)));
            }
        }

        if (asset.Manager is { } manager)
        {
            fields.Add(("m", (ref MessagePackWriter x) => x.Write(manager.ToArray())));
        }

        if (asset.ClawbackAddress is { } clawback)
        {
            fields.Add(("c", (ref MessagePackWriter x) => x.Write(clawback.ToArray())));
        }

        w.WriteMapHeader(fields.Count);
        foreach (var (key, write) in fields.OrderBy(static f => f.Key, StringComparer.Ordinal))
        {
            w.Write(key);
            write(ref w);
        }
    }

    private static void WriteApplicationBody(ref MessagePackWriter writer, TestPayment payment, TestApplication application)
    {
        var entries = 4 + (payment.Fee > 0 ? 1 : 0) + (application.ApplicationId > 0 ? 1 : 0);
        writer.WriteMapHeader(entries);
        if (application.ApplicationId > 0)
        {
            writer.Write("apid");
            writer.Write(application.ApplicationId);
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
        writer.Write("snd");
        writer.Write(payment.Sender.ToArray());
        writer.Write("type");
        writer.Write("appl");
    }

    /// <summary>An ARC-200 transfer log: selector, sender, receiver and the amount as a big-endian uint256.</summary>
    public static byte[] Arc200TransferLog(Address from, Address to, BigInteger amount)
    {
        var log = new byte[100];
        Arc200Events.TransferSelector.CopyTo(log);
        from.ToArray().CopyTo(log, 4);
        to.ToArray().CopyTo(log, 36);
        var bytes = amount.ToByteArray(isUnsigned: true, isBigEndian: true);
        bytes.CopyTo(log, 100 - bytes.Length);
        return log;
    }

    private static void WritePaymentBody(ref MessagePackWriter writer, TestPayment payment)
    {
        if (payment.Application is { } application)
        {
            WriteApplicationBody(ref writer, payment, application);
            return;
        }

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
