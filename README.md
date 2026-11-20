# WalletGamers

A small casino platform in .NET 10: a **Wallet** service (money), a **Game** service (rounds, RNG, payouts)
and a console client. Both services are stateless, backed by PostgreSQL (one container, two databases),
and talk to each other over HTTP/JSON.

## 1. Run & play

Prerequisites: .NET 10 SDK and Docker.

```bash
docker compose up -d --build --wait          # postgres + wallet (:5001) + game (:5002)
docker compose run --rm client --player alice   # interactive console client
docker compose down                           # add -v to also drop the database volume
```

Commands: `deposit <amount>`, `withdraw <amount>`, `bet <amount>` (1 to 10), `balance`, `help`, `exit`.

```
deposit 10   -> Your deposit of $10 was successful. Your current balance is: $10
bet 5        -> Congrats - you won $8.50! Your current balance is: $13.50
withdraw 13.5 -> Your withdrawal of $13.50 was successful. Your current balance is: $0
exit         -> Thank you for playing! Hope to see you again soon.
```

**Without Docker for the services.** The APIs default to `localhost:5432`, user/password `postgres`/`postgres`
(see `appsettings.json`), and need the databases `wallet` and `game`. Migrations run at startup.

```bash
docker run -d --name pg -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres:17-alpine
docker exec pg psql -U postgres -c "CREATE DATABASE wallet" -c "CREATE DATABASE game"
dotnet run --project src/Wallet.Api      # http://localhost:5001
dotnet run --project src/Game.Api        # http://localhost:5002
dotnet run --project src/Casino.Cli -- --player alice
```

The client reads `--wallet-url` / `--game-url` (or `WALLET_URL` / `GAME_URL`), defaulting to the ports above.

**Tests:** `dotnet test` (Docker must be running: integration tests use Testcontainers/Postgres).
About 130 tests across four projects (Wallet, Game, Casino.Cli, System).

## 2. Architecture

```
 ┌──────────────┐  deposit / withdraw / balance   ┌──────────────┐
 │  Casino.Cli  │ ──────────────────────────────▶ │  Wallet.Api  │──▶ Postgres db "wallet"
 │  (console)   │                                 └──────▲───────┘
 │              │  bet                                   │ bet / win / cancel
 │              │ ──────────────▶ ┌──────────────┐       │ (idempotent, HTTP)
 └──────────────┘                 │   Game.Api   │───────┘
                                  └──────┬───────┘
                                         └──▶ Postgres db "game"
```

**Bet flow** (`transactionId = "{roundId}:bet" | ":win" | ":cancel"`, `roundId` is chosen by the client):

```
1. Game: insert round (Pending)
2. Game -> Wallet: POST /bets          422/409 => Rejected;  timeout/5xx => stay Pending, 503
3. Game: roll RNG; ONE update stores win + status=Debited      (outcome is now fixed)
4. win = 0  => Settled
   win > 0  => Game -> Wallet: POST /wins (stored amount) => Settled;  failure => stay Debited, 503
5. Recovery job (every 10s, rounds older than 30s) drives stuck rounds to a terminal state
```

**Round state machine:**

```
Pending ──debit ok──▶ Debited ──credit ok / win = 0──▶ Settled
   │                     │ credit fails: recovery retries the credit
   ├─ debit rejected ──▶ Rejected
   └─ debit unknown ──(recovery: cancel)──▶ Cancelled
 Pending/Debited ──Wallet answers 4xx during recovery──▶ NeedsAttention (terminal)
```

## 3. Key decisions

- **Wallet owns money, Game owns rounds.** Wallet keeps balances and an append-only ledger and only knows an
  optional `roundId`. Game never stores a balance; it reports what Wallet returned. The client talks to Wallet
  directly for deposits/withdrawals, so Game is not a money gateway.
- **Debit, then credit, with idempotency keys.** This is how game providers integrate with casino wallets.
  Every Wallet call has a stable transaction id, which is the ledger primary key: a replay with the same
  content returns the original result, with different content it is a 409.
- **Outcome is persisted before it is credited.** Win and status `Debited` are written in one update, and
  credits always use the stored win. A retry or crash can never re-roll, so a player cannot retry until they win.
- **Cancel tombstones.** A cancel for a bet Wallet has not seen records a tombstone, so a delayed debit that lands
  afterwards is rejected (409 `transaction_cancelled`). A partial unique index allows one cancel per original bet.
- **Recovery job + `NeedsAttention`.** Rounds stuck in `Pending`/`Debited` past a 30s grace period (longer than
  the 10s total Wallet-call budget, so it does not race a live request) are cancelled or credited. Batches are
  claimed with `FOR UPDATE SKIP LOCKED`, so several Game instances can run it; one bad round does not stop the
  batch; an unavailable Wallet means the round is retried about every grace period. A Wallet 4xx is deterministic,
  so the round goes to `NeedsAttention` (logged at Error) rather than looping forever.
- **Replays are read-only.** Posting a known `roundId` returns the stored result, or 503 `round_pending` with the
  round status under `roundStatus`. It never re-rolls and never calls Wallet; only the original request and the
  recovery job move money. The round `Status` is an EF concurrency token, so a stale writer cannot overwrite a
  newer state, and a losing round settles in a single update.
- **Concurrency in Wallet.** The account row is locked `FOR UPDATE`. Bets and cancels also take a
  per-transaction-id advisory lock (they must look at other ledger rows before deciding); duplicate deposits,
  withdrawals and wins are serialised by the account lock plus the idempotency re-check done after the locks,
  so concurrent duplicates replay instead of failing. Balance changes use `UPDATE ... WHERE balance + delta >= 0 RETURNING`, with a `CHECK (balance >= 0)`
  as a backstop. Overdrafts are impossible even under concurrent bets (tested).
- **Recovery batches run in one transaction.** A recovery pass processes its batch inside one DB transaction
  (rows claimed with `FOR UPDATE SKIP LOCKED`) and holds it across the Wallet calls. That is correct because
  Wallet calls are idempotent, but during a Wallet outage a pass can hold a connection for up to batch x 10s,
  and round updates become visible only at commit. Per-round transactions would be the next step.
- **Retries are safe because of the keys.** The Game's Wallet client uses the standard resilience handler (retries,
  3s attempt / 10s total timeout, circuit breaker). That is only acceptable because each call is idempotent.
- **Money** is integer cents (`bigint`); API amounts have at most 2 decimals. Wins are rounded **down** to the cent. Wallet rejects any single amount above $1,000,000.00 with 400 `validation_error`.
- **RNG** is `RandomNumberGenerator` (CSPRNG), behind an `IRandomSource` so tests can script outcomes.
- **No shared contracts library.** Each service owns its DTOs and the client has its own copies, so deployments
  are not coupled. Errors are RFC 7807 ProblemDetails with a stable `code`
  (unhandled exceptions are a 500 with `internal_error`; an unknown round is a 404 `round_not_found`). A Wallet
  rejection the Game does not map to `insufficient_funds` surfaces as 409 `round_rejected`, with the original
  Wallet code in a `reason` extension. HTTP 408 and 429 are treated as transient (unavailable), never as final.
- **Why PostgreSQL.** Services are stateless, so they scale out; state is in Postgres. A wallet needs ACID
  across ledger + balance more than write scale-out. Every operation touches one player, so the scale-out
  path is sharding by `player_id` (Citus or application-level); rounds partition the same way.
  SQL Server would fit equally well; Cosmos DB/DynamoDB scale writes but add lock-in and a weaker local test story.

## 4. Alternatives rejected

| Alternative | Why not |
|---|---|
| One atomic "settle round" call (Game rolls, then sends bet + win together) | Cannot half-fail, but Wallet learns about game outcomes, funds are not committed before the outcome, and it sidesteps the distributed-consistency problem. |
| Async messaging (broker + outbox) | The player needs the balance synchronously after each bet, so it would be request/response over a broker anyway. The path for scale, overkill here. |
| Game proxies deposits/withdrawals | Turns Game into a money gateway. |
| Shared contracts library | Couples deployments. |
| In-memory storage | Loses money on restart and cannot scale horizontally. |
| Other databases | See the Postgres argument above. |

## 5. Consciously left out

- **No reconciliation job between services.** The ledger and the rounds are only tied together by the recovery job.
- **`NeedsAttention` rounds need an operator.** There is no automated dead-letter handling.
- **No authentication.** The player id is just a name.
- Currencies (single implicit USD), responsible-gambling limits, RNG certification.
- Outbox/broker and event streaming (e.g. real-time bonusing); tracing/metrics (OpenTelemetry would be next);
  rate limiting.
- Migrations run at service startup; production would use a separate step. Sharding is described, not built.
- NuGet package versions are pinned in the csproj files; there is no lock file for transitive dependencies.

## 6. Ambiguities and interpretations

- **"Win up to x2"** is read as a win in (0, 2x]; "between x2 and x10" as (2x, 10x]. 50% lose, 40% tier 1, 10% tier 2.
  Wins are rounded down to the cent, so a "win" is never zero: a low-tier win below 1 cent pays $0.01, and a
  high-tier win at or below 2x pays 2x + $0.01.
- **Win amount** is the gross payout that is credited; the bet is debited separately, so `new = old - bet + win` holds.
- **Loss message** is not specified: `No luck this time! Your current balance is: $x`.
- The brief's "succesful" typo is corrected in the output; whole-dollar amounts print without decimals (`$10`), otherwise two (`$8.50`).
- After a 503 the client polls the round for about a minute, then reports the wallet balance and says the
  round will settle automatically.
