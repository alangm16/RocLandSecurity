using System;

namespace RocLandSecurity.Services
{
    public class NotificationEventArgs : EventArgs
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public int RondinId { get; set; }
        public string Type { get; set; } = string.Empty;
    }

    public interface INotificationManagerService
    {
        event EventHandler<NotificationEventArgs> NotificationReceived;
        void SendNotification(string title, string message, DateTime? notifyTime = null, string type = "", int rondinId = 0);
        void ReceiveNotification(string title, string message, string type = "", int rondinId = 0);
    }
}
