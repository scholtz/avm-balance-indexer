using AvmBalanceIndexer.Domain.Tokens;

namespace AvmBalanceIndexer.Domain.Storage;

public interface ITokenStore
{
    Task<TokenInfo?> GetAsync(TokenKey key, CancellationToken cancellationToken);

    Task UpsertAsync(TokenInfo token, CancellationToken cancellationToken);
}
