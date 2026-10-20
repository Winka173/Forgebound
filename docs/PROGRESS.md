# Tiến độ Forgebound (cập nhật 2026-10-20)

Quy ước: ✅ có bằng chứng chạy thật · 🟡 đã làm nhưng chưa kiểm chứng trên nền tảng đích · ❌ chưa làm.
Không có Unity Editor trong môi trường này → mọi thứ phụ thuộc Unity đều ở trạng thái 🟡/❌ (xem docs/KNOWN_ISSUES.md).

| Phase | Trạng thái | Bằng chứng |
|---|---|---|
| 0 — Audit | ✅ | docs/AUDIT.md |
| 1 — Core Forge Prototype | ✅ lõi / 🟡 Unity | Lõi C# tất định + 78 test NUnit pass; UI Unity compile với stub, chưa chạy trong Editor |
| 2 — Combat & Rune Engineering | ✅ lõi | event queue có giới hạn proc, 3 vũ khí, stance/targeting, board 5×4→7×5, port/relay, báo cáo trận + insight |
| 3 — Adventure Run hoàn chỉnh | ✅ lõi / 🟡 Unity | 3 chương × 5 bước, shop/forge/rest/event/elite/boss, kinh tế, save/resume, replay; bot chơi trọn run |
| 4 — Collection Workshop | ✅ lõi / 🟡 Unity | profile, gallery, sandbox, training chamber (đo bằng mô phỏng thật), 8 trial, lịch sử, xuất/nhập blueprint có kiểm tra |
| 5 — Mở rộng nội dung | ✅ | 8 Forgemaster, 12 Frame, 12 Core, 45 Modifier, 52 Rune, 36 mảnh board, 42 Legendary, 52 kẻ địch (10 boss), 5 biome, 44 event, 110 phước/nguyền (35 nguyền) — validator 0 lỗi |
| 6 — Art + Audio | ✅ pipeline / 🟡 chất lượng | Blender pipeline + Art Quality Validator (0 FAIL) + 26 test; batch 2 xong: 1264 sprite + 18 atlas (35 MB) phủ mọi lookup runtime; 67 clip âm thanh gốc + validator |
| 7 — Game feel / UX | 🟡 | VFX pool, telegraph, số sát thương, link beam, âm thanh giới hạn tần suất, undo/redo, gợi ý, cài đặt trợ năng, EN/VI 0 thiếu khóa — cần kiểm chứng trong Unity |
| 8 — Balance & Stability | ✅ công cụ / 🟡 số liệu | Simulator, tournament, duel, 54/54 kịch bản adversarial PASS; Normal 38.2% (bot, 3200 run) — còn lệch Forgemaster 27–54.5% (KI-05) |
| 9 — Release Candidate | ❌ | Chưa build Windows/Android/iOS (cần Unity) — KI-01/KI-02 |

## Lệnh đã chạy & kết quả gần nhất
- `dotnet test` → 78/78 pass.
- `forgebound validate` → 0 lỗi, 16 cảnh báo (vòng proc tiềm năng, đã có chặn runtime).
- `forgebound loc-check` → EN 1363 / VI 1363 chuỗi, 0 thiếu.
- `forgebound scenarios` → 54/54 PASS.
- `forgebound sim --seeds 40` → 3200 run, thắng 38.2% (CI 35.8–40.6%); Story 80.8%, Hard 16.3%.
- `forgebound tournament --builds 600` → không thành phần nào lệch quá ±15pp.
- `art_validator.py` → 1264 file: 1208 PASS / 56 WARN / 0 FAIL (batch 2); `pytest tools/art/tests` 26/26; `audio_validator.py` → 62 clip, 0 FAIL.
- `PresentationCheck` (stub Unity API) → build thành công.

## Việc tiếp theo cụ thể
1. Mở project trong Unity 2022.3 LTS, sửa lỗi compile/runtime, chạy EditMode test, chơi trọn 1 run, chụp ảnh màn hình.
2. Build Windows + Android AAB (lệnh trong docs/BUILD.md), đo FPS trên điện thoại tầm trung.
3. Duyệt thẩm mỹ art batch 2 (mặt Forgemaster, quái bay nhỏ, nền biome phẳng — docs/ART_BIBLE.md §13).
4. Cân bằng tiếp các Forgemaster lệch (Warden, Runecrafter, Blacksmith) + playtest người thật.
