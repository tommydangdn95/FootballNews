# Football News Bot – Matchday Challenge

Bot bóng đá cho Fanpage **Matchday Challenge Việt Nam**, gồm hai phần chạy độc lập trên GitHub Actions:

| | Bot hằng ngày | Bot live |
|---|---|---|
| Việc làm | Gửi kết quả và lịch đấu kèm ảnh về Telegram để duyệt và tự đăng | Theo dõi một trận, gửi bản nháp về Telegram, duyệt xong thì đăng lên Fanpage |
| Bot Telegram | `TELEGRAM_BOT_TOKEN` | Bot riêng, `LIVE_TELEGRAM_BOT_TOKEN` |
| Workflow | `football-news.yml` | `football-live.yml` |
| Cách chạy | cron-job.org gọi lúc 07:00 | Bấm **Run workflow** bằng tay |
| Trạng thái | `data/announced.json` | `data/live.json` |

Chi tiết đầy đủ nằm trong [SPEC.md](SPEC.md) (bot live ở mục 13).

---

## Bot live: cách dùng

1. Trên Telegram, nhắn cho bot live `/live` để xem các trận đang đá và sắp đá, hoặc `/watch arsenal hull` để tìm một trận cụ thể.
2. Vào GitHub **Actions → football-live → Run workflow** (web hoặc app điện thoại). Bước 1 và 2 làm theo thứ tự nào cũng được: mỗi lần chạy, bot nghe lệnh 5 phút.
3. Khoảng 1 phút sau bot gửi danh sách trận. Bấm chọn trận.
4. Bot tự chạy và chờ đến 15 phút trước giờ đá, theo dõi hết trận rồi tự tắt. Không cần bấm lại.

> Nên bấm Run workflow trong ngày đá. Mỗi lượt chạy GitHub tối đa 6 giờ; trận còn xa hơn thì bot tự nối lượt mới để chờ tiếp.
> Huỷ lượt chạy trên GitHub hoặc bot gặp lỗi thì không tự nối lượt: trận vẫn được ghi nhớ, bấm Run workflow lại là chạy tiếp.

### Lệnh

| Lệnh | Tác dụng |
|---|---|
| `/live` | Các trận **đang đá** (kèm tỉ số) và **sắp đá trong 3 giờ tới** của mọi giải, mỗi trận một nút để bấm theo dõi luôn (tối đa 20 nút). Đang theo dõi trận khác thì chỉ liệt kê, không có nút. |
| `/watch <tên đội>` | Tìm trận để theo dõi, vd `/watch arsenal hull`. Không phân biệt hoa thường và dấu; mọi từ phải khớp tên đội. Tìm trận chưa đá hoặc đang đá, từ hôm nay đến 5 ngày tới, trong 8 giải chính và các cúp quốc nội (FA Cup, Carabao Cup, Copa del Rey, DFB Pokal, Coppa Italia, Coupe de France). Trả tối đa 8 trận kèm nút để chọn. |
| `/list` | Trận đang theo dõi, tỉ số bot đã thấy và trạng thái từng bản nháp. |
| `/unwatch` | Bỏ theo dõi. Bản nháp đang chờ bị huỷ, không đăng. |
| `/stop` | Bỏ theo dõi (nếu có), huỷ bản nháp đang chờ và **tắt workflow ngay**, không nghe lệnh thêm 5 phút và không tự nối lượt mới. Muốn dùng lại thì bấm Run workflow. |
| `/autopilot` | Bật/tắt **tự đăng** cho trận đang theo dõi (gõ không kèm gì để đảo trạng thái, hoặc `/autopilot on`, `/autopilot off`). Bật lên thì mọi bản nháp tự đăng lên Fanpage, không chờ duyệt; bản đang chờ duyệt cũng đăng luôn. Bot vẫn gửi ảnh kèm link bài về Telegram. Tự tắt khi bỏ theo dõi hoặc theo dõi xong. |
| `/fbtest` | Đăng thử một bài chữ lên Fanpage và gửi link về. Mở link bằng cửa sổ ẩn danh để chắc người ngoài xem được, rồi xoá bài. |
| `/whoami` | Xem user id Telegram (dùng để cài `TELEGRAM_ADMIN_ID`). Lệnh duy nhất trả lời cả người lạ. |
| `/help`, `/start` | Danh sách lệnh. |

Chỉ chủ trang (`TELEGRAM_ADMIN_ID`) ra lệnh và bấm nút được; người khác nhắn thì bot lặng lẽ bỏ qua.

### Bản nháp

Bot báo các sự kiện: **bàn thắng, thẻ đỏ, hết hiệp 1, hết 90 phút** (khi có hiệp phụ), **kết thúc / hết hiệp phụ** và **VAR huỷ bàn thắng**. Không báo gì về luân lưu.

Mỗi sự kiện là một bản nháp gồm ảnh xem trước và caption, kèm 3 nút:

| Nút | Tác dụng |
|---|---|
| ✅ Đăng | Đăng ảnh và caption thành bài mới trên Fanpage, tin nháp đổi thành "Đã đăng" kèm link. |
| ❌ Bỏ | Không đăng, chuyển sang bản nháp kế tiếp. |
| ✏️ Sửa | Gửi nội dung mới: **mỗi dòng là một dòng chữ trên ảnh**, cả đoạn cũng là caption (hashtag giữ nguyên). Bot vẽ lại ảnh và hỏi duyệt lần nữa. Cách nhanh hơn: **reply thẳng vào tin nháp** bằng nội dung mới. |

- Mỗi lúc chỉ có **một bản nháp chờ duyệt**; bản sau xếp hàng, hiện ra khi bản trước đã Đăng, Bỏ hoặc hết hạn.
- Bản nháp **hết hạn sau 10 phút** tính từ lúc bot phát hiện sự kiện.
- VAR huỷ một bàn: bàn đã đăng thì có bản nháp báo huỷ; bàn còn chờ duyệt thì bản nháp đó được gắn cảnh báo ⚠️; bàn đã Bỏ hoặc hết hạn thì không có gì.
- Mỗi lúc chỉ theo dõi **một trận**. Muốn đổi trận thì `/unwatch` trước.
- Hết trận và không còn bản nháp chờ thì bot nhắn "Đã theo dõi xong" rồi workflow tự dừng (muộn nhất khoảng 15 phút sau tiếng còi).
- Phòng khi ESPN bị kẹt: quá **3 tiếng** sau giờ bóng lăn (**3 tiếng 45 phút** nếu có hiệp phụ) mà chưa thấy kết thúc thì bot tự dừng theo dõi và báo lý do.
- Đăng Facebook lỗi thì bot báo về Telegram, bản nháp giữ nguyên để bấm Đăng lại.
- **Autopilot** (`/autopilot`): bản nháp đăng ngay khi có, không có nút. Đăng lỗi thì bản đó chuyển sang chờ duyệt như thường. Bản nháp có cảnh báo VAR thì không tự đăng, vẫn chờ bạn quyết. Bỏ hoặc sửa không còn kịp, muốn xoá bài thì xoá trên Fanpage.

### Menu lệnh trong Telegram (tuỳ chọn)

Trong @BotFather gõ `/setcommands`, chọn bot live, dán:

```
live - Các trận đang đá và sắp đá trong 3 giờ tới
watch - Tìm trận để theo dõi, vd /watch arsenal hull
list - Trận đang theo dõi và bản nháp
unwatch - Bỏ theo dõi trận hiện tại
stop - Bỏ theo dõi và tắt bot ngay
autopilot - Bật/tắt tự đăng bài không chờ duyệt
fbtest - Đăng thử một bài lên Fanpage
whoami - Xem user id Telegram
help - Danh sách lệnh
```

### Cài đặt lần đầu

1. **Bot Telegram mới:** @BotFather → `/newbot`; `/setjoingroups` → Disable; mở chat với bot và bấm **Start**.
2. **GitHub Secrets** (Settings → Secrets and variables → Actions):

   | Secret | Lấy ở đâu |
   |---|---|
   | `LIVE_TELEGRAM_BOT_TOKEN` | Token @BotFather trả về |
   | `TELEGRAM_ADMIN_ID` | Nhắn @userinfobot, hoặc `/whoami` cho bot |
   | `FB_PAGE_ID` | `id` của Page trong `me/accounts` (Graph API Explorer) |
   | `FB_PAGE_TOKEN` | `access_token` của Page trong `me/accounts`, loại không hết hạn |

3. **Meta App** `Matchday Live Bot`: quyền `pages_show_list`, `pages_read_engagement`, `pages_manage_posts`; chế độ **Live**; Privacy Policy là [PRIVACY.md](PRIVACY.md).
4. Page token mất hiệu lực khi đổi mật khẩu Facebook, mất quyền quản trị Page, xoá app hoặc bị checkpoint: lấy lại token rồi cập nhật `FB_PAGE_TOKEN`.

### Đổi màu và nội dung

- Màu nền ảnh từng loại sự kiện, chu kỳ hỏi ESPN, hạn bản nháp, giới hạn thời gian theo dõi, danh sách cúp: [config/live.json](config/live.json).
- Tên đội và hashtag đội: [config/clubs.json](config/clubs.json).
- Xem trước ảnh mọi loại sự kiện trên máy (cần Chrome hoặc Edge), kết quả trong `output/live-preview/`:

  ```
  dotnet run --project src/FootballNews.App -- live --preview
  ```

---

## Bot hằng ngày

- Chạy lúc 07:00 (giờ VN) qua cron-job.org, hoặc bấm tay **Actions → football-news → Run workflow** với `job` = `daily` / `results` / `upcoming`, `date` (để test ngày cũ) và `force` (gửi đầy đủ, không lưu trạng thái).
- Chạy thử trên máy, không gửi và không lưu trạng thái:

  ```
  dotnet run --project src/FootballNews.App -- daily --date yyyy-MM-dd --dry-run [--force]
  ```

## Phát triển

- Cần .NET SDK build được `net8.0` (kèm runtime .NET 8) và Chrome hoặc Edge để vẽ ảnh.
- Chạy test: `dotnet test src/FootballNews.Tests/FootballNews.Tests.csproj`
