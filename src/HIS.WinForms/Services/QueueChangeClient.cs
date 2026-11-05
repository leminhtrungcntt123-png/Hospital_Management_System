using System.Net.WebSockets;

namespace HIS.WinForms.Services;

/// <summary>Nơi lấy cấu hình kênh realtime từ appsettings (Program.cs đọc và đăng ký một lần).</summary>
public sealed record QueueChangeSettings(string WebSocketUrl, bool Enabled = true);

/// <summary>
/// Client WebSocket nối HIS.Web (/ws/quay) để nhận tin "dữ liệu quầy vừa đổi":
/// lượt hẹn mới, lượt hủy, hồ sơ chờ duyệt. Tin chỉ là tín hiệu làm mới, mọi dữ liệu
/// vẫn đọc từ database qua HIS.BLL nên không có thông tin bệnh nhân đi qua kênh này.
/// HIS.Web chưa bật thì vòng kết nối tự thử lại; trang quầy vẫn còn nút Reload và timer 60 giây.
/// </summary>
public sealed class QueueChangeClient
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);

    private readonly Uri _endpoint;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Nhảy ra từ luồng nền; phía nhận tự marshal về UI thread.</summary>
    public event Action? Received;

    public QueueChangeClient(QueueChangeSettings settings)
    {
        _endpoint = new Uri(settings.WebSocketUrl);
    }

    public void Start() => _ = RunAsync(_stop.Token);

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[256];

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(_endpoint, cancellationToken);

                while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    try
                    {
                        Received?.Invoke();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                        // Trang quầy vừa đóng nên BeginInvoke không còn handle để dùng.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Mất web, đổi port hay rớt mạng: chờ rồi kết nối lại.
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(ReconnectDelay, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public void Dispose() => _stop.Cancel();
}
