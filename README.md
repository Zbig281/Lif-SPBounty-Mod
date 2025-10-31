# SP::BountyMod (LiF:YO) — short README

Lightweight bounty system using **copper**. Players can place bounties, see a list, and claim payouts. Anti-abuse: **no payout if killer and victim are in the same guild** (bounty stays active).

## Install
- Put files in:
  - `mods/SPBounty/mod.cs` (server)
  - `mods/SPBounty/cmod.cs` + `mods/SPBounty/ui/bounty_bg.png` (client UI)
- Start server — tables `spb_bounties`, `spb_kills`, `spb_payouts` are auto-created.

## Config (`mod.cs`)
```ts
$SPBounty::CoinTypeID = 1059; // currency (1059 = Copper)
$SPBounty::MaxSteps   = 10000; // inventory scan limit
$SPBounty::Dbg        = false; // debug logs
Player commands
!bounty — open UI

!BountyGive <First> <Last> <N>c — place N copper

!BountyList — show active bounties

!BountyTake — claim your payout

Note: Paying a bounty consolidates your copper stacks and returns change.

Admin notes
Same-guild kill ⇒ no payout, bounty not cleared (logged with Amount=0).

Knockouts ignored. Works with/without LiFx.

Change currency via $SPBounty::CoinTypeID.

Troubleshooting
“DB not ready” at boot → wait; mod retries automatically.

UI doesn’t open → ensure cmod.cs + ui/bounty_bg.png are loaded for clients.