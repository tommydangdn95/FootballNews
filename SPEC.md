# Football News Bot – Spec v1

> Đặt file này ở thư mục gốc repo. Claude (hoặc dev khác) đọc file này trước khi code.

## 1. Mục tiêu
Mỗi ngày tự động tổng hợp **toàn bộ kết quả và lịch đấu của các giải trong mục 3**, soạn bản nháp cho **Facebook, X, Threads**, gửi về **Telegram** để chủ trang duyệt và tự đăng. App **không** tự đăng lên mạng xã hội (v1).

## 2. Lịch chạy (giờ Việt Nam, UTC+7)
Mỗi ngày chạy **một lần lúc 07:00** (job `daily`). Có thể chạy tay từng phần.

| Phần | Gửi khi | Nội dung | Khung dữ liệu |
|---|---|---|---|
| `results` | **Mỗi ngày**, mọi giải | Kết quả các trận đã đá | Trận **đã xong, chưa báo**, có giờ đá trong khoảng 04:00 hôm qua → 07:00 hôm nay |
| `upcoming` | Theo nhóm giải bên dưới | Các trận sắp đá, chưa báo | Trận **bắt đầu** trong khoảng 07:00 hôm nay → hết ngày thứ 5 sau đó |

| Ngày gửi lịch | Giải |
|---|---|
| Thứ 2, 4, 6 | C1, C2, C3, Ngoại Hạng Anh, La Liga |
| Thứ 3, 5, 7 | Bundesliga, Serie A, Ligue 1 |
| Chủ nhật | Không gửi lịch, chỉ gửi kết quả |

- Ngày gửi của từng giải nằm trong `config/competitions.json` (`upcomingWeekdays`).
- Mỗi nhóm có 3 lần gửi, khung 5 ngày nối tiếp nhau nên trận thứ 7 và Chủ nhật luôn có lịch trước khi đá. Do chống trùng (mục 6), mỗi trận chỉ nằm trong bài lịch đầu tiên có chứa nó.
- **Nhắc lại cuối tuần:** bài lịch **thứ 6** hiện thêm mọi trận thứ 7 và Chủ nhật (theo ngày VN) của **cả 8 giải**, kể cả trận đã báo, kể cả giải không thuộc nhóm thứ 6.
- Chạy tay `upcoming` thì gửi ngay lịch của **mọi giải**, không phụ thuộc thứ trong tuần.

- ESPN không có giờ kết thúc trận nên `results` lọc theo giờ đá, lùi thêm 3 giờ cho thời gian trận đấu.
- Giữa tuần thường là cúp (C1/C2/C3), cuối tuần là giải quốc nội — app không cần xử lý riêng, cứ lấy theo khung giờ.
- Không có trận mới trong khung → **luôn gửi 1 dòng thông báo** cho phần đó, ghi rõ chế độ chạy: `(Chạy thường, không bật force)` hoặc `(Chạy force)`.

## 3. Giải đấu (ESPN slug)
| Giải | Slug |
|---|---|
| Premier League | `eng.1` |
| Bundesliga | `ger.1` |
| Ligue 1 | `fra.1` |
| Champions League (C1) | `uefa.champions` |
| Europa League (C2) | `uefa.europa` |
| Conference League (C3) | `uefa.europa.conf` |
| La Liga | `esp.1` |
| Serie A | `ita.1` |

## 4. Danh sách ông lớn (config `clubs.json`, sửa tay được)
**Không dùng để lọc:** mọi trận của các giải trong mục 3 đều được báo. Danh sách chỉ dùng cho tên quen thuộc, hashtag và thứ tự nổi bật trên X/Threads. Match theo **ESPN team id** (ổn định hơn tên).

- **Anh:** Arsenal, Man City, Liverpool, Man United, Chelsea, Tottenham, Newcastle, Aston Villa
- **Đức:** Bayern Munich, Borussia Dortmund, RB Leipzig, Bayer Leverkusen
- **Pháp:** PSG, Marseille, Lyon, Monaco
- **Tây Ban Nha:** Real Madrid, Barcelona, Atlético Madrid
- **Ý:** Inter, Milan, Juventus, Napoli
- **Chỉ xuất hiện ở cúp châu Âu:** Benfica, Porto, Ajax

Trường `priority` (1 = top, 2 = đáng chú ý) quyết định thứ tự nổi bật. Đội ngoài danh sách vẫn được báo, dùng tên của ESPN và không có hashtag riêng.

## 5. Nguồn dữ liệu

### 5.1 Nguồn chính: ESPN public API (không cần key)
```
GET https://site.api.espn.com/apis/site/v2/sports/soccer/{slug}/scoreboard?dates=YYYYMMDD
```
- **Không hỗ trợ khoảng ngày** (`dates=A-B` lỗi) → gọi **từng ngày**.
- `results`: gọi 3 ngày × 8 giải = 24 request. `upcoming`: gọi 7 ngày × số giải của hôm đó (3 hoặc 5; thứ 6 là cả 8 giải để nhắc lại cuối tuần). Chạy `force`, phần kết quả gọi 9 ngày × 8 giải = 72 request. Ngày trùng nhau chỉ gọi một lần.
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
| – Cầu thủ | `athletesInvolved[0].shortName` (bỏ chữ viết tắt đầu, "K. Havertz" → "Havertz"), không có thì `.displayName` |
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
  - Đã có trong `upcoming` và giờ đá **không đổi** → bỏ qua (trừ phần nhắc lại cuối tuần ở bài thứ 6).
  - Giờ đá **đổi** hoặc trạng thái thành **hoãn/huỷ** → báo lại với nhãn `⚠️ Cập nhật`.
  - Trận đã báo lịch, chưa báo kết quả → lấy lại theo id để phát hiện cả trận bị dời ra ngoài khung giờ. Việc này đi cùng bài lịch nên hoãn/đổi giờ của mọi giải được báo vào lần gửi lịch kế tiếp (mọi ngày trừ Chủ nhật).
  - Đã có trong `results` → không báo lại kết quả.
- Chỉ lưu trạng thái **sau khi gửi xong**: gửi lỗi thì lần chạy sau báo lại.
- Dọn mục cũ hơn 30 ngày.
- Job **commit file này lại vào repo** sau mỗi lần chạy để lưu trạng thái.

## 7. Đầu ra: bản nháp gửi Telegram
Mỗi phần (`results`, `upcoming`) gửi theo thứ tự (`DigestText`):
1. **Tin mở đầu:** loại bài, ngày gửi, khoảng ngày thi đấu, tổng số trận và số trận từng giải.
2. Với mỗi giải: **tin ngăn cách** (tên giải, số trận, khoảng ngày, số ảnh của mỗi bản ngôn ngữ), rồi **ảnh bảng tỉ số** bản tiếng Việt và tiếng Anh. Các giải ghép chung ảnh dùng chung một tin ngăn cách.
3. **Tin "Bài nháp để đăng"**, rồi các tin của từng nền tảng (bấm vào khối chữ để copy nhanh). Bài nháp là một bài chung cho mọi giải.

Chạy `force` có thêm một dòng đầu tiên bắt đầu bằng `🧪 Chạy force`.

**Mẫu tin mở đầu:**
```
━━━━━━━━━━━━━━━━━━
📅 LỊCH THI ĐẤU SẮP TỚI
Gửi lúc: Thứ Sáu, 06/11/2026
Các trận: 07/11 – 09/11
Tổng: 3 trận · 2 giải

🏴 Ngoại Hạng Anh: 2 trận
🇪🇸 La Liga: 1 trận
━━━━━━━━━━━━━━━━━━
```

**Mẫu tin ngăn cách** (số ảnh tính cho mỗi bản ngôn ngữ; giải ghép chung thì ghi cả hai):
```
🏴 NGOẠI HẠNG ANH
10 trận · 07/11 – 09/11 · 2 ảnh
──────────────────
```
```
⭐ CHAMPIONS LEAGUE + 🟠 EUROPA LEAGUE
9 + 1 trận · 04/11 – 05/11 · 2 ảnh
──────────────────
```

**Mẫu tin trước bài nháp:**
```
📝 BÀI NHÁP ĐỂ ĐĂNG
Facebook · Threads · X
──────────────────
```

**Ảnh:** dựng từ HTML/CSS (`CardHtml`) rồi chụp thành PNG bằng Chrome/Edge có sẵn qua Playwright. Mỗi ảnh vẽ **2 bản tiếng Việt và tiếng Anh** (bài chữ vẫn chỉ tiếng Việt). Bản tiếng Việt dùng giờ VN, bản tiếng Anh dùng giờ Anh (`Europe/London`, tự đổi GMT/BST); cả giờ và ngày đều tính theo múi giờ của từng bản. Đầu ảnh không ghi ngày để dùng đăng bài được; khoảng ngày, kể cả khi chạy `force`, nằm trong tin mở đầu và tin ngăn cách trên Telegram. Ảnh **lịch đấu**: dưới giờ đá mỗi trận ghi ngày đá. Ảnh **kết quả**: dưới tỉ số mỗi trận ghi ngày và giờ đá. Đầu ảnh ghi "Đêm qua & rạng sáng nay", riêng khi chạy `force` trải nhiều ngày thì ghi "Các trận gần đây". Chân ảnh chỉ ghi tên trang, không đánh số trang. Có logo đội. Ô mỗi trận có nền chuyển màu từ xanh chanh sang xanh ngọc, chữ trong ô màu tối. Màu và tên trang sửa trong `config/card.json`. Không vẽ được ảnh thì vẫn gửi bài chữ và báo cảnh báo.

**Chia ảnh (`CardPaging`):** mỗi giải có ảnh riêng, mỗi ảnh **2–6 trận**. Giải nhiều trận thì chia đều thành nhiều ảnh (7 trận → 4 + 3). Giải chỉ có 1 trận thì ghép chung với giải liền kề. Ngoại lệ duy nhất: cả phần chỉ có đúng 1 trận thì vẫn vẽ một ảnh cho trận đó.

| Nền tảng | Giới hạn | Định dạng |
|---|---|---|
| Facebook | dài thoải mái | Đầy đủ: nhóm theo giải, tỷ số, người ghi bàn + phút, thẻ đỏ (thẻ vàng tóm tắt số lượng), hashtag |
| Threads | 500 ký tự/bài | Tất cả các trận: tỷ số + người ghi bàn; nếu dài thì tách thành nhiều bài nối tiếp, hashtag ở bài cuối |
| X | 280 ký tự/tweet (tài khoản thường) | Tất cả các trận, chỉ tỷ số hoặc giờ đá; nếu dài thì tách thành thread nhiều tweet, hashtag ở tweet cuối |

- Thứ tự trên Threads và X: bài kết quả xếp trận nổi bật trước, bài lịch đấu xếp theo giờ đá.
- Telegram giới hạn 4096 ký tự/tin → tự chia nhỏ nếu vượt.
- Giờ trong bài chữ luôn là giờ VN, kèm ngày đá ở bài lịch (`🕒 03:00 06/11 | ...`).
- Tiêu đề bài chữ kết quả khi chạy `force` trải hơn 2 ngày là `⚽ KẾT QUẢ CÁC TRẬN GẦN ĐÂY`.
- Tên đội: dùng tên quen thuộc với người Việt (map trong `clubs.json`, vd "Man Utd", "Bayern").

**Ví dụ Facebook – kết quả:**
```
⚽ KẾT QUẢ ĐÊM QUA & RẠNG SÁNG NAY

🏴 NGOẠI HẠNG ANH
Liverpool 0–1 Arsenal
⚽ Havertz 16'
🟨 Liverpool 4 – Arsenal 4

#NgoaiHangAnh #Liverpool #Arsenal
```

**Ví dụ Facebook – sắp diễn ra:**
```
📅 LỊCH THI ĐẤU SẮP TỚI

⭐ CHAMPIONS LEAGUE
🕒 03:00 06/11 | Real Madrid vs Liverpool
   Phong độ: WWDWL | WLWWW
```

## 8. Hạ tầng
- **Runtime:** .NET 8 (`net8.0`, console app), chạy trên **GitHub Actions** với `dotnet-version: 8.0.x`. Hạ từ .NET 10 xuống vì máy phát triển không cài được .NET 10.
- **Lịch chạy:** đặt ở **cron-job.org** lúc 07:00 VN (`Asia/Ho_Chi_Minh`), gọi `POST https://api.github.com/repos/tommydangdn95/FootballNews/actions/workflows/football-news.yml/dispatches` với body `{"ref":"main"}`, không truyền `force`. Workflow không có `schedule` riêng.
- Cho phép chạy tay: `workflow_dispatch` với input `job=daily|results|upcoming`, `date` (để test lại ngày cũ) và `force` (gửi đầy đủ mọi giải: kết quả 7 ngày qua tính đến hết ngày báo cáo và lịch 5 ngày tới, bất kể thứ trong tuần, kể cả trận đã báo, không lưu trạng thái; luôn gửi cả hai phần dù chọn `job` nào).
- **Secrets:** `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID` (v2 thêm `FOOTBALL_DATA_TOKEN`).
- Workflow cần `permissions: contents: write` để commit `announced.json`.

**Cài trên máy mới:**
- Cần .NET SDK có thể build `net8.0` kèm runtime .NET 8 (đã chạy được với SDK 9.0.102 + runtime 8.0.12), và Chrome hoặc Edge để vẽ ảnh. Thiếu trình duyệt thì vẫn có bài chữ, chỉ không có ảnh.
- Chạy test: `dotnet test src/FootballNews.Tests/FootballNews.Tests.csproj`.
- Chạy thử, không gửi và không ghi trạng thái: `dotnet run --project src/FootballNews.App -- daily --date yyyy-MM-dd --dry-run [--force]`. Kết quả nằm trong `output/`, gồm ảnh, bài nháp và `*-messages.txt` ghi thứ tự tin nhắn.
- `FootballNews.slnx` cần SDK từ 9.0.200 trở lên; SDK cũ hơn thì build trực tiếp từng project như lệnh trên.
- Bot tự commit `data/announced.json` lên `main`, nên luôn `git pull --rebase` trước khi push.

## 9. Cấu trúc project
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
   │   ├─ Filtering/ReportWindow.cs
   │   ├─ Dedup/AnnouncedStore.cs
   │   └─ Formatting/ (FacebookFormatter, ThreadsFormatter, XFormatter, DigestText)
   ├─ FootballNews.Sources.Espn/  # adapter ESPN
   ├─ FootballNews.Sources.FootballData/  # adapter dự phòng (làm sau)
   ├─ FootballNews.Rendering/     # vẽ ảnh bảng tỉ số (CardHtml, CardPaging, CardRenderer)
   ├─ FootballNews.Telegram/      # gửi tin
   ├─ FootballNews.App/           # console entry: `dotnet run -- daily|results|upcoming [--date yyyy-MM-dd] [--dry-run] [--force]`
   └─ FootballNews.Tests/         # test với JSON mẫu ESPN đã lưu (tests/fixtures/*.json)
```
- Nguyên tắc: mọi nguồn dữ liệu implement `IMatchSource` → đổi nguồn không ảnh hưởng phần còn lại.
- Lưu sẵn file JSON ESPN mẫu (vd trận Liverpool–Arsenal) vào `tests/fixtures` để test parser offline.

## 10. Lộ trình
1. **v1 (MVP):** ESPN adapter, 1 lần chạy lúc 07:00 (kết quả mỗi ngày, lịch đấu theo nhóm giải), chống trùng, ảnh bảng tỉ số có logo, formatter 3 nền tảng, gửi Telegram, GitHub Actions.
2. **v2:** Thêm tin tức (RSS) + AI viết lại bằng tiếng Việt; adapter dự phòng football-data.org.
3. **v3 (tuỳ chọn):** Nút "Duyệt" trên Telegram để tự đăng lên Facebook Page/X/Threads qua API.

## 11. Rủi ro
- ESPN API không chính thức → có thể đổi/chặn bất cứ lúc nào. Giảm rủi ro: adapter, test fixture, job báo lỗi về Telegram khi parse thất bại.
- Lịch phụ thuộc cron-job.org: dịch vụ này lỗi hoặc token GitHub của nó hết hạn thì bot không chạy và cũng không có tin báo lỗi.
- .NET 8 hết hỗ trợ khoảng 10/11/2026: app vẫn chạy nhưng không còn bản vá bảo mật, về lâu dài nên nâng lên bản LTS mới.
- Chạy `force` có thể gửi hơn 100 tin (khoảng 58 ảnh), dễ chạm giới hạn tốc độ của Telegram. Code đợi theo `retry_after` và thử tối đa 5 lần mỗi tin; app tự dừng sau 13 phút, job giới hạn 15 phút.

## 12. Trạng thái hiện tại
**Đã kiểm chứng:** test tự động và dry-run với dữ liệu ESPN thật; một lần chạy thường trên GitHub Actions với .NET 8 (05/11/2026) đã lưu được `announced.json`.

**Chưa kiểm chứng:** một lần chạy `force` thật gửi đủ hơn 100 tin; lịch 07:00 từ cron-job.org (cấu hình nằm ngoài repo).

**Hạn chế đã biết:**
- Bot không chạy quá khoảng 27 giờ thì kết quả của trận rơi ra ngoài khung sẽ không được báo.
- Trận đã báo rồi dời sang ngày khác chỉ báo "giờ mới" một lần, không hiện lại trong lịch ngày đá (trừ phần nhắc lại cuối tuần).
- Trận đang đá lúc chạy không xuất hiện ở cả kết quả lẫn lịch đấu, kể cả khi `force`.
- `force` chỉ lấy kết quả 7 ngày, nên cúp châu Âu không đá trong tuần có thể vắng mặt.
- Bài chữ kết quả khi `force` không ghi ngày đá từng trận; ảnh thì có.
- Telegram trả lỗi không phải JSON (vd 502 dạng HTML) sẽ báo lỗi parse thay vì lỗi thật, và lỗi 5xx không được thử lại.
