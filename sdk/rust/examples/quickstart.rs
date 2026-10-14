//! Run with: cargo run --example quickstart
use avm_balance_indexer::{AvmBalanceIndexer, ChainId, HoldersOptions, TokenKind};
use futures_util::{StreamExt, TryStreamExt};

#[tokio::main]
async fn main() -> Result<(), avm_balance_indexer::Error> {
    // Public TestNet deployment. Use ChainId::Voi / ChainId::AlgorandMainnet, or .base_url(..) on the builder for your own deployment.
    let indexer = AvmBalanceIndexer::new(ChainId::AlgorandTestnet)?;

    let status = indexer.status().await?;
    println!("{}: {}, indexed round {:?} of {:?}", status.chain, status.state, status.indexed_round, status.target_round);

    // The five biggest holders of the native token at the latest round (amounts are decimal strings).
    let options = HoldersOptions { limit: Some(100), ..Default::default() };
    let holders: Vec<_> = indexer.holders_stream(TokenKind::Algo, 0, options).take(5).try_collect().await?;
    for holder in holders {
        println!("{} {}", holder.account, holder.amount);
    }

    match indexer.token(TokenKind::Asa, 1).await {
        Err(error) if error.is_not_found() => println!("asset 1 is not known to this chain"),
        other => {
            other?;
        }
    }
    Ok(())
}
