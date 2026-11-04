# Football News Bot – Spec v1

> Đặt file này ở thư mục gốc repo. Claude (hoặc dev khác) đọc file này trước khi code.

## 1. Mục tiêu
Mỗi ngày tự động tổng hợp tin bóng đá của **các ông lớn**, soạn bản nháp cho **Facebook, X, Threads**, gửi về **Telegram** để chủ trang duyệt và tự đăng. App **không** tự đăng lên mạng xã hội (v1).

## 2. Lịch chạy (giờ Việt Nam, UTC+7)
Mỗi ngày chạy **một lần lúc 08:00** (job `daily`), gửi cả hai phần dưới đây. Có thể chạy tay từng phần.

| Phần | Nội dung | Khung dữ liệu |
|---|---|---|
| `results` | Kết quả các trận đã đá | Trận **đã xong, chưa báo**, có giờ đá trong khoảng 05:00 hôm qua → 08:00 hôm nay |
| `upcoming` | Các trận sắp đá | Trận **bắt đầu** trong khoảng 08:00 hôm nay → 08:00 ngày mai |

- ESPN không có giờ kết thúc trận nên `results` lọc theo giờ đá, lùi thêm 3 giờ cho thời gian trận đấu.
- Giữa tuần thường là cúp (C1/C2/C3), cuối tuần là giải quốc nội — app không cần xử lý riêng, cứ lấy theo khung giờ.
- Không có trận ông lớn nào trong khung → **không gửi gì** (hoặc gửi 1 dòng "Hôm nay không có trận ông lớn" — cấu hình được).

## 3. Giải đấu (ESPN slug)
| Giải | Slug |
|---|---|
| Premier League | `eng.1` |
| Bundesliga | `ger.1` |
| Ligue 1 | `fra.1` |
| Champions League (C1) | `uefa.champions` |
| Europa League (C2) | `uefa.europa` |
| Conference League (C3) | `uefa.europa.conf` |

## 4. Danh sách ông lớn (config `clubs.json`, sửa tay được)
Lọc trận: giữ trận có **ít nhất 1 đội** trong danh sách. Nên match theo **ESPN team id** (ổn định hơn tên).

- **Anh:** Arsenal, Man City, Liverpool, Man United, Chelsea, Tottenham, Newcastle, Aston Villa
- **Đức:** Bayern Munich, Borussia Dortmund, RB Leipzig, Bayer Leverkusen
- **Pháp:** PSG, Marseille, Lyon, Monaco
- **Chỉ xuất hiện ở cúp châu Âu:** Real Madrid, Barcelona, Atlético Madrid, Inter, Milan, Juventus, Napoli, Benfica, Porto, Ajax

Có thể thêm `priority` (1 = top, 2 = đáng chú ý) để sắp xếp thứ tự trong bài.

## 5. Nguồn dữ liệu

### 5.1 Nguồn chính: ESPN public API (không cần key)
```
GET https://site.api.espn.com/apis/site/v2/sports/soccer/{slug}/scoreboard?dates=YYYYMMDD
```
- **Không hỗ trợ khoảng ngày** (`dates=A-B` lỗi) → gọi **từng ngày**.
- `daily`: gọi 4 ngày × 6 giải = 24 request.
- Lưu ý: tham số `dates` theo ngày của ESPN (giờ Mỹ), nên luôn lấy dư 1 ngày rồi lọc lại theo UTC trong code.
- **Không đặt User-Agent có chữ "bot":** Akamai của ESPN trả 403 cho `FootballNewsBot/1.0` (đã thử 04/11/2026). Dùng mặc định của `HttpClient`, không giả danh trình duyệt.
- Kiểm tra lại một trận đã báo: `GET .../soccer/{slug}/summary?event={id}`, đọc `header.competitions[0]`.

**Mapping trường (đã kiểm chứng với trận Liverpool 0-1 Arsenal, 01/11/2026):**
| Dữ liệu | JSON path |
|---|---|
| Match id | `events[].id` |
| Giờ đá (UTC) | `events[].date` → +7h |
| Trạng thái | `competitions[0].status.type.state` (`pre` / `in` / `post`), `.completed`, `.name` (vd `STATUS_FULL_TIME`, `STATUS_POSTPONED`) |
| Đội, sân nhà/khách | `competitors[].team.id`, `.displayName`, `.abbreviation`, `.homeAway` |
| Tỷ số | `competitors[].score` |
| Logo | `competitors[].team.logo` |
| Phong độ | `competitors[].form` (vd `LWWWW`) |
| Thống kê | `competitors[].statistics[]` (`possessionPct`, `totalShots`, `shotsOnTarget`…) |
| Sự kiện | `competitions[0].details[]` |
| – Bàn thắng | `scoringPlay == true`; kèm `penaltyKick`, `ownGoal` |
| – Thẻ | `yellowCard`, `redCard` |
| – Phút | `clock.displayValue` (vd `90'+7'`) |
| – Cầu thủ | `athletesInvolved[0].displayName` |
| – Đội | `team.id` |
| Sân | `competitions[0].venue.fullName` |

⚠️ `headlines[].description` là văn bản của ESPN — **chỉ dùng tham khảo, không đăng nguyên văn**.

### 5.2 Nguồn dự phòng: football-data.org (free, cần token)
- Chỉ có tỷ số/lịch (không có người ghi bàn, thẻ) cho PL, BL1, FL1, CL. Không có C2/C3.
- Header `X-Auth-Token`. Giới hạn 10 request/phút.

### 5.3 Đã loại
- API-Football free: chặn mùa hiện tại ("Free plans do not have access to this season, try from 2022 to 2024"). Nâng cấp lên trả phí nếu sau này cần.

## 6. Chống báo trùng
- File `data/announced.json` lưu trạng thái đã báo:
```json
{
  "upcoming": { "401879246": { "kickoffUtc": "2026-11-01T16:30:00+00:00", "status": "scheduled", "competition": "eng.1", "announcedAt": "..." } },
  "results":  { "401879246": { "announcedAt": "..." } }
}
```
- Quy tắc:
  - Đã có trong `upcoming` và giờ đá **không đổi** → bỏ qua.
  - Giờ đá **đổi** hoặc trạng thái thành **hoãn/huỷ** → báo lại với nhãn `⚠️ Cập nhật`.
  - Trận đã báo lịch, chưa báo kết quả → lấy lại theo id để phát hiện cả trận bị dời ra ngoài khung giờ.
  - Đã có trong `results` → không báo lại kết quả.
- Chỉ lưu trạng thái **sau khi gửi xong**: gửi lỗi thì lần chạy sau báo lại.
- Dọn mục cũ hơn 30 ngày.
- Job **commit file này lại vào repo** sau mỗi lần chạy (vừa lưu state, vừa giữ repo "active" để GitHub không tắt scheduled workflow).

## 7. Đầu ra: bản nháp gửi Telegram
Mỗi phần (`results`, `upcoming`) gửi **ảnh bảng tỉ số** trước, rồi **3 tin nhắn**, mỗi tin 1 nền tảng (bấm vào khối chữ để copy nhanh).

**Ảnh:** dựng từ HTML/CSS (`CardHtml`) rồi chụp thành PNG bằng Chrome/Edge có sẵn qua Playwright. Mỗi ảnh vẽ **2 bản tiếng Việt và tiếng Anh** (bài chữ vẫn chỉ tiếng Việt). Bản tiếng Việt dùng giờ VN, bản tiếng Anh dùng giờ Anh (`Europe/London`, tự đổi GMT/BST); cả giờ và ngày đều tính theo múi giờ của từng bản. Đầu ảnh ghi ngày thi đấu; nếu các trận thuộc nhiều ngày thì ghi khoảng ngày và thêm ngày dưới từng trận. Có logo đội, tối đa 6 trận/ảnh, màu và tên trang sửa trong `config/card.json`. Không vẽ được ảnh thì vẫn gửi bài chữ và báo cảnh báo.

| Nền tảng | Giới hạn | Định dạng |
|---|---|---|
| Facebook | dài thoải mái | Đầy đủ: nhóm theo giải, tỷ số, người ghi bàn + phút, thẻ đỏ (thẻ vàng tóm tắt số lượng), hashtag |
| Threads | 500 ký tự | Rút gọn: tỷ số + người ghi bàn của các trận nổi bật nhất |
| X | 280 ký tự (tài khoản thường) | Ngắn nhất: 2–4 trận nổi bật + hashtag; nếu dài thì tách thành thread nhiều tweet |

- Telegram giới hạn 4096 ký tự/tin → tự chia nhỏ nếu vượt.
- Giờ hiển thị luôn là giờ VN.
- Tên đội: dùng tên quen thuộc với người Việt (map trong `clubs.json`, vd "Man Utd", "Bayern").

**Ví dụ Facebook – kết quả:**
```
⚽ KẾT QUẢ ĐÊM QUA & RẠNG SÁNG NAY

🏴 NGOẠI HẠNG ANH
Liverpool 0–1 Arsenal
⚽ Havertz 16'
🟨 Liverpool 4 – Arsenal 4

#NgoaiHangAnh #Arsenal #Liverpool
```

**Ví dụ Facebook – sắp diễn ra:**
```
📅 LỊCH THI ĐẤU TỐI NAY & RẠNG SÁNG MAI

⭐ CHAMPIONS LEAGUE
🕒 03:00 | Real Madrid vs Liverpool
   Phong độ: WWDWL | WLWWW
```

## 8. Hạ tầng
- **Runtime:** .NET (console app), chạy trên **GitHub Actions**.
- **Cron (UTC):** `daily` 07:50 VN → `50 0 * * *` (chạy sớm 10 phút vì cron GitHub hay trễ, tránh giờ tròn).
- Cho phép chạy tay: `workflow_dispatch` với input `job=daily|results|upcoming`, `date` (để test lại ngày cũ) và `force` (test thông báo: gửi lại cả trận đã báo, không lưu trạng thái, không có trận thì vẫn gửi 1 dòng).
- Chạy thử ở máy, không gửi và không ghi trạng thái: `dotnet run --project src/FootballNews.App -- daily --date yyyy-MM-dd --dry-run` (kết quả nằm trong `output/`).
- **Secrets:** `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID`, (tuỳ chọn) `FOOTBALL_DATA_TOKEN`.
- **Biến tuỳ chọn:** `NOTIFY_WHEN_EMPTY=true` để gửi 1 dòng khi không có trận.
- Workflow cần `permissions: contents: write` để commit `announced.json`.

## 9. Cấu trúc project đề xuất
```
/
├─ SPEC.md
├─ .github/workflows/football-news.yml
├─ config/ (clubs.json, competitions.json, card.json)
├─ data/announced.json
├─ assets/                        # logo.png của trang, fonts/ (Be Vietnam Pro) cho ảnh
├─ tests/fixtures/                # JSON mẫu ESPN
└─ src/
   ├─ FootballNews.Core/          # model, interface, logic thuần (dễ test)
   │   ├─ Models/ (Match, MatchEvent, Team, Competition)
   │   ├─ Sources/IMatchSource.cs
   │   ├─ Filtering/BigClubFilter.cs
   │   ├─ Dedup/AnnouncedStore.cs
   │   └─ Formatting/ (FacebookFormatter, ThreadsFormatter, XFormatter)
   ├─ FootballNews.Sources.Espn/  # adapter ESPN
   ├─ FootballNews.Sources.FootballData/  # adapter dự phòng (làm sau)
   ├─ FootballNews.Rendering/     # vẽ ảnh bảng tỉ số
   ├─ FootballNews.Telegram/      # gửi tin
   ├─ FootballNews.App/           # console entry: `dotnet run -- daily|results|upcoming [--date yyyy-MM-dd] [--dry-run]`
   └─ FootballNews.Tests/         # test với JSON mẫu ESPN đã lưu (tests/fixtures/*.json)
```
- Nguyên tắc: mọi nguồn dữ liệu implement `IMatchSource` → đổi nguồn không ảnh hưởng phần còn lại.
- Lưu sẵn file JSON ESPN mẫu (vd trận Liverpool–Arsenal) vào `tests/fixtures` để test parser offline.

## 10. Lộ trình
1. **v1 (MVP):** ESPN adapter, lọc ông lớn, 1 lần chạy lúc 08:00 gồm kết quả và lịch đấu, chống trùng, ảnh bảng tỉ số có logo, formatter 3 nền tảng, gửi Telegram, GitHub Actions.
2. **v2:** Thêm tin tức (RSS) + AI viết lại bằng tiếng Việt; adapter dự phòng football-data.org.
3. **v3 (tuỳ chọn):** Nút "Duyệt" trên Telegram để tự đăng lên Facebook Page/X/Threads qua API.

## 11. Rủi ro
- ESPN API không chính thức → có thể đổi/chặn bất cứ lúc nào. Giảm rủi ro: adapter, test fixture, job báo lỗi về Telegram khi parse thất bại.
- Cron GitHub có thể trễ 5–30 phút.
- Repo public: scheduled workflow tự tắt nếu 60 ngày không có hoạt động (đã xử lý bằng commit `announced.json` hằng ngày).
