using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HIS.Web.Realtime;

/// <summary>
/// Phát tin "dữ liệu quầy vừa đổi" tới các màn hình đang giữ WebSocket (WinForms quầy tiếp nhận).
/// Nội dung chỉ gồm loại sự kiện và thời điểm, không có tên bệnh nhân hay mã lịch hẹn:
/// màn hình nhận tin rồi tự truy vấn lại qua HIS.BLL, nên kênh này không chuyển dữ liệu cá nhân.
/// </summary>
public sealed class QueueChangeNotifier
{
    private sealed record Client(WebSocket Socket, SemaphoreSlim SendLock);

    private readonly List<Client> _clients = new();
    private readonly object _gate = new();

    public void Add(WebSocket socket)
    {
        lock (_gate)
        {
            _clients.Add(new Client(socket, new SemaphoreSlim(1, 1)));
        }
    }

    public void Remove(WebSocket socket)
    {
        lock (_gate)
        {
            _clients.RemoveAll(c => c.Socket == socket);
        }
    }

    public void Publish(string kind)
    {
        byte[] message = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { kind, at = DateTime.Now }));

        Client[] targets;
        lock (_gate)
        {
            targets = [.. _clients];
        }

        foreach (Client client in targets)
        {
            if (client.Socket.State != WebSocketState.Open)
            {
                Remove(client.Socket);
                continue;
            }

            // Gửi không chặn luồng HTTP: client rớt thì vòng nhận của chính nó sẽ gỡ khỏi danh sách.
            _ = SendAsync(client, message);
        }
    }

    private static async Task SendAsync(Client client, byte[] message)
    {
        try
        {
            await client.SendLock.WaitAsync();
            await client.Socket.SendAsync(message, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
        }
        catch (WebSocketException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        finally
        {
            client.SendLock.Release();
        }
    }
}

/// <summary>Các loại sự kiện phát đi; WinForms chỉ cần biết "có gì đó đổi" để làm mới.</summary>
public static class QueueChangeKinds
{
    public const string AppointmentCreated = "appointment.created";
    public const string AppointmentPaid = "appointment.paid";
    public const string AppointmentCancelled = "appointment.cancelled";
    public const string ProfileLinkRequested = "profile.link_requested";
}

public static class QueueWebSocketExtensions
{
    /// <summary>
    /// Mở một WebSocket endpoint (mặc định /ws/quay) cho quầy tiếp nhận.
    /// Phải đặt trước UseHttpsRedirection trong pipeline để ws:// trên cổng http không bị chuyển hướng.
    /// </summary>
    public static IApplicationBuilder UseQueueChangeSocket(
        this IApplicationBuilder app, string path = "/ws/quay")
    {
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });

        return app.Use(async (context, next) =>
        {
            if (!context.WebSockets.IsWebSocketRequest
                || !context.Request.Path.StartsWithSegments(path, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            QueueChangeNotifier notifier = context.RequestServices.GetRequiredService<QueueChangeNotifier>();
            using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();

            notifier.Add(socket);
            try
            {
                await ReceiveUntilClosedAsync(socket, context.RequestAborted);
            }
            finally
            {
                notifier.Remove(socket);
            }
        });
    }

    // Client chỉ lắng nghe, không gửi lệnh qua kênh này; đọc liên tục để phát hiện phía kia đóng kết nối.
    private static async Task ReceiveUntilClosedAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[256];

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
            }
            catch (WebSocketException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                try
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
                catch (WebSocketException)
                {
                }

                break;
            }
        }
    }
}
