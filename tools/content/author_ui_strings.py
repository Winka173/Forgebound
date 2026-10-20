"""UI string tables (Resources/Localization/ui_en.json, ui_vi.json). Content/tooltip strings live in en.json/vi.json.
Re-run after adding UI keys; `forgebound loc-check` verifies coverage of every key referenced by Presentation code."""
import json, os, re

ROOT = os.path.join(os.path.dirname(__file__), "..", "..")
OUT = os.path.join(ROOT, "Assets", "Forgebound", "Resources", "Localization")
S = {}

def k(key, en, vi):
    S[key] = (en, vi)

# --- general
k("ui.back", "Back", "Quay lại"); k("ui.continue", "Continue", "Tiếp tục"); k("ui.leave", "Leave", "Rời đi")
k("ui.yes", "Yes", "Có"); k("ui.no", "No", "Không"); k("ui.on", "On", "Bật"); k("ui.off", "Off", "Tắt")
# --- title
k("ui.title.tagline", "Forge weapons. Connect them. Watch them fight.", "Rèn vũ khí. Kết nối chúng. Xem chúng chiến đấu.")
k("ui.title.continue", "Continue Run", "Tiếp tục hành trình"); k("ui.title.new_run", "New Adventure", "Hành trình mới")
k("ui.title.workshop", "Collection Workshop", "Xưởng Sưu Tầm"); k("ui.title.codex", "Codex", "Bách khoa")
k("ui.title.settings", "Settings", "Cài đặt"); k("ui.title.quit", "Quit", "Thoát")
k("ui.title.stats", "Runs {0} • Wins {1}", "Lượt chơi {0} • Thắng {1}")
k("ui.title.resume_failed", "The saved run could not be restored.", "Không thể khôi phục lượt chơi đã lưu.")
k("ui.title.warnings", "{0} startup warning(s) — see log.", "{0} cảnh báo khi khởi động — xem nhật ký.")
# --- forgemaster select
k("ui.select.title", "Choose your Forgemaster", "Chọn Thợ Rèn Bậc Thầy")
k("ui.select.seed_placeholder", "Seed (optional)", "Hạt giống (tùy chọn)")
k("ui.select.start", "Begin", "Bắt đầu"); k("ui.select.difficulty", "Difficulty: {0}", "Độ khó: {0}")
k("ui.select.starts_with", "Starts with:", "Khởi đầu với:"); k("ui.select.locked", "This Forgemaster is still locked.", "Thợ rèn này vẫn còn bị khóa.")
for d, en, vi in [("story", "Story", "Cốt truyện"), ("normal", "Normal", "Thường"), ("hard", "Hard", "Khó"), ("forgemaster", "Forgemaster", "Bậc Thầy")]:
    k("difficulty." + d, en, vi)
for c, en, vi in [("win_any", "Win any run", "Thắng một lượt bất kỳ"), ("reach_chapter_3", "Reach chapter 3", "Đến chương 3"),
                  ("elites_10", "Defeat 10 elites", "Hạ 10 tinh anh"), ("craft_legendary", "Forge a Legendary", "Rèn một vũ khí Huyền Thoại")]:
    k("ui.unlock." + c, "Unlock: " + en, "Mở khóa: " + vi)
# --- settings
k("ui.settings.title", "Settings", "Cài đặt"); k("ui.settings.language", "Language", "Ngôn ngữ")
k("ui.settings.text_scale", "Text size", "Cỡ chữ"); k("ui.settings.reduced_flashes", "Reduce flashes", "Giảm nhấp nháy")
k("ui.settings.shake", "Screen shake", "Rung màn hình"); k("ui.settings.colorblind", "Colour-blind palette", "Bảng màu mù màu")
k("ui.settings.speed", "Default battle speed", "Tốc độ trận mặc định"); k("ui.settings.particles", "Effect density", "Mật độ hiệu ứng")
k("ui.settings.fps30", "Battery saver (30 FPS)", "Tiết kiệm pin (30 FPS)"); k("ui.settings.hints", "Tutorial hints", "Gợi ý hướng dẫn")
k("ui.settings.skip_forge", "Skip forge animations", "Bỏ qua hoạt cảnh rèn"); k("ui.settings.master", "Master volume", "Âm lượng tổng")
k("ui.settings.music", "Music", "Nhạc nền"); k("ui.settings.sfx", "Sound effects", "Hiệu ứng âm thanh")
for m, en, vi in [("none", "Off", "Tắt"), ("protan", "Protanopia", "Mù màu đỏ"), ("deutan", "Deuteranopia", "Mù màu lục"), ("tritan", "Tritanopia", "Mù màu lam")]:
    k("ui.settings.cb." + m, en, vi)
# --- map / nodes
k("ui.map.chapter", "Chapter {0}/{1} — {2}", "Chương {0}/{1} — {2}")
k("ui.map.pick_hint", "Pick your next stop. Hover or tap a node to preview it.", "Chọn điểm đến tiếp theo. Rê chuột hoặc chạm để xem trước.")
k("ui.map.tap_again", "Tap again to travel here.", "Chạm lần nữa để đi tới đây.")
k("ui.map.loadout", "Loadout", "Trang bị"); k("ui.map.menu", "Menu", "Menu")
k("ui.map.style", "Encounter: {0}", "Kiểu trận: {0}"); k("ui.map.waves", "Waves: {0}", "Số đợt: {0}")
nodes = {"Battle": ("Battle", "Trận chiến", "A standard fight.", "Một trận đấu thường.", "Rewards: gold + one item choice.", "Thưởng: vàng + chọn một vật phẩm.", "⚔"),
         "Elite": ("Elite", "Tinh anh", "A dangerous foe with escorts.", "Kẻ thù nguy hiểm kèm hộ vệ.", "Rewards: more gold, Forge Essence, better items or a blessing.", "Thưởng: nhiều vàng, Tinh Chất Rèn, vật phẩm tốt hơn hoặc phước lành.", "☠"),
         "Boss": ("Boss", "Trùm", "The chapter's guardian. Read its telegraphs.", "Kẻ canh giữ chương. Hãy đọc các dấu hiệu tấn công.", "Rewards: blessing choice, 2 Essence, partial heal.", "Thưởng: chọn phước lành, 2 Tinh Chất, hồi một phần máu.", "♛"),
         "Shop": ("Merchant", "Thương nhân", "Buy, sell, reroll, lock offers or temper a weapon.", "Mua, bán, đổi hàng, giữ món hoặc tôi luyện vũ khí.", "Spend gold to shape your build.", "Tiêu vàng để định hình lối chơi.", "$"),
         "Forge": ("Forge", "Lò rèn", "One free upgrade: temper, expand the board, or evolve a Legendary.", "Một lần nâng cấp miễn phí: tôi luyện, mở rộng bàn rèn hoặc tiến hóa Huyền Thoại.", "Legendary evolution happens here.", "Tiến hóa Huyền Thoại diễn ra tại đây.", "⚒"),
         "Rest": ("Rest", "Nghỉ ngơi", "Heal or temper a weapon.", "Hồi máu hoặc tôi luyện vũ khí.", "Recover before the next fight.", "Hồi phục trước trận tiếp theo.", "♨"),
         "Event": ("Event", "Sự kiện", "Something unusual. Choices have consequences.", "Điều bất thường. Lựa chọn có hậu quả.", "Outcomes vary.", "Kết quả khác nhau.", "?"),
         "Treasure": ("Treasure", "Kho báu", "An unguarded cache.", "Một kho đồ không ai canh giữ.", "Rewards: a better item choice.", "Thưởng: chọn vật phẩm tốt hơn.", "◆")}
for t, (en, vi, den, dvi, ren, rvi, glyph) in nodes.items():
    k("ui.node." + t, en, vi); k("ui.node." + t + ".desc", den, dvi); k("ui.node." + t + ".reward", ren, rvi); k("ui.node.glyph." + t, glyph, glyph)
for st, en, vi in [("swarm", "Swarm", "Bầy đàn"), ("mixed", "Mixed", "Hỗn hợp"), ("armor", "Armored", "Bọc giáp"), ("support", "Supported", "Có hỗ trợ"),
                   ("flyers", "Flyers", "Kẻ bay"), ("ranged", "Ranged", "Tầm xa"), ("elite", "Elite duel", "Đấu tinh anh"), ("boss", "Boss", "Trùm"),
                   ("healer", "Healers", "Hồi máu"), ("stealth", "Stealth", "Tàng hình"), ("splitter", "Splitters", "Tách đôi"), ("bruiser", "Bruisers", "Đấu sĩ"),
                   ("summoner", "Summoners", "Triệu hồi"), ("reflect", "Reflectors", "Phản đòn"), ("training", "Training", "Luyện tập")]:
    k("style." + st, en, vi)
for r, en, vi in [("Minion", "Minion", "Lính"), ("Elite", "Elite", "Tinh anh"), ("Boss", "Boss", "Trùm")]:
    k("rank." + r, en, vi)
# --- HUD
k("ui.hud.hp", "HP {0}/{1}", "Máu {0}/{1}"); k("ui.hud.gold", "{0} gold", "{0} vàng"); k("ui.hud.essence", "{0} Essence", "{0} Tinh Chất")
k("ui.hud.step", "Step {0}/{1}", "Bước {0}/{1}"); k("ui.hud.boons", "Boons {0} • Curses {1}", "Phước {0} • Nguyền {1}")
k("ui.hud.hp_tip", "Health carries over between fights. Rest, bosses and some items heal.", "Máu được giữ giữa các trận. Nghỉ ngơi, trùm và một số vật phẩm giúp hồi máu.")
k("ui.hud.gold_tip", "Gold buys items, rerolls and tempering.", "Vàng dùng để mua đồ, đổi hàng và tôi luyện.")
k("ui.hud.essence_tip", "Forge Essence evolves weapons into Legendaries at a Forge.", "Tinh Chất Rèn dùng để tiến hóa vũ khí thành Huyền Thoại tại Lò rèn.")
k("ui.hud.step_tip", "3 chapters × 5 steps. Each chapter ends with a boss.", "3 chương × 5 bước. Mỗi chương kết thúc bằng một trùm.")
k("ui.hud.no_boons", "No blessings or curses yet.", "Chưa có phước lành hay lời nguyền.")
# --- pause
k("ui.pause.resume", "Resume", "Tiếp tục"); k("ui.pause.seed", "Seed:", "Hạt giống:"); k("ui.pause.seed_copied", "Seed copied.", "Đã sao chép hạt giống.")
k("ui.pause.save_quit", "Save & quit to title", "Lưu và về màn hình chính"); k("ui.pause.abandon", "Abandon run", "Bỏ lượt chơi")
k("ui.pause.abandon_confirm", "Abandon this run? Progress in this run is lost (discoveries are kept).", "Bỏ lượt chơi này? Tiến trình lượt chơi sẽ mất (khám phá vẫn được giữ).")
# --- forge screen
for ph, en, vi in [("PreBattle", "Prepare for battle", "Chuẩn bị chiến đấu"), ("InShop", "Merchant", "Thương nhân"), ("InForge", "Forge", "Lò rèn"),
                   ("InRest", "Campfire", "Lửa trại"), ("Loadout", "Loadout", "Trang bị"), ("ChoosingNode", "Loadout", "Trang bị"),
                   ("InEvent", "Loadout", "Trang bị"), ("InReward", "Loadout", "Trang bị"), ("Finished", "Loadout", "Trang bị")]:
    k("ui.forge.title." + ph, en, vi)
k("ui.forge.inventory", "Inventory {0}/{1}", "Túi đồ {0}/{1}"); k("ui.forge.bench", "(bench)", "(dự bị)")
k("ui.forge.tap_cell", "Tap a board cell to place it (R / Rotate to turn).", "Chạm vào ô trên bàn rèn để đặt (R / Xoay để xoay).")
k("ui.forge.sell_value", "Sells for {0} gold", "Bán được {0} vàng")
k("ui.forge.no_weapon", "Select a weapon on the board or bench.", "Chọn một vũ khí trên bàn rèn hoặc ô dự bị.")
k("ui.forge.tier", "Tier {0}", "Bậc {0}"); k("ui.forge.primary", "Primary", "Chủ lực")
k("ui.forge.spec", "Damage {0} • every {1}s • range {2} • crit {3}% • {4}", "Sát thương {0} • mỗi {1} giây • tầm {2} • chí mạng {3}% • {4}")
k("ui.forge.socket", "Socket {0}", "Gắn {0}"); k("ui.forge.assemble", "Assemble {0}", "Lắp ráp {0}")
k("ui.forge.ready_at_forge", "— ready to evolve at a Forge!", "— sẵn sàng tiến hóa tại Lò rèn!")
k("ui.forge.missing", "Missing: {0}", "Còn thiếu: {0}"); k("ui.forge.favorite", "☆ Favourite recipe", "☆ Công thức yêu thích")
k("ui.forge.unfavorite", "★ Favourite (tap to clear)", "★ Đã yêu thích (chạm để bỏ)")
k("ui.forge.make_primary", "Make primary", "Đặt làm chủ lực"); k("ui.forge.dismantle", "Dismantle", "Tháo rời")
k("ui.forge.dismantle_confirm", "Dismantle this weapon? All parts return to your inventory.", "Tháo rời vũ khí này? Mọi bộ phận trở về túi đồ.")
k("ui.forge.sell_weapon", "Sell weapon", "Bán vũ khí"); k("ui.forge.sell_confirm", "Sell this weapon and all its parts?", "Bán vũ khí này cùng mọi bộ phận?")
k("ui.forge.tap_to_remove", "Tap to remove (returns to inventory).", "Chạm để gỡ (trở về túi đồ).")
k("ui.forge.empty_slot", "empty", "trống"); k("ui.forge.targeting", "Target: {0}", "Mục tiêu: {0}")
k("ui.forge.next_fight", "Next: {0}", "Kế tiếp: {0}"); k("ui.forge.fight", "Fight!", "Chiến đấu!"); k("ui.forge.free", "free", "miễn phí")
k("ui.forge.name_core", "{0} {1}", "{1} {0}")
k("ui.weapon.name_core", "{0} {1}", "{1} {0}")
for t, en, vi in [("Nearest", "Nearest", "Gần nhất"), ("LowestHp", "Lowest HP", "Máu thấp nhất"), ("HighestThreat", "Biggest threat", "Nguy hiểm nhất"),
                  ("Cluster", "Densest group", "Đám đông nhất"), ("Marked", "Marked", "Bị đánh dấu"), ("Farthest", "Farthest", "Xa nhất")]:
    k("targeting." + t, en, vi)
for c, en, vi in [("Frame", "Frame", "Khung"), ("Core", "Core", "Lõi"), ("Modifier", "Modifier", "Biến thể"), ("Rune", "Rune", "Rune"), ("Piece", "Board piece", "Mảnh bàn rèn"), ("Weapon", "Weapon", "Vũ khí")]:
    k("category." + c, en, vi)
for r, en, vi in [("Common", "Common", "Thường"), ("Uncommon", "Uncommon", "Khá"), ("Rare", "Rare", "Hiếm"), ("Epic", "Epic", "Sử thi"), ("Legendary", "Legendary", "Huyền thoại")]:
    k("rarity." + r, en, vi)
k("slot.core", "Core", "Lõi"); k("slot.modifiers", "Modifiers", "Biến thể"); k("slot.runes", "Runes", "Rune")
for code, en, vi in [("slot_overflow", "Not enough modifier slots {0}", "Không đủ ô biến thể {0}"), ("rune_overflow", "Not enough rune slots {0}", "Không đủ ô rune {0}"),
                     ("requires_tag", "Requires: {0}", "Yêu cầu: {0}"), ("excluded_tag", "Incompatible with {0}", "Không tương thích với {0}"),
                     ("conflict", "Conflicts with another {0} modifier", "Xung đột với biến thể {0} khác"), ("duplicate", "Already socketed", "Đã gắn rồi"),
                     ("unknown", "Unknown part {0}", "Bộ phận không xác định {0}"), ("bad_tier", "Invalid tier", "Bậc không hợp lệ")]:
    k("assembly." + code, en, vi)
k("recipe.already_legendary", "Already Legendary", "Đã là Huyền Thoại")
# --- board
k("ui.board.rotate", "Rotate", "Xoay"); k("ui.board.remove", "Remove", "Gỡ"); k("ui.board.undo", "Undo", "Hoàn tác"); k("ui.board.redo", "Redo", "Làm lại")
k("ui.board.measure", "Measure", "Đo lường"); k("ui.board.dead_relays", "{0} relay(s) carry no signal", "{0} relay không có tín hiệu")
k("ui.board.idle_supports", "{0} support(s) touch no weapon", "{0} mảnh hỗ trợ không chạm vũ khí nào")
k("ui.board.capacity", "{0} link(s) over relay capacity", "{0} liên kết vượt sức chứa relay")
k("ui.board.links", "{0} active link(s)", "{0} liên kết đang hoạt động")
k("ui.board.rotation", "Rotation {0}°", "Góc xoay {0}°"); k("ui.board.cannot_rotate", "No room to rotate here.", "Không đủ chỗ để xoay ở đây.")
k("ui.measure.result", "Training dummy: {0} DPS single • {1} DPS swarm • {2} links", "Hình nộm: {0} DPS đơn • {1} DPS đám đông • {2} liên kết")
k("ui.measure.delta", "Change vs last measure: {0} DPS", "Thay đổi so với lần đo trước: {0} DPS")
k("ui.measure.explain", "Measured with the real combat simulation on passive training dummies (15–20 s, fixed seed). Real fights differ.", "Đo bằng mô phỏng chiến đấu thật trên hình nộm bất động (15–20 giây, hạt giống cố định). Trận thật sẽ khác.")
k("ui.measure.hint", "Tip: Measure before and after a change to compare.", "Mẹo: Đo trước và sau khi thay đổi để so sánh.")
k("ui.measure.stale", "Last measure: {0} DPS (board changed — measure again)", "Lần đo trước: {0} DPS (bàn rèn đã đổi — hãy đo lại)")
# --- shop / forge node / rest
k("ui.shop.sold", "Sold", "Đã bán"); k("ui.shop.lock_hint", "Lock keeps this offer through rerolls and into the next shop.", "Khóa để giữ món này qua lần đổi hàng và tới cửa hàng sau.")
k("ui.shop.temper", "Temper selected ({0}g)", "Tôi luyện vũ khí chọn ({0}v)"); k("ui.shop.reroll", "Reroll ({0}g)", "Đổi hàng ({0}v)")
k("ui.shop.sell", "Sell selected (+{0}g)", "Bán món chọn (+{0}v)"); k("ui.shop.sell_confirm", "Sell this item?", "Bán vật phẩm này?")
k("ui.forgenode.upgrade", "Temper selected ({0})", "Tôi luyện vũ khí chọn ({0})"); k("ui.forgenode.evolve", "Evolve: {0} ({1} Essence)", "Tiến hóa: {0} ({1} Tinh Chất)")
k("ui.forgenode.expand", "Expand board ({0})", "Mở rộng bàn rèn ({0})")
k("ui.rest.heal", "Rest (+{0} HP)", "Nghỉ ngơi (+{0} máu)"); k("ui.rest.temper", "Temper {0}", "Tôi luyện {0}")
# --- errors (ActionError)
for e, en, vi in [("None", "OK", "OK"), ("WrongPhase", "Not possible right now. {0}", "Không thể làm lúc này. {0}"), ("NotFound", "Not found. {0}", "Không tìm thấy. {0}"),
                  ("NotEnoughGold", "Not enough gold (need {0}).", "Không đủ vàng (cần {0})."), ("NotEnoughEssence", "Not enough Essence. {0}", "Không đủ Tinh Chất. {0}"),
                  ("Invalid", "Invalid action. {0}", "Hành động không hợp lệ. {0}"), ("InventoryFull", "Inventory is full. {0}", "Túi đồ đã đầy. {0}"),
                  ("BoardError", "Can't place there: {0}", "Không thể đặt ở đó: {0}"), ("AlreadyUsed", "Already used. {0}", "Đã sử dụng. {0}"),
                  ("Incompatible", "Incompatible: {0}", "Không tương thích: {0}"), ("RunOver", "The run is over. {0}", "Lượt chơi đã kết thúc. {0}"),
                  ("TooManyWeapons", "Only 3 weapons can be active. {0}", "Chỉ 3 vũ khí được kích hoạt. {0}"), ("Requirement", "Requirements not met: {0}", "Chưa đủ điều kiện: {0}"),
                  ("frame_locked", "That frame is not discovered yet.", "Khung này chưa được khám phá."), ("core_locked", "That core is not discovered yet.", "Lõi này chưa được khám phá.")]:
    k("error." + e, en, vi)
# --- battle
k("ui.battle.pause", "Pause", "Tạm dừng"); k("ui.battle.resume", "Resume", "Tiếp tục"); k("ui.battle.skip", "Skip", "Bỏ qua")
k("ui.battle.enemies_left", "{0} enemies left", "Còn {0} kẻ thù"); k("ui.battle.live", "{0}s • runes {1} • relay fires {2}", "{0} giây • rune {1} • relay kích {2}")
k("battle.miss", "Miss", "Trượt"); k("battle.enrage", "ENRAGED", "CUỒNG NỘ"); k("battle.wave", "Wave {0}/{1}", "Đợt {0}/{1}")
# --- report
k("ui.report.victory", "Victory", "Chiến thắng"); k("ui.report.defeat", "Defeat", "Thất bại")
k("ui.report.summary", "{0}s • {1} kills • {2} damage dealt • {3} damage taken", "{0} giây • {1} hạ gục • {2} sát thương gây ra • {3} sát thương nhận")
k("ui.report.weapons", "Weapons", "Vũ khí")
k("ui.report.weapon_line", "{0} dmg ({1}%) • hits {2} • crits {3} • procs {4} • proc dmg {5} • DoT {6} • kills {7}", "{0} st ({1}%) • trúng {2} • chí mạng {3} • kích hoạt {4} • st kích hoạt {5} • st theo thời gian {6} • hạ {7}")
k("ui.report.defense", "Shield blocked {0} • armor/resist prevented {1} • healed {2} (leech {3})", "Khiên chặn {0} • giáp/kháng giảm {1} • hồi {2} (hút máu {3})")
k("ui.report.status_uptime", "Status uptime:", "Thời gian hiệu ứng:"); k("ui.report.procs", "Runes & relays", "Rune & relay")
k("ui.report.no_procs", "No rune triggered.", "Không rune nào kích hoạt."); k("ui.report.links", "Relay fires: {0} • inactive links/pieces: {1}", "Relay kích hoạt: {0} • liên kết/mảnh không hoạt động: {1}")
k("ui.report.suppressed", "{0} procs were capped (chain depth / per-tick limit).", "{0} lần kích hoạt bị chặn (giới hạn chuỗi / mỗi nhịp).")
k("ui.report.insights", "Insights", "Phân tích"); k("ui.report.killed_by", "Defeated by: {0}", "Bị hạ bởi: {0}"); k("ui.report.innate", "Innate effect", "Hiệu ứng bẩm sinh")
# --- event / reward / end
k("ui.reward.title", "Choose a reward", "Chọn phần thưởng"); k("ui.reward.blessing_title", "Choose a blessing", "Chọn phước lành")
k("ui.reward.take", "Take", "Nhận"); k("ui.reward.skip", "Skip (+{0} gold)", "Bỏ qua (+{0} vàng)")
for o, en, vi in [("Victory", "Victory!", "Chiến thắng!"), ("Defeat", "Defeated", "Thất bại"), ("Abandoned", "Run abandoned", "Đã bỏ lượt chơi"), ("None", "Run", "Lượt chơi")]:
    k("ui.end." + o, en, vi)
k("ui.end.summary", "{0} • chapter {1} • step {2} • battles {3} • damage {4} • seed {5} • {6}", "{0} • chương {1} • bước {2} • trận {3} • sát thương {4} • hạt giống {5} • {6}")
k("ui.end.death", "Cause: {0}", "Nguyên nhân: {0}"); k("ui.end.weapons", "Final weapons", "Vũ khí cuối cùng"); k("ui.end.timeline", "Timeline", "Dòng thời gian")
k("ui.end.dmg", "{0} dmg", "{0} st"); k("ui.end.unlocked", "Unlocked", "Đã mở khóa"); k("ui.end.unlock_fm", "New Forgemaster: {0}", "Thợ rèn mới: {0}")
k("ui.end.achievement", "Achievement: {0}", "Thành tựu: {0}"); k("ui.end.retry_seed", "Retry same seed", "Chơi lại cùng hạt giống")
k("ui.end.new_run", "New run", "Lượt mới"); k("ui.end.title", "Title", "Màn hình chính")
for d, en, vi in [("hero_defeated", "Fell in battle", "Ngã xuống trong trận"), ("timeout", "Ran out of time", "Hết thời gian"), ("event", "An event went wrong", "Một sự kiện thất bại")]:
    k("death." + d, en, vi)
for a, en, vi in [("first_run", "First steps", "Những bước đầu"), ("first_victory", "Forgebound", "Thợ Rèn Chiến Thắng"), ("first_legendary", "Legend forged", "Rèn nên huyền thoại"),
                  ("hard_victory", "Tempered steel", "Thép đã tôi"), ("forgemaster_victory", "Master of the forge", "Bậc thầy lò rèn"),
                  ("all_frames", "Armory complete", "Kho vũ khí đầy đủ"), ("triple_threat", "Triple threat", "Bộ ba đáng sợ")]:
    k("achievement." + a, en, vi)
# --- hints (onboarding by doing)
for h, en, vi in [("board_intro", "This is your Forge Board. Weapons on the board fight; drag to move, R to rotate. Gold nubs are outputs, cyan nubs are inputs.", "Đây là Bàn Rèn. Vũ khí trên bàn sẽ chiến đấu; kéo để di chuyển, R để xoay. Mấu vàng là đầu ra, mấu xanh là đầu vào."),
                  ("relay_intro", "Relays carry signals from one weapon's output port to another weapon's input: connected weapons trigger extra attacks.", "Relay dẫn tín hiệu từ cổng ra của vũ khí này tới cổng vào của vũ khí khác: vũ khí được nối sẽ đánh thêm."),
                  ("core_intro", "A Core gives a weapon its element and special behaviour. Select a Core in your inventory, then Socket it.", "Lõi mang lại nguyên tố và hiệu ứng đặc biệt. Chọn một Lõi trong túi rồi bấm Gắn."),
                  ("rune_intro", "Runes react to combat events (on hit, on kill…). The battle report shows how often each rune fired — and why it didn't.", "Rune phản ứng với sự kiện chiến đấu (khi trúng, khi hạ…). Báo cáo trận cho biết rune kích hoạt bao nhiêu lần — và vì sao không."),
                  ("battle_intro", "Battles are automatic. Watch which weapon deals damage and when relays fire; adjust your build afterwards.", "Trận đấu diễn ra tự động. Quan sát vũ khí nào gây sát thương và khi nào relay kích hoạt; rồi điều chỉnh."),
                  ("legendary_intro", "Legendaries change how a weapon behaves. Their recipes are hinted in the weapon panel.", "Vũ khí Huyền Thoại thay đổi cách vũ khí hoạt động. Công thức được gợi ý trong bảng vũ khí.")]:
    k("hint." + h, en, vi)
# --- workshop / sandbox / codex
k("ui.workshop.title", "Collection Workshop", "Xưởng Sưu Tầm")
for t, en, vi in [("gallery", "Gallery", "Phòng trưng bày"), ("sandbox", "Sandbox & Training", "Thử nghiệm & Luyện tập"), ("trials", "Blueprint Trials", "Thử thách Bản vẽ"), ("history", "Run History", "Lịch sử")]:
    k("ui.workshop.tab." + t, en, vi)
k("ui.workshop.discovered", "Blueprints {0}/{1} • Legendaries {2}/{3}", "Bản vẽ {0}/{1} • Huyền thoại {2}/{3}")
k("ui.workshop.undiscovered", "Undiscovered legendary", "Huyền thoại chưa khám phá"); k("ui.workshop.designs", "Saved designs", "Thiết kế đã lưu")
k("ui.workshop.export", "Copy code", "Sao chép mã"); k("ui.workshop.exported", "Blueprint code copied.", "Đã sao chép mã bản vẽ.")
k("ui.workshop.import", "Import from clipboard", "Nhập từ bộ nhớ tạm"); k("ui.workshop.import_failed", "Import rejected: {0}", "Từ chối nhập: {0}")
k("ui.workshop.imported_name", "Imported design", "Thiết kế đã nhập"); k("ui.workshop.tier", "Tier {0}", "Cấp {0}")
k("ui.workshop.start_trial", "Start trial", "Bắt đầu thử thách")
k("ui.workshop.history_stats", "Runs {0} • wins {1} • best chapter {2} • legendaries {3}", "Lượt {0} • thắng {1} • chương xa nhất {2} • huyền thoại {3}")
k("ui.workshop.history_line", "ch.{0} step {1} • {2} battles • seed {3}", "chương {0} bước {1} • {2} trận • hạt giống {3}")
k("ui.workshop.replay_seed", "Replay seed", "Chơi lại hạt giống")
k("ui.sandbox.title", "Sandbox & Training Chamber", "Thử nghiệm & Phòng luyện tập")
k("ui.sandbox.hint", "Build with parts you have discovered, then measure against dummies. Nothing here carries into Adventure runs.", "Lắp ráp bằng bộ phận đã khám phá rồi đo trên hình nộm. Không gì ở đây được mang vào Hành trình.")
k("ui.sandbox.trial_hint", "Build with the trial's parts, then fight. Rewards are cosmetic.", "Lắp ráp bằng bộ phận của thử thách rồi chiến đấu. Phần thưởng chỉ là trang trí.")
for d, en, vi in [("training_single", "Single target", "Một mục tiêu"), ("training_swarm", "Swarm", "Bầy đàn"), ("training_armored", "Armored", "Bọc giáp"),
                  ("training_shielded", "Shielded", "Có khiên"), ("training_status", "Resistant", "Kháng nguyên tố")]:
    k("ui.sandbox.dummy." + d, en, vi)
k("ui.sandbox.save_design", "Save selected design", "Lưu thiết kế đang chọn"); k("ui.sandbox.saved", "Design saved to the gallery.", "Đã lưu thiết kế vào phòng trưng bày.")
k("ui.sandbox.fight_trial", "Fight the trial", "Chiến đấu thử thách")
k("ui.sandbox.nothing_discovered", "Play Adventure runs to discover parts.", "Hãy chơi Hành trình để khám phá bộ phận.")
k("ui.sandbox.frame_not_allowed", "This trial forbids that frame.", "Thử thách này cấm khung đó.")
k("ui.sandbox.select_weapon", "Create or select a weapon first.", "Hãy tạo hoặc chọn vũ khí trước.")
k("ui.sandbox.result", "{0}: {1} DPS • total {2} • {3} active links • {4} dead links", "{0}: {1} DPS • tổng {2} • {3} liên kết hoạt động • {4} liên kết chết")
k("ui.sandbox.cosmetic", "Reward unlocked: {0}", "Mở khóa phần thưởng: {0}")
for c, en, vi in [("frame", "Frames", "Khung"), ("core", "Cores", "Lõi"), ("modifier", "Modifiers", "Biến thể"), ("rune", "Runes", "Rune"), ("piece", "Board pieces", "Mảnh bàn rèn"),
                  ("legendary", "Legendaries", "Huyền thoại"), ("enemy", "Bestiary", "Quái vật"), ("blessing", "Boons & Curses", "Phước & Nguyền"), ("forgemaster", "Forgemasters", "Thợ rèn")]:
    k("codex." + c, en, vi)

# Cosmetics from challenges.json
cj = open(os.path.join(ROOT, "Assets", "Forgebound", "Resources", "Content", "challenges.json")).read()
cj = "\n".join(l for l in cj.splitlines() if not l.strip().startswith("//"))
for ch in json.loads(cj)["items"]:
    cid = ch.get("reward")
    if cid:
        nice = cid.replace("cosmetic_", "").replace("_", " ").title()
        k("cosmetic." + cid, nice + " (cosmetic)", nice + " (trang trí)")
# Boss phase names from enemies.json
ej = json.load(open(os.path.join(ROOT, "Assets", "Forgebound", "Resources", "Content", "enemies.json")))
roman = {"1": ("I", "I"), "2": ("II", "II"), "3": ("III", "III")}
for e in ej["items"]:
    for ph in e.get("phases", []):
        key = ph.get("name")
        if key:
            n = key.split(".")[-1]
            k(key, "Phase " + roman.get(n, (n, n))[0], "Giai đoạn " + roman.get(n, (n, n))[1])

en = {kk: v[0] for kk, v in sorted(S.items())}
vi = {kk: v[1] for kk, v in sorted(S.items())}
for lang, table in (("en", en), ("vi", vi)):
    json.dump({"language": lang, "strings": table}, open(os.path.join(OUT, "ui_" + lang + ".json"), "w"), indent=1, ensure_ascii=False)
print(len(S), "ui keys")
