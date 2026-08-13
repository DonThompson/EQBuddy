namespace EQBuddy.Core;

public sealed record EpicQuestGuide(string ClassName, string Summary, string[] Steps);

public static class EpicQuestDefaults
{
    public static List<EpicQuestChecklistItem> Items()
    {
        var catalog = QuestCatalog.LoadEmbedded();
        var items = new List<EpicQuestChecklistItem>();

        foreach (var className in QuestClassFilter.Classes)
        {
            var quest = FindQuest(catalog, className);
            if (quest is null) continue;

            var reward = string.Join(", ", quest.Rewards.Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
            var grouped = quest.Items
                .Where(i => i.Name.Length > 0)
                .GroupBy(i => QuestCatalog.BaseItemName(i.Name), StringComparer.OrdinalIgnoreCase)
                .Select(g => new QuestItemNeed { Name = g.First().Name, Qty = g.Max(i => Math.Max(1, i.Qty)) })
                .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < grouped.Count; i++)
            {
                var need = grouped[i];
                items.Add(new EpicQuestChecklistItem
                {
                    Id = $"epic-{QuestClassFilter.Abbrev(className).ToLowerInvariant()}-{i:000}-{StableKey(need.Name)}",
                    ClassName = className,
                    QuestName = quest.Name,
                    Reward = reward,
                    QuestItem = need.Name,
                    Qty = need.Qty,
                    Source = SourceFor(className, need.Name, quest),
                });
            }
        }

        return items;
    }

    public static EpicQuestGuide? GuideFor(string className) =>
        Guides.FirstOrDefault(g => g.ClassName.Equals(className, StringComparison.OrdinalIgnoreCase));

    public static string SourceFor(string className, string itemName, QuestEntry quest)
    {
        var baseName = QuestCatalog.BaseItemName(itemName);
        var guide = GuideFor(className);
        var step = guide?.Steps.FirstOrDefault(s => s.Contains(baseName, StringComparison.OrdinalIgnoreCase));
        return step ?? SourceLine(quest);
    }

    public static QuestEntry? FindQuest(QuestCatalog catalog, string className) =>
        catalog.Quests.FirstOrDefault(q =>
            q.Name.Equals($"{className} Epic Quest", StringComparison.OrdinalIgnoreCase));

    public static string SourceLine(QuestEntry quest)
    {
        var start = quest.StartZone.Length > 0
            ? quest.QuestGiver.Length > 0 ? $"{quest.StartZone}: {quest.QuestGiver}" : quest.StartZone
            : quest.QuestGiver;
        var zones = quest.Zones.Where(z => z.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToList();
        var zoneText = zones.Count == 0 ? "" : "Zones: " + string.Join(", ", zones);
        return start.Length > 0 && zoneText.Length > 0 ? $"{start} | {zoneText}" : start + zoneText;
    }

    private static readonly EpicQuestGuide[] Guides =
    [
        new("Bard",
            "Long travel chain with several named kills, then the Trakanon/Phinigel/VS pieces for Singing Short Sword.",
            [
                "Run the torch relay: Konia Swiftfoot in West Karana -> Fajio Knejo in Misty Thicket -> Andad Filla in South Ro -> Misty Tekchita in Lake Rathetear for Proof of Speed.",
                "Return Proof of Speed to Konia for Maestro's Symphony Page 24 Top.",
                "Baenar Swiftsong in South Karana sends you through Marfen in Solusek's Eye, Serra in Unrest, and Maligar in West Karana; kill Maligar's enraged doppleganger for Maligar's Head.",
                "Turn Maligar's Head in to Baenar for Mahlin's Mystical Bongos, then give the bongos to Konia for Maestro's Symphony Page 24 Bottom.",
                "Loot Onyx Drake Gut from Blackwing in Rathe Mountains, Red Wurm Gut from Nezekezena or Phurzikon in Burning Woods, and Chromodrac Gut from Eldrig the Old in Skyfire.",
                "Finish the late raid drops and combines: Trakanon Gut, White Dragon Scales, Red Dragon Scales, Undead Dragongut Strings, Kedge Backbone, and related lute parts."
            ]),
        new("Cleric",
            "Water/Fire chain through Lake Rathe, Timorous Deep, Sol Ro, Burning Woods, Chardok, then Ragefire for Water Sprinkler.",
            [
                "Kill Lord Bergurgle in Lake Rathetear for Lord Bergurgle's Crown; give it to Shmendrik Lavawalker to spawn Natasha and get Oil of Fennin Ro.",
                "Kill Shmendrik's spawned spirit for Damaged Goblin Crown, then give it to Natasha for Ornate Sea Shell.",
                "Give Ornate Sea Shell to Omat Vastsea in Timorous Deep for Coral Statue of Tarew.",
                "Give Coral Statue of Tarew to a seeker in Temple of Solusek Ro; kill the Plasmatic Priest and loot Blood Soaked Plasmatic Priest Robe.",
                "Loot Lord Gimblox's Signet Ring in Solusek's Eye; use Natasha/Omat/Naxot steps to spawn Ixiblat Fer in Burning Woods.",
                "Loot Sceptre of Ixiblat Fer from Ixiblat Fer and Singed Scroll from Overking Bathezid in Chardok; finish through Omat/Natasha and Ragefire."
            ]),
        new("Druid",
            "Nature chain shared with ranger starts at Telin, then forage/combine work, corrupted fights, Faydedar, and final cleansed spirits.",
            [
                "Speak with Telin Darkforest in Burning Woods for Worn Note; give it to Faelin Bloodbriar in Greater Faydark for Faelin's Ring.",
                "Give Faelin's Ring to Giz X'Tin in Kithicor, then return the Dark Metal Coin chain to Telin and Althele in East Karana.",
                "Use the Braided Grass Amulet on Sionae, Nuien, and Teloa in East Karana; intercept the Dark Elf Corruptor and loot Fleshbound Tome.",
                "Give Fleshbound Tome to Althele, then Ella Foodcrafter in Misty Thicket for Shiny Tin Bowl.",
                "Forage Chilled Tundra Root, Ripened Heartfruit, Speckled Molded Mushroom, and Sweetened Mudroot; combine them in the Shiny Tin Bowl for Hardened Mixture.",
                "Continue the bowl/stones path through Timorous Deep, Felwithe, Chardok, the corrupted seafury/cyclops/brownie/gorilla, Faydedar, and the final druid spirits."
            ]),
        new("Enchanter",
            "Stofo/Jeb notes chain, then Vessel Drozlin, Verina Tomb, Chardok prince/queen, Wraith of a Shissar, and final Staff of the Serpent turn-ins.",
            [
                "Hail Stofo Olan in Erudin at level 50.",
                "Get Empty Ink Vial from Reania Jukle in Qeynos Catacombs; charm a ghoul scribe in Lower Guk and turn it in for Ink of the Dark.",
                "Loot Shining Metallic Robes from ghoul arch magus in Lower Guk; give them to Rilgor Plegnog in Ak'Anon for Mechanical Pen.",
                "Buy Quill and Piece of Parchment; give them to Chrislin Baker in West Karana, kill Thrackin Griften, and loot White Paper.",
                "Give Ink of the Dark, Mechanical Pen, and White Paper to Stofo for Copy of Notes, then give Copy of Notes to Jeb Lumsed in Burning Woods for Jeb's Seal.",
                "Loot Xolion Rod from Vessel Drozlin, Innoruuk's Word from Verina Tomb, Chalice of Kings through Chardok royals, and Wraith of a Shissar pieces before final Jeb turn-ins."
            ]),
        new("Magician",
            "Token of Mastery starts the quest, then Magi`kot pages, four elemental powers, four mastery pages, four elemental staves, and Phinny.",
            [
                "Get Token of Mastery from Rykas in Lake Rathetear and take it to Jahsohn Aksot in West Commonlands.",
                "Loot Torn Page of Magi`kot pg. 1 from enraged dread wolf in Kithicor, pg. 2 from tentacle terrors in Unrest, and pg. 3 from bloodthirsty ghoul in Lower Guk.",
                "Turn the Magi`kot pages in to Jahsohn for Words of Magi`kot.",
                "Loot Power of Wind from a gypsy dancer in Mistmoore, Power of Fire from Solusek's Eye elementals, Power of Water from Kaesora raveners, and Power of Earth from faerie guards in Lesser Faydark.",
                "Give the four powers to Walnan in Butcherblock for Power of the Elements.",
                "Collect Torn Page of Mastery Earth/Fire/Water/Wind, turn them in at Najena, then finish the Staff of Elemental Mastery set and Phinigel piece for Orb of Mastery."
            ]),
        new("Monk",
            "Robe chain, Danl/Lheao book, Eejag/Gwan/Trunt fights, then Kaiaren and Demon Fangs for Celestial Fists.",
            [
                "Get Robe of the Lost Circle by killing Brother Zephyl or Brother Qwinn, or by the sash/headband subquest.",
                "For the robe subquest: Purple Headband + Code of Zan Fi from Targin the Rock in Sol B go to Brother Qwinn for Needle of the Void.",
                "Red Sash of Order + The Idol from Raster of Guk in Lower Guk go to Brother Zephyl for Rare Robe Pattern.",
                "Combine Shadow Silk, Needle of the Void, Rare Robe Pattern, and Jonthan's Whistling Warsong to make Robe of the Lost Circle.",
                "Loot the Chardok and Karnor metal pipes, then give both pipes plus Robe of the Lost Circle to Brother Balatin in Dreadlands for Robe of the Whistling Fists.",
                "Turn Immortals in to Tomekeeper Danl in Erudin for Danl's Reference; give Danl's Reference and Robe of the Whistling Fists to Lheao in Timorous Deep for Celestial Fists book.",
                "Spawn and kill Eejag in Lavastorm for Charred Scale; use it in Plane of Sky to spawn Gwan for Breath of Gwan; give Breath of Gwan in Nurga to spawn Trunt for Trunt's Head.",
                "Use the Kaiaren/Deep chain in Lake of Ill Omen and Trakanon's Teeth; kill Xenevorash for Demon Fangs, then give the modified Book of Celestial Fists and Demon Fangs to sane Kaiaren."
            ]),
        new("Necromancer",
            "Symbol chain through Nektulos/Lake Rathe, Najena robe, Chardok herb, Kazen/bone golem, then Fear and Plane of Sky pieces.",
            [
                "Start with Venenzi Oberzendi in Nektulos and Kazen Fecae in Lake Rathetear.",
                "Kill Sir Edwin Motte for his Head; give it to Kazen for Symbol of the Apprentice, then give that to Venenzi for Twisted Symbol.",
                "Loot Flowing Black Robe from Najena and give it to Venenzi for Rolling Stone Moss.",
                "Give Rolling Stone Moss and Twisted Symbol to Emkel Kabae in Lake Rathetear for Symbol of the Serpent.",
                "Give Symbol of the Serpent to Ssessthrass in Swamp of No Hope for Scaled Symbol; loot Manisi Herb from Chardok herbalists and refine it through Ssessthrass/Emkel.",
                "Continue through Kazen, bone golem, Plane of Sky, Plane of Fear, and the final turn-ins for Scythe of the Shadowed Soul."
            ]),
        new("Paladin",
            "Fiery Avenger plus three purified darksteel pieces; final Plane of Fear turn-in gives Fiery Defender.",
            [
                "Complete SoulFire, Ghoulbane, and Fiery Avenger first.",
                "Loot Tainted Darksteel Breastplate from thought destroyer in Plane of Hate; purify it with Pure Crystal through Jark/Nella in North Kaladim and Reklon Gnallen in Erudin.",
                "Loot Tainted Darksteel Sword from Keeper of the Tombs in The Hole; purify it with Bucket of Pure Water through West Freeport and Reklon Gnallen.",
                "Loot Tainted Darksteel Shield from Kirak Vil in Nektulos Forest; purify it through Elia the Pure in Felwithe.",
                "Turn Gleaming Crested Breastplate, Gleaming Crested Sword, and Gleaming Crested Shield in to Reklon for Mark of Atonement.",
                "Give Mark of Atonement and Fiery Avenger to Irak Altil in Plane of Fear for Fiery Defender."
            ]),
        new("Ranger",
            "Druid-style bowl chain plus ranger-only Ancient Sword, Faydedar, VS stone, and final Earthcaller/Swiftwind turn-ins.",
            [
                "Ask Telin Darkforest in Burning Woods for Worn Note; give it to Faelin Bloodbriar in Greater Faydark for Faelin's Ring.",
                "Give Faelin's Ring to Giz X'Tin in Kithicor, then return to Telin and Althele in East Karana for the Braided Grass Amulet chain.",
                "Use Sionae, Nuien, and Teloa in East Karana; kill the Dark Elf Corruptor and return Fleshbound Tome to Althele.",
                "Get Shiny Tin Bowl from Ella Foodcrafter in Misty Thicket and forage Chilled Tundra Root, Ripened Heartfruit, Speckled Molded Mushroom, and Sweetened Mudroot for Hardened Mixture.",
                "Do the Ancient Pattern/Runecrested Bowl path through Timorous Deep, Felwithe, Chardok, and the foraged/ground components.",
                "Finish with Faydedar, Venril Sathir, Plane of Sky/Plane of Hate style bottlenecks, and the final hand-ins for Earthcaller and Swiftwind."
            ]),
        new("Rogue",
            "Stanos pouch, pickpocket parchment, Book of Souls/Cazic quill path, then General/Vilnius/Renux for Ragebringer.",
            [
                "Get Stanos' Pouch from Malka Rale in Qeynos Aqueducts or with help from a level 50 rogue.",
                "Pickpocket Stained Parchment Top from Founy Jestands in North Kaladim and Stained Parchment Bottom from Tani N'Mar in Neriak.",
                "Use Anson McBale in Highpass to spawn Stanos; turn in both parchment halves for Combined Parchment.",
                "Give Combined Parchment, 100pp, and two unstacked Bottles of Milk to Eldreth in Lake Rathetear for Scribbled Parchment.",
                "Take Scribbled Parchment to Yendar Starpyre in Steamfont for Tattered Parchment and continue the Book of Souls/Cazic Quill steps.",
                "Finish the Jagged Diamond Dagger path through General V'Ghera, Vilnius the Small, and Renux Herkanor for Ragebringer."
            ]),
        new("Shadow Knight",
            "Darkforge/Kurron/Duriek start, Corrupted Ghoulbane pieces, Kyrenna/Glohnor/Lhranc chain. Indifferent con matters on hand-ins.",
            [
                "Give Kurron Ni in Overthere Darkforge Breastplate, Darkforge Greaves, Darkforge Helm, and 900pp; kill Kurron and loot Letter to Duriek.",
                "Give Letter to Duriek Bloodpool in Paineel; buy Cough Elixir from Smaka in Neriak and give it to Duriek.",
                "Loot Dusty Tome from a ratman guard in The Hole and give it to Duriek.",
                "Loot Ghoulbane from froglok shin lord in Upper Guk, Soul Leech from Cazic-Thule or Fear golems, Blade of Abrogation from Plane of Sky, Drake Spine from Rharzar, and Decrepit Hide from ashenbone drakes in Plane of Hate.",
                "Give Drake Spine, Decrepit Hide, and Enchanted Platinum Bar to Teydar for Decrepit Sheath.",
                "Give Ghoulbane, Soul Leech, Blade of Abrogation, and Decrepit Sheath to Duriek for Corrupted Ghoulbane.",
                "Use Cell Key on Caradon in The Hole, kill Kyrenna, and loot Blood of Kyrenna and Heart of Kyrenna; Blood goes to Marl Kastane for Dark Shroud.",
                "Give Dark Shroud to Ghost of Glohnor in The Hole, kill Mummy of Glohnor for Head of Glohnor and Glohnor wrappings, then turn those in for Head of the Valiant and Will of Innoruuk.",
                "Combine Heart of Kyrenna in Soulcase for Heart of the Innocent; give Corrupted Ghoulbane, Heart of the Innocent, Head of the Valiant, and Will of Innoruuk to Lhranc, kill him, and loot Innoruuk's Curse."
            ]),
        new("Shaman",
            "True Spirit faction chain, Black Dire boots, City of Mist reports/books, High Scale Kirn/Neh`Ashiir, Fear tear, then Rak`Ashiir.",
            [
                "Spawn a lesser spirit by killing the RM/OoT/BBM/FoB trigger mobs; receive Tiny Gem and use Bondl Felligan in North Freeport to begin True Spirit faction.",
                "Use the proper Greater Spirit path to get Opaque Gem, then Test of Patience in Erud's Crossing.",
                "Kill Glaron the Wicked for Envy and Woe, and Tabien the Goodly for Marr's Promise in Rathe Mountains.",
                "Turn Envy, Woe, and Marr's Promise in to the wandering spirit in West Karana for Shield of Falsehood and the next gem.",
                "Kill Black Dire in Mistmoore for Black Dire Pelt; turn it in to Spirit Sentinel in Emerald Jungle for Black Fur Boots and booklet.",
                "Collect six City of Mist reports, then Lord Ghiosk's three books, and turn them in through Spirit Sentinel.",
                "Get Icon of the High Scale from City of Mist, give it to High Scale Kirn in The Hole, kill him, then give the ring to Neh`Ashiir in City of Mist and loot the diary.",
                "Loot Child's Tear from the Plane of Fear broodling/golem cycle; give it to Lord Rak`Ashiir in City of Mist, kill him, and loot Iksar Scale for the final Spear of Fate turn-in."
            ]),
        new("Warrior",
            "Two dragon-head hilts, ancient blades, red/green scales, then final Kargek/Wenden combines for Jagged Blade.",
            [
                "Hail Kargek Redblade and Wenden Blackhammer in East Freeport.",
                "Retrieve Unjeweled Dragon Head Hilt from Lake Rathetear; combine through Wenden with Diamond, Jacinth, and Black Sapphire for Jeweled Dragon Head Hilt.",
                "Retrieve Severely Damaged Dragon Head Hilt from Timorous Deep chessboard.",
                "Get Giant Sized Monocle from mountain giant patriarch in Dreadlands; trade it to Mentrax Mountainbone in Frontier Mountains for Rejesiam Ore.",
                "Loot Ball of Everliving Golem from Fright, Dread, or Terror in Plane of Fear, then turn in with Severely Damaged Dragon Head Hilt and Rejesiam Ore for Finely Crafted Dragon Head Hilt.",
                "Buy Keg of Vox Tail Ale, get two Rebreathers, and loot Block of Permafrost from ice giants; turn in to Denken Strongpick in Ocean of Tears for Ancient Sword Blade.",
                "Loot Ancient Blade from Queen Velazul Di`zok in Chardok, plus Red Dragon Scales and Green Dragon Scales for the final blade work."
            ]),
        new("Wizard",
            "Solomen/Kandin faction path, Cazic Skin and Gabstik, then Arantir combines the three staves for Staff of the Four.",
            [
                "Start with Solomen in Temple of Solusek Ro; the quest is TrueSpirit/faction based.",
                "Work the Solomen note chain to Kandin Firepot.",
                "Give Cazic's Skin from Cazic-Thule to Kandin for Kandin's Bag.",
                "Give Mistletoe Powder to Kandin to receive Staff of Gabstik.",
                "Return Kandin's Bag to Kandin for the next note.",
                "Give the note to Dargon to spawn Arantir; hand Arantir Blue Crystal Staff, Gnarled Staff, and Staff of Gabstik for the sealed bag.",
                "Give Arantir's bag to Solomen for Staff of the Four."
            ]),
    ];

    private static string StableKey(string value)
    {
        var chars = value.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var compact = new string(chars);
        while (compact.Contains("--", StringComparison.Ordinal))
            compact = compact.Replace("--", "-", StringComparison.Ordinal);
        return compact.Trim('-');
    }
}
