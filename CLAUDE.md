# CLAUDE.md

Đọc `SPEC.md` trước khi code. Spec là nguồn sự thật về hành vi của bot; đổi hành vi thì sửa spec cùng lúc.

## Nguyên tắc số 1: kết quả phải chính xác và được kiểm chứng

Bot đăng tỉ số, bảng xếp hạng và lịch đấu cho người xem thật. Một con số sai là mất uy tín của trang.

- **Mọi thông tin trên ảnh và tin nhắn phải truy được về dữ liệu nguồn** (ESPN), hoặc được tính từ dữ liệu đó bằng một quy tắc đã ghi trong `SPEC.md` và có test. Không đoán, không điền giá trị cho đủ.
- **Không chắc thì bỏ trống**, đừng hiện giá trị có thể sai.
- **Không tin một trường ESPN khi chưa xem dữ liệu thật.** Trước khi dùng trường mới, gọi API thật của vài giải và xem giá trị có hợp lý không. Đã gặp: `rankChange` luôn bằng 0; ghi chú vùng xếp hạng gán cả suất dự cúp không nhờ thứ hạng; cờ `winner` ở lượt về cúp đánh dấu đội đi tiếp.
- **Số liệu tự tính phải tự kiểm tra chéo** với dữ liệu nguồn: số vòng khớp số trận đã đá, đội vừa thắng không thể tụt hạng vì chính trận đó, tổng điểm khớp thắng/hoà/bại.

## Trước khi báo "xong"

1. `dotnet test src/FootballNews.Tests/FootballNews.Tests.csproj` phải chạy qua hết.
2. Thay đổi ảnh hưởng tới nội dung hoặc ảnh thì chạy thử với dữ liệu thật, không gửi đi đâu:
   `dotnet run --project src/FootballNews.App -- results --date yyyy-MM-dd --dry-run`
   (ảnh nằm trong `output/<ngày>/`), mở ảnh ra xem.
3. Đối chiếu các con số trên ảnh với dữ liệu ESPN của chính lần chạy đó (tỉ số, điểm, hạng, phong độ…); với thay đổi lớn, so thêm với trang chính thức của giải.
4. Báo lại cho chủ trang đã kiểm chứng những gì, và chỗ nào còn là ước lượng (vd luật phụ khi hai đội bằng điểm).
