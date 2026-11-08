namespace FootballNews.Rendering;

/// <summary>
/// Ảnh được thiết kế theo khung rộng 1080px nhưng chụp ở mật độ điểm ảnh cao hơn, để cạnh rộng đúng 2048px:
/// cỡ lớn nhất Facebook giữ nguyên, không phải co giãn lại nên chữ và logo sắc nét.
/// </summary>
public static class RenderScale
{
    public const int DesignWidth = 1080;
    public const int OutputWidth = 2048;
    public const float DeviceScaleFactor = (float)OutputWidth / DesignWidth;
}
