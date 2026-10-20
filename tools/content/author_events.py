"""Authoring script for events.json + their EN/VI localisation (kept in source control; re-run to regenerate).
Each event is a real choice with distinct outcomes; text is written per event, not templated."""
import json, os
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "Forgebound", "Resources", "Content")
EV, LOC_EN, LOC_VI = [], {}, {}
def O(t, **k): return dict(type=t, **k)
def ev(id, title, body, options, biome=None, minCh=1, weight=10):
    """options: list of (en, vi, dict(outcomes=[...], fail=[...], success=x, requireGold=..))"""
    LOC_EN[f"event.{id}.title"], LOC_VI[f"event.{id}.title"] = title
    LOC_EN[f"event.{id}.body"], LOC_VI[f"event.{id}.body"] = body
    opts = []
    for i, (en, vi, spec) in enumerate(options):
        key = f"event.{id}.opt{i}"
        LOC_EN[key], LOC_VI[key] = en, vi
        o = {"text": key}
        o.update(spec)
        opts.append(o)
    d = {"id": id, "minChapter": minCh, "weight": weight, "options": opts, "art": "fb_event_" + id}
    if biome: d["biome"] = biome
    EV.append(d)

leave = ("Leave.", "Rời đi.", {"outcomes": [O("nothing")]})
ev("wandering_smith", ("The Wandering Smith", "Thợ Rèn Lang Thang"),
   ("A smith with a cart of tools offers to work your steel for coin.", "Một thợ rèn kéo xe dụng cụ đề nghị rèn vũ khí của bạn để lấy vàng."),
   [("Pay 45 gold: temper a weapon.", "Trả 45 vàng: tôi luyện một vũ khí.", {"requireGold": 45, "requireTag": "weapon_upgradable", "outcomes": [O("upgradeWeapon")]}),
    ("Trade stories (heal 15%).", "Kể chuyện cùng ông (hồi 15% máu).", {"outcomes": [O("heal", value=15)]}), leave])
ev("cursed_anvil", ("The Cursed Anvil", "Đe Rèn Bị Nguyền"),
   ("An anvil hums with dark power. Striking it could forge something great — or something wrong.", "Một chiếc đe rung lên sức mạnh hắc ám. Gõ vào nó có thể rèn nên điều vĩ đại — hoặc tai hoạ."),
   [("Strike it (gain 2 Essence, gain a curse).", "Gõ búa (nhận 2 Tinh Chất, nhận một lời nguyền).", {"outcomes": [O("essence", value=2), O("gainCurse")]}),
    ("Pry loose the rune inlay (random rare rune).", "Cạy lớp khảm rune (rune hiếm ngẫu nhiên).", {"outcomes": [O("gainRandomItem", category="rune", minRarity="rare"), O("damage", value=10)]}), leave])
ev("ember_spring", ("Ember Spring", "Suối Than Hồng"), ("Molten water bubbles from the ground, warm and restorative.", "Nước nóng chảy sủi lên từ lòng đất, ấm áp và hồi sức."),
   [("Bathe (heal 30%).", "Ngâm mình (hồi 30% máu).", {"outcomes": [O("heal", value=30)]}),
    ("Quench a core in it (random fire-aspected core item).", "Tôi lõi trong suối (nhận lõi Lửa).", {"outcomes": [O("gainItem", category="core", id="fire")]})], biome="ember_foundry")
ev("bandit_toll", ("Bandit Toll", "Trạm Thu Phí Của Cướp"), ("Forge bandits block the road and demand payment.", "Bọn cướp lò rèn chặn đường đòi tiền mãi lộ."),
   [("Pay 30 gold.", "Trả 30 vàng.", {"requireGold": 30, "outcomes": [O("nothing")]}),
    ("Fight them (elite battle, better loot).", "Chiến đấu (trận tinh anh, phần thưởng tốt hơn).", {"outcomes": [O("battle", id="ember_foreman")]}),
    ("Sneak around (60%: free; else lose 15% HP).", "Lẻn qua (60%: thoát; nếu không mất 15% máu).", {"success": 0.6, "outcomes": [O("gold", value=10)], "fail": [O("damage", value=15)]})], biome="ember_foundry")
ev("overgrown_shrine", ("Overgrown Shrine", "Đền Thờ Rêu Phủ"), ("Vines cradle a shrine to a forgotten harvest god.", "Dây leo ôm lấy ngôi đền thờ một vị thần mùa màng bị lãng quên."),
   [("Pray (random blessing choice).", "Cầu nguyện (chọn một phước lành).", {"outcomes": [O("gainBlessing")]}),
    ("Take the offerings (+40 gold, curse).", "Lấy đồ cúng (+40 vàng, nhận nguyền).", {"outcomes": [O("gold", value=40), O("gainCurse")]}), leave], biome="verdant_ruins")
ev("spore_cloud", ("Spore Cloud", "Đám Mây Bào Tử"), ("A drifting cloud of spores glows faintly green.", "Một đám bào tử trôi lơ lửng phát sáng xanh mờ."),
   [("Breathe deep (+8 max HP, lose 10% HP).", "Hít sâu (+8 máu tối đa, mất 10% máu).", {"outcomes": [O("maxHp", value=8), O("damage", value=10)]}),
    ("Harvest them (venom core).", "Thu hoạch (nhận lõi Độc).", {"outcomes": [O("gainItem", category="core", id="venom")]})], biome="verdant_ruins")
ev("frozen_courier", ("Frozen Courier", "Người Đưa Tin Đóng Băng"), ("A courier lies frozen, satchel still clutched.", "Một người đưa tin bị đóng băng, tay vẫn ôm chặt túi."),
   [("Thaw the satchel (random modifier).", "Làm tan túi (nhận modifier ngẫu nhiên).", {"outcomes": [O("gainRandomItem", category="modifier", minRarity="uncommon")]}),
    ("Take the coin purse (+35 gold).", "Lấy túi tiền (+35 vàng).", {"outcomes": [O("gold", value=35)]})], biome="frostspire", minCh=2)
ev("ice_mirror", ("The Ice Mirror", "Gương Băng"), ("Your reflection in the ice holds a different weapon.", "Hình phản chiếu trong băng cầm một vũ khí khác."),
   [("Reach in (random frame).", "Thò tay vào (nhận Frame ngẫu nhiên).", {"outcomes": [O("gainRandomItem", category="frame", minRarity="uncommon")]}),
    ("Shatter it (gain 1 Essence, 10% damage).", "Đập vỡ (nhận 1 Tinh Chất, mất 10% máu).", {"outcomes": [O("essence", value=1), O("damage", value=10)]}), leave], biome="frostspire", minCh=2)
ev("bone_merchant", ("The Bone Merchant", "Thương Nhân Xương"), ("A skeletal merchant offers discounts — for a price.", "Một thương nhân bộ xương đề nghị giảm giá — với một cái giá."),
   [("Accept (next shop 40% off, curse).", "Đồng ý (cửa hàng tới giảm 40%, nhận nguyền).", {"outcomes": [O("shopDiscount", value=400), O("gainCurse")]}),
    ("Buy a relic (50 gold: random rare relay/support).", "Mua di vật (50 vàng: mảnh board hiếm).", {"requireGold": 50, "outcomes": [O("gainRandomItem", category="piece", minRarity="rare")]}), leave], biome="sunken_crypt", minCh=2)
ev("restless_dead", ("Restless Dead", "Vong Hồn Không Yên"), ("Spirits beg you to lay their bones to rest.", "Những vong hồn cầu xin bạn an táng xương cốt của họ."),
   [("Help them (remove a curse).", "Giúp đỡ (gỡ một lời nguyền).", {"requireTag": "has_curse", "outcomes": [O("removeCurse")]}),
    ("Plunder the tomb (+60 gold, crypt elite fight).", "Cướp mộ (+60 vàng, đánh tinh anh).", {"outcomes": [O("gold", value=60), O("battle", id="crypt_colossus")]}), leave], biome="sunken_crypt", minCh=2)
ev("storm_conduit", ("Storm Conduit", "Ống Dẫn Bão"), ("Lightning arcs between two pylons, pulsing with stored power.", "Tia sét giật giữa hai cột trụ, đập nhịp năng lượng tích trữ."),
   [("Channel it (lightning core + 10% damage).", "Hấp thụ (nhận lõi Sét, mất 10% máu).", {"outcomes": [O("gainItem", category="core", id="lightning"), O("damage", value=10)]}),
    ("Salvage the pylon (random relay).", "Tháo dỡ cột (nhận relay ngẫu nhiên).", {"outcomes": [O("gainRandomItem", category="piece", minRarity="uncommon")]})], biome="storm_citadel", minCh=3)
ev("clockwork_vault", ("Clockwork Vault", "Kho Bánh Răng"), ("A vault ticks. Its lock resets every few seconds.", "Một kho tích tắc. Ổ khoá tự đặt lại mỗi vài giây."),
   [("Crack it (55%: 2 Essence; else 20% damage).", "Bẻ khoá (55%: 2 Tinh Chất; nếu không mất 20% máu).", {"success": 0.55, "outcomes": [O("essence", value=2)], "fail": [O("damage", value=20)]}),
    ("Pay a locksmith (60 gold: 2 Essence).", "Thuê thợ khoá (60 vàng: 2 Tinh Chất).", {"requireGold": 60, "outcomes": [O("essence", value=2)]}), leave], biome="storm_citadel", minCh=3)
# generic events (any biome)
ev("forge_spirit", ("Forge Spirit", "Hồn Lò Rèn"), ("A spirit of the forge offers to widen your workbench.", "Hồn lò rèn đề nghị nới rộng bàn rèn của bạn."),
   [("Accept (+1 board size, lose 15% HP).", "Chấp nhận (+1 kích thước board, mất 15% máu).", {"outcomes": [O("expandBoard"), O("damage", value=15)]}),
    ("Ask for knowledge (random rune).", "Xin tri thức (nhận rune ngẫu nhiên).", {"outcomes": [O("gainRandomItem", category="rune", minRarity="uncommon")]})])
ev("gambler", ("The Gambler", "Kẻ Cờ Bạc"), ("A grinning gambler shuffles cards: double or nothing.", "Một kẻ cờ bạc cười nhăn nhở xáo bài: gấp đôi hoặc mất trắng."),
   [("Bet 30 gold (50%: win 70).", "Cược 30 vàng (50%: thắng 70).", {"requireGold": 30, "success": 0.5, "outcomes": [O("gold", value=70)], "fail": [O("nothing")]}),
    ("Bet your blood (50%: 2 Essence; else 20% damage).", "Cược bằng máu (50%: 2 Tinh Chất; nếu không mất 20% máu).", {"success": 0.5, "outcomes": [O("essence", value=2)], "fail": [O("damage", value=20)]}), leave])
ev("abandoned_camp", ("Abandoned Camp", "Trại Bỏ Hoang"), ("A cold campfire and scattered supplies.", "Đống lửa nguội lạnh và đồ tiếp tế vương vãi."),
   [("Rest a while (heal 20%).", "Nghỉ một lát (hồi 20% máu).", {"outcomes": [O("heal", value=20)]}),
    ("Search the packs (random item).", "Lục balo (nhận vật phẩm ngẫu nhiên).", {"outcomes": [O("gainRandomItem", category="modifier")]})])
ev("rune_scholar", ("Rune Scholar", "Học Giả Rune"), ("A scholar studies your weapons with great interest.", "Một học giả chăm chú nghiên cứu vũ khí của bạn."),
   [("Let them engrave (random rare rune).", "Để họ khắc (rune hiếm ngẫu nhiên).", {"requireGold": 40, "outcomes": [O("gainRandomItem", category="rune", minRarity="rare")]}),
    ("Sell your notes (+30 gold).", "Bán ghi chép (+30 vàng).", {"outcomes": [O("gold", value=30)]}), leave])
ev("mysterious_crate", ("Mysterious Crate", "Thùng Bí Ẩn"), ("A sealed crate marked with a forgemaster's sigil.", "Một thùng niêm phong mang ấn của một thợ rèn bậc thầy."),
   [("Open it (random frame).", "Mở ra (nhận Frame ngẫu nhiên).", {"outcomes": [O("gainRandomItem", category="frame")]}),
    ("Sell it unopened (+45 gold).", "Bán nguyên thùng (+45 vàng).", {"outcomes": [O("gold", value=45)]})])
ev("blood_altar", ("Blood Altar", "Tế Đàn Máu"), ("An altar asks for blood in exchange for strength.", "Một tế đàn đòi máu để đổi lấy sức mạnh."),
   [("Offer 25% HP (gain a blessing choice).", "Hiến 25% máu (chọn một phước lành).", {"outcomes": [O("damage", value=25), O("gainBlessing")]}),
    ("Desecrate it (+1 Essence, curse).", "Báng bổ (+1 Tinh Chất, nhận nguyền).", {"outcomes": [O("essence", value=1), O("gainCurse")]}), leave])
ev("lost_apprentice", ("Lost Apprentice", "Người Học Việc Lạc Đường"), ("A young apprentice asks to be escorted home.", "Một người học việc trẻ nhờ bạn hộ tống về nhà."),
   [("Escort them (heal 10%, +25 gold).", "Hộ tống (hồi 10% máu, +25 vàng).", {"outcomes": [O("heal", value=10), O("gold", value=25)]}),
    ("Take their toolkit (random support piece, curse).", "Lấy bộ đồ nghề (mảnh hỗ trợ, nhận nguyền).", {"outcomes": [O("gainRandomItem", category="piece"), O("gainCurse")]})])
ev("whispering_blade", ("Whispering Blade", "Lưỡi Kiếm Thì Thầm"), ("A blade embedded in stone whispers promises.", "Một lưỡi kiếm cắm trong đá thì thầm những lời hứa."),
   [("Pull it free (random rare modifier, 15% damage).", "Rút ra (modifier hiếm, mất 15% máu).", {"outcomes": [O("gainRandomItem", category="modifier", minRarity="rare"), O("damage", value=15)]}),
    ("Ignore the whispers.", "Phớt lờ.", {"outcomes": [O("nothing")]})])
ev("traveling_bazaar", ("Traveling Bazaar", "Phiên Chợ Lưu Động"), ("Merchants set up stalls; bargains abound for the patient.", "Thương nhân dựng sạp; nhiều món hời cho người kiên nhẫn."),
   [("Haggle (next shop 25% off).", "Mặc cả (cửa hàng tới giảm 25%).", {"outcomes": [O("shopDiscount", value=250)]}),
    ("Sell spare parts (lose a random item, +55 gold).", "Bán đồ thừa (mất 1 vật phẩm ngẫu nhiên, +55 vàng).", {"outcomes": [O("loseItem"), O("gold", value=55)]})])
ev("ancient_forge", ("Ancient Forge", "Lò Rèn Cổ"), ("A forge older than kingdoms still burns with blue fire.", "Một lò rèn cổ hơn cả các vương quốc vẫn cháy lửa xanh."),
   [("Temper a weapon (free tier upgrade).", "Tôi luyện vũ khí (nâng bậc miễn phí).", {"requireTag": "weapon_upgradable", "outcomes": [O("upgradeWeapon")]}),
    ("Draw essence from the flames (+1 Essence).", "Chiết tinh chất từ lửa (+1 Tinh Chất).", {"outcomes": [O("essence", value=1)]})], minCh=2)
ev("hermit_alchemist", ("Hermit Alchemist", "Nhà Giả Kim Ẩn Dật"), ("A hermit brews potions in a hollow tree.", "Một ẩn sĩ nấu thuốc trong thân cây rỗng."),
   [("Drink the red draught (+10 max HP).", "Uống thuốc đỏ (+10 máu tối đa).", {"outcomes": [O("maxHp", value=10)]}),
    ("Drink the black draught (60%: blessing; else curse).", "Uống thuốc đen (60%: phước lành; nếu không: nguyền).", {"success": 0.6, "outcomes": [O("gainBlessing")], "fail": [O("gainCurse")]}), leave])
ev("shrine_of_steel", ("Shrine of Steel", "Đền Thép"), ("Blades of fallen warriors ring softly in the wind.", "Lưỡi kiếm của chiến binh tử trận ngân nhẹ trong gió."),
   [("Honor them (steel core).", "Tôn vinh (nhận lõi Thép).", {"outcomes": [O("gainItem", category="core", id="steel")]}),
    ("Take a blade (random blade modifier: serrated).", "Lấy một lưỡi (modifier Răng Cưa).", {"outcomes": [O("gainItem", category="modifier", id="serrated")]})])
ev("eclipse_omen", ("Eclipse Omen", "Điềm Nhật Thực"), ("The sun darkens; light and shadow trade places.", "Mặt trời tối sầm; ánh sáng và bóng tối hoán đổi."),
   [("Embrace light (solar core).", "Đón ánh sáng (lõi Mặt Trời).", {"outcomes": [O("gainItem", category="core", id="solar")]}),
    ("Embrace shadow (shadow core).", "Đón bóng tối (lõi Bóng Tối).", {"outcomes": [O("gainItem", category="core", id="shadow")]})], minCh=2)
ev("gale_crossing", ("Gale Crossing", "Vượt Gió Lốc"), ("A rope bridge sways in a howling gale.", "Cây cầu dây chao đảo trong cơn gió gào thét."),
   [("Cross quickly (70%: wind core; else 15% damage).", "Vượt nhanh (70%: lõi Gió; nếu không mất 15% máu).", {"success": 0.7, "outcomes": [O("gainItem", category="core", id="wind")], "fail": [O("damage", value=15)]}),
    ("Take the long way (heal 5%).", "Đi đường vòng (hồi 5% máu).", {"outcomes": [O("heal", value=5)]})])
ev("void_rift", ("Void Rift", "Khe Nứt Hư Không"), ("Reality tears; something stares back.", "Thực tại rách toạc; có thứ gì đó nhìn lại bạn."),
   [("Reach in (void core, curse).", "Thò tay vào (lõi Hư Không, nhận nguyền).", {"outcomes": [O("gainItem", category="core", id="void"), O("gainCurse")]}),
    ("Seal it (+1 Essence).", "Phong ấn (+1 Tinh Chất).", {"outcomes": [O("essence", value=1)]})], minCh=2)
ev("training_dummy", ("Old Training Yard", "Sân Tập Cũ"), ("Battered dummies stand in neat rows.", "Những hình nộm tơi tả đứng thẳng hàng."),
   [("Practice (temper a weapon, lose 10% HP).", "Luyện tập (nâng bậc vũ khí, mất 10% máu).", {"requireTag": "weapon_upgradable", "outcomes": [O("upgradeWeapon"), O("damage", value=10)]}),
    ("Scavenge (random relay).", "Nhặt nhạnh (relay ngẫu nhiên).", {"outcomes": [O("gainRandomItem", category="piece")]})])
ev("merchant_caravan", ("Merchant Caravan", "Đoàn Thương Buôn"), ("A caravan seeks guards for the road ahead.", "Một đoàn thương buôn cần vệ binh cho chặng đường."),
   [("Guard them (battle, +50 gold).", "Hộ tống (chiến đấu, +50 vàng).", {"outcomes": [O("gold", value=50), O("battle")]}),
    ("Trade (+1 random rune for 35 gold).", "Giao thương (35 vàng: 1 rune).", {"requireGold": 35, "outcomes": [O("gainRandomItem", category="rune")]}), leave])
ev("well_of_echoes", ("Well of Echoes", "Giếng Tiếng Vọng"), ("Your voice returns twice as loud.", "Tiếng nói của bạn vọng lại to gấp đôi."),
   [("Shout a war cry (echo relay).", "Hô chiến (nhận Echo Relay).", {"outcomes": [O("gainItem", category="piece", id="echo_relay")]}),
    ("Whisper a secret (random blessing choice, 50%).", "Thì thầm bí mật (50%: phước lành).", {"success": 0.5, "outcomes": [O("gainBlessing")], "fail": [O("nothing")]})])
ev("broken_golem", ("Broken Golem", "Golem Hỏng"), ("A broken golem's core still glows.", "Lõi của một golem hỏng vẫn còn phát sáng."),
   [("Extract the core (earth core).", "Lấy lõi (lõi Đất).", {"outcomes": [O("gainItem", category="core", id="earth")]}),
    ("Salvage plating (heavy anvil support).", "Tháo giáp (mảnh Đe Nặng).", {"outcomes": [O("gainItem", category="piece", id="heavy_anvil")]})])
ev("healing_grove", ("Healing Grove", "Lùm Cây Chữa Lành"), ("Soft light filters through ancient leaves.", "Ánh sáng dịu lọc qua tán lá cổ thụ."),
   [("Rest (heal 35%).", "Nghỉ ngơi (hồi 35% máu).", {"outcomes": [O("heal", value=35)]}),
    ("Gather sap (life core).", "Lấy nhựa (lõi Sự Sống).", {"outcomes": [O("gainItem", category="core", id="life")]})])
ev("arcane_library", ("Arcane Library", "Thư Viện Bí Thuật"), ("Shelves of spellbooks hover in the air.", "Những kệ sách phép lơ lửng trên không."),
   [("Study (arcane core).", "Nghiên cứu (lõi Bí Thuật).", {"outcomes": [O("gainItem", category="core", id="arcane")]}),
    ("Steal a tome (tome frame, curse).", "Trộm sách (Frame Sách Phép, nhận nguyền).", {"outcomes": [O("gainItem", category="frame", id="tome"), O("gainCurse")]})], minCh=2)
ev("smugglers_den", ("Smuggler's Den", "Hang Buôn Lậu"), ("Smugglers sell contraband relays, no questions asked.", "Bọn buôn lậu bán relay lậu, không hỏi han gì."),
   [("Buy contraband (40 gold: random rare piece).", "Mua hàng lậu (40 vàng: mảnh hiếm).", {"requireGold": 40, "outcomes": [O("gainRandomItem", category="piece", minRarity="rare")]}),
    ("Report them (+20 gold).", "Tố giác (+20 vàng).", {"outcomes": [O("gold", value=20)]}), leave])
ev("starfall_crater", ("Starfall Crater", "Hố Sao Rơi"), ("A fallen star still radiates warmth.", "Một ngôi sao rơi vẫn toả hơi ấm."),
   [("Mine star-metal (+2 Essence, 15% damage).", "Khai thác thiên thạch (+2 Tinh Chất, mất 15% máu).", {"outcomes": [O("essence", value=2), O("damage", value=15)]}),
    ("Bask in its light (heal 15%).", "Tắm ánh sáng (hồi 15% máu).", {"outcomes": [O("heal", value=15)]})], minCh=2)
ev("weapon_rack", ("Abandoned Armory", "Kho Vũ Khí Bỏ Hoang"), ("Racks of weapons, most rusted beyond use.", "Những giá vũ khí, phần lớn đã gỉ sét."),
   [("Search thoroughly (random frame, 10% damage).", "Lục kỹ (Frame ngẫu nhiên, mất 10% máu).", {"outcomes": [O("gainRandomItem", category="frame"), O("damage", value=10)]}),
    ("Grab the whetstone (whetstone support).", "Lấy đá mài (mảnh Đá Mài).", {"outcomes": [O("gainItem", category="piece", id="whetstone")]})])
ev("duelist_challenge", ("A Duelist's Challenge", "Lời Thách Đấu"), ("A masked duelist challenges you to single combat.", "Một kiếm khách đeo mặt nạ thách bạn đấu tay đôi."),
   [("Accept (elite fight, then a blessing choice).", "Nhận lời (đánh tinh anh, rồi chọn phước lành).", {"outcomes": [O("battle"), O("gainBlessing")]}),
    ("Decline politely.", "Từ chối lịch sự.", {"outcomes": [O("nothing")]})])
ev("tinkers_gift", ("Tinker's Gift", "Quà Của Thợ Máy"), ("A tinker trades gadgets for interesting stories.", "Một thợ máy đổi đồ chơi cơ khí lấy những câu chuyện thú vị."),
   [("Tell of your victories (splitter relay).", "Kể chiến công (relay Chia Nhánh).", {"outcomes": [O("gainItem", category="piece", id="splitter")]}),
    ("Tell of your defeats (amplifier relay, 10% damage).", "Kể thất bại (relay Khuếch Đại, mất 10% máu).", {"outcomes": [O("gainItem", category="piece", id="amplifier"), O("damage", value=10)]})])
ev("fortune_teller", ("Fortune Teller", "Thầy Bói"), ("She reads the path ahead in molten glass.", "Bà đọc con đường phía trước trong thuỷ tinh nóng chảy."),
   [("Pay 25 gold (reveal routes + heal 10%).", "Trả 25 vàng (soi đường + hồi 10% máu).", {"requireGold": 25, "outcomes": [O("revealMap"), O("heal", value=10)]}),
    ("Ask about your foes (random rune).", "Hỏi về kẻ thù (rune ngẫu nhiên).", {"outcomes": [O("gainRandomItem", category="rune")]})])
ev("sacrificial_forge", ("Sacrificial Forge", "Lò Hiến Tế"), ("Feed it an item; it returns something stranger.", "Cho nó một vật phẩm; nó trả lại thứ kỳ lạ hơn."),
   [("Sacrifice an item (lose random item, gain random rare modifier).", "Hiến một vật phẩm (mất ngẫu nhiên, nhận modifier hiếm).", {"outcomes": [O("loseItem"), O("gainRandomItem", category="modifier", minRarity="rare")]}),
    ("Sacrifice vitality (-8 max HP, +2 Essence).", "Hiến sinh lực (-8 máu tối đa, +2 Tinh Chất).", {"outcomes": [O("maxHp", value=-8), O("essence", value=2)]}), leave], minCh=2)
ev("ghostly_smith", ("Ghostly Smith", "Hồn Ma Thợ Rèn"), ("A ghost hammers at an anvil that isn't there.", "Một hồn ma gõ búa trên chiếc đe không tồn tại."),
   [("Help finish its work (+1 Essence, temper a weapon).", "Giúp hoàn thành (+1 Tinh Chất, nâng bậc vũ khí).", {"requireTag": "weapon_upgradable", "outcomes": [O("essence", value=1), O("upgradeWeapon")]}),
    ("Banish it (+30 gold).", "Trục xuất (+30 vàng).", {"outcomes": [O("gold", value=30)]})], minCh=2)
ev("crystal_cavern", ("Crystal Cavern", "Hang Pha Lê"), ("Crystals hum in resonance with your relays.", "Pha lê ngân vang cộng hưởng với relay của bạn."),
   [("Harvest a resonator (resonator support).", "Thu cộng hưởng khí (mảnh Cộng Hưởng).", {"outcomes": [O("gainItem", category="piece", id="resonator")]}),
    ("Mine crystals (+40 gold, 10% damage).", "Đào pha lê (+40 vàng, mất 10% máu).", {"outcomes": [O("gold", value=40), O("damage", value=10)]})], minCh=2)
ev("mercenary_offer", ("Mercenary Offer", "Lính Đánh Thuê"), ("A mercenary offers to share battle tricks.", "Một lính đánh thuê đề nghị chia sẻ mẹo chiến đấu."),
   [("Pay 50 gold (blessing choice).", "Trả 50 vàng (chọn phước lành).", {"requireGold": 50, "outcomes": [O("gainBlessing")]}),
    ("Spar (lose 15% HP, random rare rune).", "Đấu tập (mất 15% máu, rune hiếm).", {"outcomes": [O("damage", value=15), O("gainRandomItem", category="rune", minRarity="rare")]}), leave])
ev("purifying_flame", ("Purifying Flame", "Ngọn Lửa Thanh Tẩy"), ("A white flame burns away corruption — and some flesh.", "Ngọn lửa trắng thiêu sạch ô uế — và một ít da thịt."),
   [("Step in (remove a curse, 20% damage).", "Bước vào (gỡ nguyền, mất 20% máu).", {"requireTag": "has_curse", "outcomes": [O("removeCurse"), O("damage", value=20)]}),
    ("Warm your hands (heal 10%).", "Hơ tay (hồi 10% máu).", {"outcomes": [O("heal", value=10)]})])
json.dump({"version": 1, "items": EV}, open(os.path.join(OUT, "events.json"), "w"), indent=1, ensure_ascii=False)
json.dump(LOC_EN, open(os.path.join(os.path.dirname(__file__), "loc_events_en.json"), "w"), indent=1, ensure_ascii=False)
json.dump(LOC_VI, open(os.path.join(os.path.dirname(__file__), "loc_events_vi.json"), "w"), indent=1, ensure_ascii=False)
print(len(EV))
