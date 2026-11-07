# Football News Bot – Spec v1

> Đặt file này ở thư mục gốc repo. Claude (hoặc dev khác) đọc file này trước khi code.

## 1. Mục tiêu
Mỗi ngày tự động tổng hợp **toàn bộ kết quả và lịch đấu của các giải trong mục 3**, vẽ **ảnh bảng tỉ số**, gửi về **Telegram** để chủ trang duyệt và tự đăng. Bài tổng hợp hằng ngày **không** tự đăng lên mạng xã hội. Riêng phần **báo trực tiếp** (mục 13) đăng lên Fanpage Facebook, nhưng chỉ sau khi chủ trang bấm duyệt trên Telegram.

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
**Không dùng để lọc:** mọi trận của các giải trong mục 3 đều được báo. Danh sách chỉ dùng cho tên quen thuộc trên ảnh (`hashtag` và `priority` còn trong file nhưng không dùng nữa). Match theo **ESPN team id** (ổn định hơn tên).

- **Anh:** Arsenal, Man City, Liverpool, Man United, Chelsea, Tottenham, Newcastle, Aston Villa
- **Đức:** Bayern Munich, Borussia Dortmund, RB Leipzig, Bayer Leverkusen
- **Pháp:** PSG, Marseille, Lyon, Monaco
- **Tây Ban Nha:** Real Madrid, Barcelona, Atlético Madrid
- **Ý:** Inter, Milan, Juventus, Napoli
- **Chỉ xuất hiện ở cúp châu Âu:** Benfica, Porto, Ajax

Đội ngoài danh sách vẫn được báo, dùng tên của ESPN.

## 5. Nguồn dữ liệu

### 5.1 Nguồn chính: ESPN public API (không cần key)
```
GET https://site.api.espn.com/apis/site/v2/sports/soccer/{slug}/scoreboard?dates=YYYYMMDD
```
- **Không hỗ trợ khoảng ngày** (`dates=A-B` lỗi) → gọi **từng ngày**.
- `results`: gọi 3 ngày × 8 giải = 24 request. `upcoming`: gọi 7 ngày × số giải của hôm đó (3 hoặc 5; thứ 6 là cả 8 giải để nhắc lại cuối tuần). Chạy `force`, phần kết quả gọi 9 ngày × 8 giải = 72 request. Ngày trùng nhau chỉ gọi một lần.
- Lưu ý: tham số `dates` theo ngày của ESPN (giờ Mỹ), nên luôn lấy dư 1 ngày rồi lọc lại theo UTC trong code.
- **Không đặt User-Agent có chữ "bot":** Akamai của ESPN trả 403 cho `FootballNewsBot/1.0` (đã thử 04/11/2026). Dùng mặc định của `HttpClient`, không giả danh trình duyệt.
- Phong độ (chỉ cho bài lịch đấu): `GET .../soccer/{slug}/teams/{teamId}/schedule?season={year}` trả các trận đã đá của đội **trong đúng giải đó** và mùa đó; score dạng `{ "value": 2.0 }`. Mỗi đội gọi một lần mỗi lần chạy (khoảng 2 request mỗi trận).
- **Không dùng `competitors[].form` của ESPN:** chuỗi này xếp mới → cũ và trộn nhiều giải (vd Liverpool `LLWWW` ngày 08/11/2026 gồm cả Ngoại Hạng Anh lẫn C1 nhưng bỏ Carabao Cup).
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
| Mùa giải | `events[].season.year` (vd `2026` cho mùa 2026-27); summary: `header.season.year` |
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

## 7. Đầu ra: tin và ảnh gửi Telegram
Mỗi phần (`results`, `upcoming`) gửi theo thứ tự (`DigestText`):
1. **Tin mở đầu:** loại bài, ngày gửi, khoảng ngày thi đấu, tổng số trận và số trận từng giải.
2. Với mỗi giải: **tin ngăn cách** (tên giải, số trận, khoảng ngày, số ảnh của mỗi bản ngôn ngữ), rồi **ảnh bảng tỉ số** bản tiếng Việt và tiếng Anh. Các giải ghép chung ảnh dùng chung một tin ngăn cách.

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

**Ảnh:** dựng từ HTML/CSS (`CardHtml`) rồi chụp thành PNG bằng Chrome/Edge có sẵn qua Playwright. Mỗi ảnh vẽ **2 bản tiếng Việt và tiếng Anh**; không có bài chữ để đăng. Bản tiếng Việt dùng giờ VN, bản tiếng Anh dùng giờ nước chủ nhà với La Liga (`Europe/Madrid`), Bundesliga (`Europe/Berlin`), Ligue 1 (`Europe/Paris`) và giờ Anh (`Europe/London`) với các giải còn lại, tự đổi giờ mùa hè; dòng phụ đề lịch đấu ghi rõ múi giờ (vd. "Kick-off times in Spanish time"); cả giờ và ngày đều tính theo múi giờ của từng bản. Đầu ảnh không ghi ngày để dùng đăng bài được; khoảng ngày, kể cả khi chạy `force`, nằm trong tin mở đầu và tin ngăn cách trên Telegram. Ảnh **lịch đấu**: dưới giờ đá mỗi trận ghi ngày đá và tên sân (lấy từ ESPN, thiếu thì bỏ qua; ảnh kết quả không ghi sân). Ảnh lịch đấu có **phong độ** mỗi đội: tối đa 5 trận gần nhất **trong cùng giải và cùng mùa**, đá trước giờ bóng lăn của trận (trận Ngoại Hạng Anh chỉ tính trận Ngoại Hạng Anh, trận C1 chỉ tính trận C1…). Xếp cũ → mới, trận gần nhất ở bên phải. Mùa mới đá ít hơn 5 trận thì hiện ít ô hơn, không lấy mùa trước; chưa đá trận nào hoặc lấy lỗi thì không hiện. Trận hoà rồi đá luân lưu lấy kết quả cuối cùng: đội thắng luân lưu là W, đội thua là L (lịch đội của ESPN không có tỉ số luân lưu, chỉ có trạng thái `STATUS_FINAL_PEN` và cờ `winner`; cờ này chỉ dùng khi có luân lưu vì ở lượt về cúp nó có thể đánh dấu đội đi tiếp nhờ tổng tỉ số). Dưới tên giải có dòng giải thích, vd "Phong độ 5 trận gần nhất tại Ngoại Hạng Anh mùa này, trận mới nhất bên phải". Ảnh **kết quả**: dưới tỉ số mỗi trận ghi ngày và giờ đá. Đầu ảnh ghi "Đêm qua & rạng sáng nay", riêng khi chạy `force` trải nhiều ngày thì ghi "Các trận gần đây". Chân ảnh chỉ ghi tên trang, không đánh số trang. Có logo đội. Ô mỗi trận có nền chuyển màu từ xanh chanh sang xanh ngọc, chữ trong ô màu tối. Màu và tên trang sửa trong `config/card.json`. Không vẽ được ảnh thì vẫn gửi tin mở đầu, tin ngăn cách và báo cảnh báo.

**Chia ảnh (`CardPaging`):** mỗi giải có ảnh riêng, mỗi ảnh **2–6 trận**. Giải nhiều trận thì chia đều thành nhiều ảnh (7 trận → 4 + 3). Giải chỉ có 1 trận thì ghép chung với giải liền kề. Ngoại lệ duy nhất: cả phần chỉ có đúng 1 trận thì vẫn vẽ một ảnh cho trận đó.

- Telegram giới hạn 4096 ký tự/tin.
- Tên đội: dùng tên quen thuộc với người Việt (map trong `clubs.json`, vd "Man Utd", "Bayern").

## 8. Hạ tầng
- **Runtime:** .NET 8 (`net8.0`, console app), chạy trên **GitHub Actions** với `dotnet-version: 8.0.x`. Hạ từ .NET 10 xuống vì máy phát triển không cài được .NET 10.
- **Lịch chạy:** đặt ở **cron-job.org** lúc 07:00 VN (`Asia/Ho_Chi_Minh`), gọi `POST https://api.github.com/repos/tommydangdn95/FootballNews/actions/workflows/football-news.yml/dispatches` với body `{"ref":"main"}`, không truyền `force`. Workflow không có `schedule` riêng.
- Cho phép chạy tay: `workflow_dispatch` với input `job=daily|results|upcoming`, `date` (để test lại ngày cũ) và `force` (gửi đầy đủ mọi giải: kết quả 7 ngày qua tính đến hết ngày báo cáo và lịch 5 ngày tới, bất kể thứ trong tuần, kể cả trận đã báo, không lưu trạng thái; luôn gửi cả hai phần dù chọn `job` nào).
- **Secrets:** `TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID`; phần live dùng bot Telegram riêng với `LIVE_TELEGRAM_BOT_TOKEN`, `TELEGRAM_ADMIN_ID`, cùng `FB_PAGE_ID`, `FB_PAGE_TOKEN` (v2 thêm `FOOTBALL_DATA_TOKEN`).
- Workflow cần `permissions: contents: write` để commit `announced.json`.

**Cài trên máy mới:**
- Cần .NET SDK có thể build `net8.0` kèm runtime .NET 8 (đã chạy được với SDK 9.0.102 + runtime 8.0.12), và Chrome hoặc Edge để vẽ ảnh. Thiếu trình duyệt thì chỉ có tin chữ trên Telegram, không có ảnh.
- Chạy test: `dotnet test src/FootballNews.Tests/FootballNews.Tests.csproj`.
- Chạy thử, không gửi và không ghi trạng thái: `dotnet run --project src/FootballNews.App -- daily --date yyyy-MM-dd --dry-run [--force]`. Kết quả nằm trong `output/`, gồm ảnh và `*-messages.txt` ghi thứ tự tin nhắn.
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
   │   ├─ Models/ (Match, MatchEvent, Team, Competition, TeamForm)
   │   ├─ Sources/IMatchSource.cs
   │   ├─ Filtering/ReportWindow.cs
   │   ├─ Dedup/AnnouncedStore.cs
   │   └─ Formatting/ (DigestText: các tin chữ trên Telegram)
   ├─ FootballNews.Sources.Espn/  # adapter ESPN
   ├─ FootballNews.Sources.FootballData/  # adapter dự phòng (làm sau)
   ├─ FootballNews.Rendering/     # vẽ ảnh bảng tỉ số (CardHtml, CardPaging, CardRenderer)
   ├─ FootballNews.Telegram/      # gửi tin; phần live thêm đọc lệnh và nút bấm (getUpdates)
   ├─ FootballNews.Facebook/      # đăng ảnh lên Fanpage qua Graph API (phần live)
   ├─ FootballNews.App/           # console entry: `dotnet run -- daily|results|upcoming [--date yyyy-MM-dd] [--dry-run] [--force]`
   └─ FootballNews.Tests/         # test với JSON mẫu ESPN đã lưu (tests/fixtures/*.json)
```
- Nguyên tắc: mọi nguồn dữ liệu implement `IMatchSource` → đổi nguồn không ảnh hưởng phần còn lại.
- Lưu sẵn file JSON ESPN mẫu (vd trận Liverpool–Arsenal) vào `tests/fixtures` để test parser offline.

## 10. Lộ trình
1. **v1 (MVP):** ESPN adapter, 1 lần chạy lúc 07:00 (kết quả mỗi ngày, lịch đấu theo nhóm giải), chống trùng, ảnh bảng tỉ số có logo và phong độ, gửi Telegram, GitHub Actions.
2. **v2:** Thêm tin tức (RSS) + AI viết lại bằng tiếng Việt; adapter dự phòng football-data.org.
3. **v3:** Báo trực tiếp một trận do chủ trang chọn, duyệt trên Telegram rồi đăng lên Fanpage Facebook (mục 13). Đăng lên X/Threads để sau.

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
- Telegram trả lỗi không phải JSON (vd 502 dạng HTML) sẽ báo lỗi parse thay vì lỗi thật, và lỗi 5xx không được thử lại.

## 13. Báo trực tiếp (live) lên Fanpage

Chủ trang chọn **một trận** để theo dõi. Khi trận đấu có sự kiện, bot gửi bản nháp về Telegram. Chủ trang duyệt (có thể sửa nội dung) thì bot mới đăng lên Fanpage **Matchday Challenge Việt Nam**. Chậm 5–10 phút so với thực tế là chấp nhận được.

### 13.1 Bot Telegram riêng
- Phần live dùng **một bot Telegram riêng** (`LIVE_TELEGRAM_BOT_TOKEN`), tách khỏi bot gửi bài hằng ngày, để hai bên không giành tin nhắn của nhau khi đọc `getUpdates`.
- Bot live chỉ nhắn trong **chat riêng với chủ trang**. Chat id của chat riêng bằng user id, nên chỉ cần `TELEGRAM_ADMIN_ID`.
- **Chỉ chủ trang ra lệnh được:** bot chỉ xử lý tin nhắn và nút bấm có `from.id` và `chat.id` bằng `TELEGRAM_ADMIN_ID`. Người khác nhắn hay bấm nút thì bot lặng lẽ bỏ qua, không trả lời. Trong @BotFather tắt `/setjoingroups`.

### 13.2 Lệnh
| Lệnh | Tác dụng |
|---|---|
| `/watch <tên đội>` | Tìm trận của 8 giải trong mục 3, từ hôm nay đến 5 ngày tới (giờ VN). Bot trả danh sách trận khớp kèm nút để chọn. |
| `/list` | Xem trận đang theo dõi và bản nháp đang chờ. |
| `/unwatch` | Bỏ theo dõi. Các bản nháp đang chờ bị huỷ, không đăng. |
| `/fbtest` | Đăng thử một bài lên Fanpage và gửi link về để kiểm tra người ngoài xem được. |
| `/whoami` | Trả lại user id của người nhắn (để cài `TELEGRAM_ADMIN_ID`). Lệnh duy nhất trả lời cả người lạ. |
| `/help` | Danh sách lệnh. |

- **Mỗi lúc chỉ theo dõi một trận.** Đang theo dõi mà `/watch` trận khác thì bot từ chối và nhắc `/unwatch` trước.
- Trận kết thúc (và hàng đợi bản nháp đã trống), bị hoãn hoặc huỷ thì bot tự bỏ theo dõi và báo lại.

### 13.3 Sự kiện báo
| Sự kiện | Nhận biết từ ESPN (`summary?event={id}`) |
|---|---|
| ⚽ Bàn thắng (kể cả penalty, phản lưới) | `details[]` có `scoringPlay`, và tỉ số của đội tăng |
| 🟥 Thẻ đỏ | `details[]` có `redCard` |
| ⏸️ Hết hiệp 1 | Trạng thái `STATUS_HALFTIME` |
| ⏱️ Hết 90 phút | Trận hoà sau 90 phút và đá hiệp phụ (trạng thái hết giờ chính thức, vd `STATUS_END_OF_REGULATION`). Bàn thắng trong hiệp phụ vẫn báo |
| 🏁 Kết thúc / Hết hiệp phụ | Trận `completed`, hoặc hết hiệp phụ (kể cả khi sau đó đá luân lưu). Có hiệp phụ thì ghi "Hết hiệp phụ". **Không báo gì về luân lưu**, không ghi tỉ số luân lưu |
| ❌ VAR huỷ bàn thắng | Số bàn của một đội **giảm** so với lần trước |

- Huỷ bàn chỉ dựa vào **số bàn giảm**. ESPN sửa phút hoặc tên cầu thủ của bàn đã báo thì bỏ qua, không coi là huỷ.
- Bàn bị huỷ **đã được đăng** → tạo bản nháp "VAR huỷ bàn thắng".
- Bàn bị huỷ **còn đang chờ duyệt hoặc xếp hàng** → thêm dòng "⚠️ VAR đã huỷ bàn này" vào bản nháp đó. Chủ trang vẫn tự quyết.
- Bàn bị huỷ **đã bị Bỏ hoặc hết hạn** → không tạo gì.

### 13.4 Bản nháp và duyệt
- Mỗi sự kiện tạo **một bản nháp**, đăng thành **một bài mới** trên Fanpage. Bản nháp giữ đúng tỉ số **lúc xảy ra sự kiện**.
- **Hàng đợi:** mỗi lúc Telegram chỉ có **một bản nháp đang chờ quyết định**. Bản đến sau xếp hàng theo thứ tự, chỉ hiện lên khi bản trước đã được Đăng, Bỏ hoặc hết hạn. Không bản nào bị huỷ thay chủ trang.
- **Hết hạn sau 10 phút tính từ lúc bot phát hiện sự kiện** (ESPN không có giờ thực tế của sự kiện). Bản đang chờ mà hết hạn thì tin trên Telegram sửa thành "⌛ Hết hạn" và mất nút. Bản còn trong hàng đợi mà hết hạn thì bị bỏ qua, bot gửi một dòng báo ngắn.
- Tin nháp gồm **ảnh xem trước** đúng như bài sẽ đăng, caption, và 3 nút **✅ Đăng / ❌ Bỏ / ✏️ Sửa**.
- **Sửa:** bấm ✏️ Sửa hoặc trả lời (reply) vào tin nháp bằng nội dung mới. Nội dung mới thay **chữ trên ảnh** và **dòng đầu của caption** (hashtag giữ nguyên). Bot vẽ lại ảnh và gửi bản xem trước mới kèm 3 nút. Sửa không gia hạn thời gian.
- Không có bản nào được duyệt thì không đăng gì.

### 13.5 Nội dung bài đăng
- **Chỉ tiếng Việt.** Tên đội lấy từ `clubs.json` như bài hằng ngày.
- **Ảnh vuông 1080×1080**, nền chuyển màu (giống kiểu bài chữ nền màu của Facebook, vì Graph API không hỗ trợ chính thức kiểu bài đó), chữ trắng đậm ở giữa, có logo trang. Mỗi loại sự kiện một bộ màu, sửa trong `config/card.json`.
- Mẫu chữ trên ảnh:
  - Bàn thắng: `⚽ BÀN THẮNG!` / `Arsenal 1-0 Hull City` / `Declan Rice 23'`
  - Thẻ đỏ: `🟥 THẺ ĐỎ` / `Hull City – Tên cầu thủ 61'` / `Arsenal 1-0 Hull City`
  - Hết hiệp 1: `⏸️ HẾT HIỆP 1` / `Arsenal 1-0 Hull City`
  - Hết 90 phút: `⏱️ HẾT 90 PHÚT` / `Arsenal 1-1 Hull City`
  - Kết thúc: `🏁 KẾT THÚC` / `Arsenal 2-1 Hull City` (có hiệp phụ thì `🏁 HẾT HIỆP PHỤ`)
  - VAR huỷ: `❌ VAR HUỶ BÀN THẮNG` / `Bàn của Declan Rice 23'` / `Tỉ số: Arsenal 0-0 Hull City`
- **Caption:** một dòng ngắn và hashtag, vd `⚽ Declan Rice mở tỷ số cho Arsenal! #Arsenal #HullCity #MatchdayChallenge`.

### 13.6 Đăng lên Facebook
- Graph API `POST /{FB_PAGE_ID}/photos` (ảnh + caption) bằng **Page access token không hết hạn** (`FB_PAGE_TOKEN`). App Meta `Matchday Live Bot` ở chế độ Live, quyền `pages_show_list`, `pages_read_engagement`, `pages_manage_posts` mức Standard access.
- Đăng xong bot gửi link bài về Telegram.
- **Đăng lỗi** (token hết hiệu lực, mất quyền, lỗi mạng…) → bot báo lỗi về Telegram, bản nháp **giữ nguyên** kèm nút để bấm Đăng lại sau khi sửa (vẫn tính hạn 10 phút).
- Page token bị vô hiệu khi đổi mật khẩu Facebook, mất quyền quản trị Page, xoá app hoặc bị checkpoint → lấy lại token theo hướng dẫn và cập nhật secret.

### 13.7 Cách chạy
- Workflow riêng `.github/workflows/football-live.yml`, job `live`, **concurrency group riêng** `football-live` (không chặn job hằng ngày). Repo để **public** nên không giới hạn số phút Actions.
- **cron-job.org gọi workflow 10 phút một lần.** Mỗi lần chạy:
  1. Đọc lệnh và nút bấm mới bằng `getUpdates` (Telegram giữ tin 24 giờ, `offset` đánh dấu đã đọc).
  2. Không theo dõi trận nào, hoặc trận còn hơn 15 phút nữa mới đá → thoát.
  3. Trận sắp đá hoặc đang đá → **chạy liên tục suốt trận**: cứ **3–4 phút** hỏi ESPN một lần (khoảng 30–40 request cho một trận), giữa các lần thì đọc Telegram liên tục (long polling) để bấm nút là xử lý ngay.
  4. Thoát khi trận đã xong và hàng đợi trống (tối đa thêm 10 phút), hoặc gần hết giới hạn 6 giờ của GitHub (job đặt `timeout-minutes: 330`). Lần gọi sau của cron-job.org sẽ chạy tiếp từ trạng thái đã lưu.
- Trong lúc job live đang chạy, các lần gọi của cron-job.org xếp hàng chờ. Khi được chạy thì thấy không còn việc và thoát ngay.

### 13.8 Trạng thái
- File riêng `data/live.json`, tách khỏi `announced.json`:
  - Trận đang theo dõi (id, giải, giờ đá).
  - Tỉ số và danh sách sự kiện đã thấy (để biết sự kiện nào mới, bàn nào bị huỷ).
  - Các bản nháp: loại sự kiện, nội dung, caption, lúc tạo, trạng thái (`queued`, `active`, `posted`, `skipped`, `expired`), id tin Telegram, id bài Facebook.
- Ghi file sau mỗi thay đổi. **Commit và push sau mỗi lần đăng Facebook** và khi job kết thúc, để job chết giữa chừng thì lần sau không đăng lại bài đã đăng.

### 13.9 Rủi ro
- ESPN chậm 1–2 phút và đôi khi sửa sự kiện sau vài phút (đổi người ghi bàn, đổi phút). Bot chỉ báo theo lần đầu thấy.
- Bản nháp bị xếp hàng sau một bản chưa duyệt có thể hết hạn trước khi kịp hiện ra.
- Đăng dồn dập nhiều bài có thể bị bộ lọc spam của Facebook hạn chế. Hàng đợi một bản một lúc giúp giãn khoảng cách.
- cron-job.org lỗi thì lệnh `/watch` không được đọc và trận không được theo dõi.
