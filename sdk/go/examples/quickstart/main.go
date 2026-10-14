// Command quickstart prints the status of the public TestNet deployment and its first holders of the native token.
package main

import (
	"context"
	"errors"
	"fmt"
	"log"

	avm "github.com/scholtz/avm-balance-indexer/sdk/go"
)

func main() {
	ctx := context.Background()

	// Public TestNet deployment. Use avm.Voi / avm.AlgorandMainnet, or &avm.Options{BaseURL: "..."} for your own deployment.
	indexer, err := avm.New(avm.AlgorandTestnet, nil)
	if err != nil {
		log.Fatal(err)
	}

	status, err := indexer.Status(ctx)
	if err != nil {
		log.Fatal(err)
	}
	fmt.Printf("%s: %s, indexed round %v of %v\n", status.Chain, status.State, deref(status.IndexedRound), deref(status.TargetRound))

	// The first five holders of the native token at the latest round (amounts are decimal strings).
	shown := 0
	stop := errors.New("enough")
	limit := int32(100)
	err = indexer.ForEachHolder(ctx, avm.Algo, 0, &avm.GetTokenHoldersParams{Limit: &limit}, func(h avm.HolderDto) error {
		fmt.Println(h.Account, h.Amount)
		if shown++; shown == 5 {
			return stop
		}
		return nil
	})
	if err != nil && !errors.Is(err, stop) {
		log.Fatal(err)
	}

	if _, err := indexer.Token(ctx, avm.Asa, 1); avm.IsNotFound(err) {
		fmt.Println("asset 1 is not known to this chain")
	}
}

func deref(value *uint64) any {
	if value == nil {
		return "unknown"
	}
	return *value
}
