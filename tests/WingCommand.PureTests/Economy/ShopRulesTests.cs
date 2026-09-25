using Xunit;

namespace WingCommand.PureTests
{
    public class ShopRulesTests
    {
        private static ShopWing Wing() => new ShopWing
        {
            Host = true, Flying = true, HasFaction = true, Members = 1, Pending = 0, MaxMembers = 3, Funds = 1000f, PlayerRank = 2,
            FactionAi = 3, FactionAiLimit = 6f, Mode = OverLimitMode.Surcharge, BasesOn = 2, RtbCandidate = true,
        };

        private static ShopAirframe Jet() => new ShopAirframe
        {
            Value = 87f, RankRequired = 1, Jet = true, Declared = true, FactionStock = 4, Held = 0, BasesForClass = 2,
        };

        // ---- wing-wide blockers

        [Fact]
        public void AFullWingBlocksOnceWithTheCountAndTheInbound()
        {
            ShopWing w = Wing();
            w.Members = 2;
            w.Pending = 1;
            ShopQuote q = ShopRules.Quote(w, Jet());
            Assert.Equal(WingBlock.WingFull, q.Wing);
            Assert.Equal(TileBlock.None, q.Tile);
            Assert.Equal("Wing is full (2/3 · 1 inbound)", ShopRules.WingReason(q.Wing, w));
            Assert.Equal("BLOCKED · Wing is full (2/3 · 1 inbound)", ShopRules.Blocker(q, w, Jet()));
            Assert.DoesNotContain("full", ShopRules.TileFoot(q, Jet(), false));
        }

        [Fact]
        public void PendingLaunchesTakeWingRoomLikeMembers()
        {
            ShopWing w = Wing();
            w.Members = 0;
            w.Pending = 3;
            Assert.Equal(WingBlock.WingFull, ShopRules.WingBlocker(w));
            w.Pending = 2;
            Assert.Equal(WingBlock.None, ShopRules.WingBlocker(w));
        }

        [Fact]
        public void TheBlockersComeInOrderClientNotFlyingNoFactionFullNoBase()
        {
            ShopWing w = Wing();
            w.Host = false;
            w.Flying = false;
            w.HasFaction = false;
            w.BasesOn = 0;
            w.Members = 3;
            Assert.Equal(WingBlock.Client, ShopRules.WingBlocker(w));
            w.Host = true;
            Assert.Equal(WingBlock.NotFlying, ShopRules.WingBlocker(w));
            w.Flying = true;
            Assert.Equal(WingBlock.NoFaction, ShopRules.WingBlocker(w));
            w.HasFaction = true;
            Assert.Equal(WingBlock.WingFull, ShopRules.WingBlocker(w));
            w.Members = 1;
            Assert.Equal(WingBlock.NoBaseOn, ShopRules.WingBlocker(w));
            Assert.Equal("No launch base is ON", ShopRules.WingReason(WingBlock.NoBaseOn, w));
            Assert.Equal("HOST ONLY · the host requisitions", ShopRules.WingReason(WingBlock.Client, w));
        }

        [Fact]
        public void WingWideBlockersComeBeforeAnyTileReasonOnTheCardButTilesKeepTheirOwn()
        {
            ShopWing w = Wing();
            w.Members = 3;
            ShopAirframe a = Jet();
            a.RankRequired = 5;
            ShopQuote q = ShopRules.Quote(w, a);
            Assert.Equal(WingBlock.WingFull, q.Wing);
            Assert.Equal(TileBlock.Rank, q.Tile);
            Assert.StartsWith("BLOCKED · Wing is full", ShopRules.Blocker(q, w, a));
            Assert.Equal("RANK 5", ShopRules.TileFoot(q, a, false));
        }

        [Fact]
        public void AnAirframeNoLongerListedIsNotOffered()
        {
            ShopAirframe a = Jet();
            a.Restricted = true;
            ShopQuote q = ShopRules.Quote(Wing(), a);
            Assert.Equal(WingBlock.NotOffered, q.Wing);
            Assert.False(q.Allowed);
        }

        // ---- listing and tile reasons

        [Fact]
        public void ARestrictedOrVtolOrPlaceholderAirframeIsNotListed()
        {
            ShopAirframe a = Jet();
            Assert.True(ShopRules.Listed(a, false));
            a.Vtol = true;
            Assert.False(ShopRules.Listed(a, true));
            a = Jet();
            a.Placeholder = true;
            Assert.False(ShopRules.Listed(a, true));
        }

        [Fact]
        public void AnUndeclaredAirframeIsListedOnlyInTheSandboxOrWhenOneIsInTheHangar()
        {
            ShopAirframe a = Jet();
            a.Declared = false;
            a.FactionStock = 0;
            Assert.False(ShopRules.Listed(a, sandbox: false));
            Assert.True(ShopRules.Listed(a, sandbox: true));
            a.Held = 1;
            Assert.True(ShopRules.Listed(a, false));
        }

        [Fact]
        public void AnAirframeAboveThePlayersRankShowsTheRankItNeedsAndEqualIsEnough()
        {
            ShopAirframe a = Jet();
            a.RankRequired = 3;
            ShopQuote q = ShopRules.Quote(Wing(), a);
            Assert.Equal(TileBlock.Rank, q.Tile);
            Assert.Equal("BLOCKED · needs rank 3 (you are 2)", ShopRules.Blocker(q, Wing(), a));
            a.RankRequired = 2;
            Assert.Equal(TileBlock.None, ShopRules.Quote(Wing(), a).Tile);
        }

        [Fact]
        public void NoFactionStockAndNothingInTheHangarReadsNoStock()
        {
            ShopAirframe a = Jet();
            a.FactionStock = 0;
            ShopQuote q = ShopRules.Quote(Wing(), a);
            Assert.Equal(TileBlock.NoStock, q.Tile);
            Assert.Equal("NO STOCK", ShopRules.TileFoot(q, a, false));
        }

        [Fact]
        public void AnAirframeInTheHangarIsBuyableWithEmptyFactionStockIsUsedFirstAndCostsItsListPrice()
        {
            ShopAirframe a = Jet();
            a.FactionStock = 0;
            a.Held = 1;
            ShopQuote q = ShopRules.Quote(Wing(), a);
            Assert.True(q.Allowed);
            Assert.True(q.FromHeld);
            Assert.Equal(87f, q.Price);
        }

        [Fact]
        public void TooLittleAllocationNamesThePriceAndTheFundsInCreditsAndEqualIsEnough()
        {
            ShopWing w = Wing();
            w.Funds = 20f;
            ShopQuote q = ShopRules.Quote(w, Jet());
            Assert.Equal(TileBlock.Funds, q.Tile);
            Assert.Equal("BLOCKED · needs 87 CR, you have 20 CR", ShopRules.Blocker(q, w, Jet()));
            Assert.Equal("NEED 87 CR", ShopRules.TileFoot(q, Jet(), false));
            w.Funds = 87f;
            Assert.True(ShopRules.Quote(w, Jet()).Allowed);
        }

        [Fact]
        public void NoOnBaseForTheClassReadsNoBaseOnTheTile()
        {
            ShopAirframe a = Jet();
            a.BasesForClass = 0;
            ShopQuote q = ShopRules.Quote(Wing(), a);
            Assert.Equal(TileBlock.NoBase, q.Tile);
            Assert.Equal("NO BASE", ShopRules.TileFoot(q, a, false));
        }

        // ---- over the faction's AI limit (user decision 2026-09-25)

        [Fact]
        public void UnderTheFactionLimitNoModeChangesAnything()
        {
            foreach (OverLimitMode m in new[] { OverLimitMode.Surcharge, OverLimitMode.MatchEnemy, OverLimitMode.RtbOne })
            {
                ShopWing w = Wing();
                w.Mode = m;
                w.RtbCandidate = false;
                ShopQuote q = ShopRules.Quote(w, Jet());
                Assert.True(q.Allowed, m.ToString());
                Assert.False(q.OverLimit);
                Assert.Equal(87f, q.Price);
            }
        }

        [Fact]
        public void OurOwnWingmenCountTowardTheFactionLimitAsTheGameCountsIt()
        {
            // The game deploys no more AI once the count reaches the limit (FactionHQ.DeployAIAircraft): so do we.
            ShopWing w = Wing();
            w.FactionAi = 6;
            w.FactionAiLimit = 6f;
            Assert.True(ShopRules.OverCap(w));
            w.FactionAi = 5;
            Assert.False(ShopRules.OverCap(w));
            w.FactionAiLimit = 5.5f;
            Assert.False(ShopRules.OverCap(w));
        }

        [Fact]
        public void AtTheLimitTheSurchargeTriplesThePriceAtAnyRank()
        {
            ShopWing w = Wing();
            w.FactionAi = 6;
            w.PlayerRank = 0;
            ShopAirframe a = Jet();
            a.RankRequired = 0;
            ShopQuote q = ShopRules.Quote(w, a);
            Assert.True(q.Allowed);
            Assert.True(q.OverLimit);
            Assert.Equal(261f, q.Price);
            Assert.Equal("OVER LIMIT (6/6) · ×3 COST", ShopRules.OverLimitNote(w));
        }

        [Fact]
        public void AtTheLimitMatchEnemyAndRtbOneKeepThePrice()
        {
            ShopWing w = Wing();
            w.FactionAi = 6;
            w.Mode = OverLimitMode.MatchEnemy;
            ShopQuote q = ShopRules.Quote(w, Jet());
            Assert.Equal((true, true, 87f), (q.Allowed, q.OverLimit, q.Price));
            Assert.Equal("OVER LIMIT (6/6) · ENEMY +1", ShopRules.OverLimitNote(w));
            w.Mode = OverLimitMode.RtbOne;
            q = ShopRules.Quote(w, Jet());
            Assert.Equal((true, true, 87f), (q.Allowed, q.OverLimit, q.Price));
            Assert.Equal("OVER LIMIT (6/6) · 1 AI TO BASE", ShopRules.OverLimitNote(w));
        }

        [Fact]
        public void RtbOneWithNoFactionAiToSendHomeBlocksWithTheLimitInWords()
        {
            ShopWing w = Wing();
            w.FactionAi = 6;
            w.Mode = OverLimitMode.RtbOne;
            w.RtbCandidate = false;
            Assert.Equal(WingBlock.NoAiToSendHome, ShopRules.WingBlocker(w));
            Assert.Equal("Faction AI limit (6/6) · no AI can go home", ShopRules.WingReason(WingBlock.NoAiToSendHome, w));
            w.FactionAi = 3;
            Assert.Equal(WingBlock.None, ShopRules.WingBlocker(w));
        }

        // ---- review R4a (fix pass)

        [Fact]
        public void EachAircraftOfACallIsQuotedWithTheOnesBeforeIt()
        {
            ShopWing w = Wing();
            w.Members = 0;
            w.MaxMembers = 7;
            w.FactionAi = 5;
            ShopAirframe a = Jet();
            ShopQuote first = ShopRules.Quote(ShopRules.After(w, 0, 0f), a);
            Assert.False(first.OverLimit);
            Assert.Equal(87f, first.Price);
            ShopWing second = ShopRules.After(w, 1, 87f);
            Assert.Equal(6, second.FactionAi);
            Assert.Equal(1, second.Pending);
            Assert.Equal(913f, second.Funds);
            ShopQuote q2 = ShopRules.Quote(second, a);
            Assert.True(q2.OverLimit);
            Assert.Equal(261f, q2.Price);
        }

        [Fact]
        public void MatchEnemyGivesTheEnemyOnlyWhatTheFactionIsOverByNow()
        {
            ShopWing w = Wing();
            w.Mode = OverLimitMode.MatchEnemy;
            w.FactionAi = 6;
            Assert.Equal(1, ShopRules.EnemyBonus(w, 1, 0));
            // A lost wingman's replacement: the faction is no further over than before.
            Assert.Equal(0, ShopRules.EnemyBonus(w, 1, 1));
            w.FactionAi = 7;
            Assert.Equal(1, ShopRules.EnemyBonus(w, 1, 1));
            w.FactionAi = 3;
            Assert.Equal(0, ShopRules.EnemyBonus(w, 1, 0));
            w.FactionAiLimit = 5.5f;
            w.FactionAi = 6;
            Assert.Equal(1, ShopRules.EnemyBonus(w, 1, 0));
        }

        [Fact]
        public void ASandboxTileSaysFreeNotItsStock()
        {
            ShopWing w = Wing();
            w.Sandbox = true;
            ShopAirframe a = Jet();
            a.FactionStock = 0;
            Assert.Equal("FREE · SANDBOX", ShopRules.TileFoot(ShopRules.Quote(w, a), a, true));
            Assert.Equal("87 CR · 4 LEFT", ShopRules.TileFoot(ShopRules.Quote(Wing(), Jet()), Jet(), false));
        }

        [Fact]
        public void TheSandboxNeverUsesTheHangarStore()
        {
            ShopWing w = Wing();
            w.Sandbox = true;
            ShopAirframe a = Jet();
            a.Held = 1;
            Assert.False(ShopRules.Quote(w, a).FromHeld);
        }

        [Fact]
        public void OverTheLimitInTheSandboxCostsNothingExtra()
        {
            ShopWing w = Wing();
            w.Sandbox = true;
            w.FactionAi = 6;
            Assert.Equal("OVER LIMIT (6/6) · SANDBOX, NO ×3", ShopRules.OverLimitNote(w));
            w.Mode = OverLimitMode.MatchEnemy;
            Assert.Equal("OVER LIMIT (6/6) · ENEMY +1", ShopRules.OverLimitNote(w));
        }

        [Fact]
        public void UnderTheLimitTheNoteCountsTheFactionsAi() => Assert.Equal("AI 3/6", ShopRules.OverLimitNote(Wing()));

        // ---- sandbox

        [Fact]
        public void TheSandboxIsFreeAndSkipsStockButKeepsRankAndCapacity()
        {
            ShopWing w = Wing();
            w.Sandbox = true;
            w.FactionAi = 6;
            ShopAirframe a = Jet();
            a.FactionStock = 0;
            ShopQuote q = ShopRules.Quote(w, a);
            Assert.True(q.Allowed);
            Assert.Equal(0f, q.Price);
            Assert.Equal("FREE · SANDBOX", ShopRules.TileFoot(q, a, true));
            a.RankRequired = 9;
            Assert.Equal(TileBlock.Rank, ShopRules.Quote(w, a).Tile);
            w.Members = 3;
            Assert.Equal(WingBlock.WingFull, ShopRules.Quote(w, Jet()).Wing);
        }

        // ---- launch bases

        private static ShopBase Base(bool on, float dist, bool carrier = false, bool hangar = false, bool picked = false) =>
            new ShopBase { On = on, DistSq = dist * dist, Carrier = carrier, HangarReady = hangar, Picked = picked };

        [Fact]
        public void AJetNeverTakesACarrierAndAHelicopterMay()
        {
            var bases = new[] { Base(true, 1000f, carrier: true), Base(true, 5000f) };
            Assert.Equal(1, ShopRules.PickBase(LaunchMode.Nearest, true, bases, 2));
            Assert.Equal(0, ShopRules.PickBase(LaunchMode.Nearest, false, bases, 2));
        }

        [Fact]
        public void NearestTakesThePickedFieldWhenItIsOnElseTheNearestOn()
        {
            var bases = new[] { Base(true, 1000f), Base(true, 9000f, picked: true), Base(false, 500f) };
            Assert.Equal(1, ShopRules.PickBase(LaunchMode.Nearest, true, bases, 3));
            bases[1].On = false;
            Assert.Equal(0, ShopRules.PickBase(LaunchMode.Nearest, true, bases, 3));
        }

        [Fact]
        public void AnyPrefersTheNearestFieldWithAHangarReadyForTheType()
        {
            var bases = new[] { Base(true, 1000f), Base(true, 9000f, hangar: true), Base(true, 20000f, hangar: true) };
            Assert.Equal(1, ShopRules.PickBase(LaunchMode.Any, true, bases, 3));
            bases[1].HangarReady = bases[2].HangarReady = false;
            Assert.Equal(0, ShopRules.PickBase(LaunchMode.Any, true, bases, 3));
        }

        [Fact]
        public void AnOffBaseIsNeverPicked()
        {
            var bases = new[] { Base(false, 1000f, hangar: true, picked: true) };
            Assert.Equal(-1, ShopRules.PickBase(LaunchMode.Nearest, true, bases, 1));
            Assert.Equal(-1, ShopRules.PickBase(LaunchMode.Any, true, bases, 1));
        }

        // ---- fuel, dispatch text

        [Theory]
        [InlineData(25, 50)]
        [InlineData(50, 75)]
        [InlineData(75, 100)]
        [InlineData(100, 25)]
        [InlineData(40, 100)]
        public void FuelStepsGo25To100AndWrap(int from, int to) => Assert.Equal(to, ShopRules.NextFuel(from));

        [Fact]
        public void TheDispatchLineNamesPilotFitFuelAndBaseWithinTheCard()
        {
            Assert.Equal("HATCH · FIT AUTO · FUEL 100% · FROM NORTH BOSCALI AB",
                ShopRules.DispatchLine("HATCH", "AUTO", 100, "North Boscali AB"));
            Assert.Equal("NEW PILOT · FIT AUTO · FUEL 50% · NO BASE", ShopRules.DispatchLine(null, "AUTO", 50, null));
            Assert.True(ShopRules.DispatchLine("ABCDEFGHIJKLMN", "ABCDEFGHIJKLMNOP", 100, "ABCDEFGHIJKLMNOPQRST").Length <= 79);
        }

        [Fact]
        public void TheDispatchStateReadsReadyInboundBlockedOrNoAirframe()
        {
            ShopQuote ok = ShopRules.Quote(Wing(), Jet());
            Assert.Equal("READY", ShopRules.State(ok, true, 0, out string s));
            Assert.Equal("live", s);
            Assert.Equal("INBOUND 2", ShopRules.State(ok, true, 2, out s));
            Assert.Equal("info", s);
            ShopWing full = Wing();
            full.Members = 3;
            Assert.Equal("BLOCKED", ShopRules.State(ShopRules.Quote(full, Jet()), true, 0, out s));
            Assert.Equal("warn", s);
            Assert.Equal("NO AIRFRAME", ShopRules.State(ok, false, 0, out s));
            Assert.Equal("inert", s);
        }

        [Fact]
        public void EveryTileFootFitsTheTileAndEveryBlockerTheCard()
        {
            ShopWing rich = Wing();
            rich.Funds = 9_999_999f;
            ShopAirframe a = Jet();
            a.Value = 1250f;
            a.FactionStock = 99;
            Assert.True(ShopRules.TileFoot(ShopRules.Quote(rich, a), a, false).Length <= 18);
            ShopWing poor = Wing();
            poor.Funds = 9620f;
            a.Value = 999_999f;
            Assert.True(ShopRules.TileFoot(ShopRules.Quote(poor, a), a, false).Length <= 18);
            Assert.True(ShopRules.Blocker(ShopRules.Quote(poor, a), poor, a).Length <= 79);
            foreach (WingBlock b in System.Enum.GetValues(typeof(WingBlock)))
                Assert.True(ShopRules.WingReason(b, rich).Length <= 68, b.ToString());
        }

        // ---- the hangar store (user decision 2026-09-25: HANGAR with STORE / RETURN)

        [Fact]
        public void StoreNeedsHostFactionRoomAndFactionStock()
        {
            Assert.Null(ShopRules.StoreBlock(host: true, faction: true, count: 1, capacity: 3, factionStock: 2));
            Assert.Equal("Host only", ShopRules.StoreBlock(false, true, 1, 3, 2));
            Assert.Equal("No faction", ShopRules.StoreBlock(true, false, 1, 3, 2));
            Assert.Equal("The hangar is full (3/3) · return one first", ShopRules.StoreBlock(true, true, 3, 3, 2));
            Assert.Equal("None left in the faction's stock", ShopRules.StoreBlock(true, true, 1, 3, 0));
        }

        [Fact]
        public void ReturnNeedsAStoredAirframeOfThatType()
        {
            Assert.Null(ShopRules.ReturnBlock(true, true, stored: 1));
            Assert.Equal("None of this type is in the hangar", ShopRules.ReturnBlock(true, true, 0));
            Assert.Equal("Host only", ShopRules.ReturnBlock(false, true, 1));
        }
    }
}
