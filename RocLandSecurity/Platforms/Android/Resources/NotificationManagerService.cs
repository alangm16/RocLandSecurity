using Android.App;
using Android.Content;
using Android.Graphics;
using Android.OS;
using AndroidX.Core.App;
using RocLandSecurity.Services;

[assembly: UsesPermission(Android.Manifest.Permission.PostNotifications)]
[assembly: UsesPermission(Android.Manifest.Permission.ScheduleExactAlarm)]

namespace RocLandSecurity.Platforms.Android
{
    [BroadcastReceiver(Enabled = true, Label = "Rondin Notification Receiver")]
    public class AlarmHandler : BroadcastReceiver
    {
        public override void OnReceive(Context context, Intent intent)
        {
            if (intent?.Extras != null)
            {
                string title = intent.GetStringExtra(NotificationManagerService.TitleKey) ?? string.Empty;
                string message = intent.GetStringExtra(NotificationManagerService.MessageKey) ?? string.Empty;
                string type = intent.GetStringExtra(NotificationManagerService.TypeKey) ?? string.Empty;
                int rondinId = intent.GetIntExtra(NotificationManagerService.RondinIdKey, 0);

                var manager = NotificationManagerService.Instance ?? new NotificationManagerService();
                manager.Show(title, message, type, rondinId);
            }
        }
    }

    public class NotificationManagerService : INotificationManagerService
    {
        const string channelId = "rondin_channel";
        const string channelName = "Rondines";
        const string channelDescription = "Notificaciones de rondines";

        public const string TitleKey = "title";
        public const string MessageKey = "message";
        public const string TypeKey = "type";
        public const string RondinIdKey = "rondinId";

        bool channelInitialized = false;
        int messageId = 0;
        int pendingIntentId = 0;

        NotificationManagerCompat compatManager;

        public event EventHandler<NotificationEventArgs>? NotificationReceived;

        public static NotificationManagerService? Instance { get; private set; }

        public NotificationManagerService()
        {
            if (Instance == null)
            {
                CreateNotificationChannel();
                compatManager = NotificationManagerCompat.From(Platform.AppContext);
                Instance = this;
            }
        }

        public void SendNotification(string title, string message, DateTime? notifyTime = null, string type = "", int rondinId = 0)
        {
            if (!channelInitialized)
            {
                CreateNotificationChannel();
            }

            if (notifyTime != null)
            {
                Intent intent = new Intent(Platform.AppContext, typeof(AlarmHandler));
                intent.PutExtra(TitleKey, title);
                intent.PutExtra(MessageKey, message);
                intent.PutExtra(TypeKey, type);
                intent.PutExtra(RondinIdKey, rondinId);
                intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

                var pendingIntentFlags = (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                    ? PendingIntentFlags.CancelCurrent | PendingIntentFlags.Immutable
                    : PendingIntentFlags.CancelCurrent;

                PendingIntent pendingIntent = PendingIntent.GetBroadcast(Platform.AppContext, pendingIntentId++, intent, pendingIntentFlags);
                long triggerTime = GetNotifyTime(notifyTime.Value);
                AlarmManager alarmManager = Platform.AppContext.GetSystemService(Context.AlarmService) as AlarmManager;
                if (alarmManager != null)
                {
                    alarmManager.SetExact(AlarmType.RtcWakeup, triggerTime, pendingIntent);
                }
            }
            else
            {
                Show(title, message, type, rondinId);
            }
        }

        public void ReceiveNotification(string title, string message, string type = "", int rondinId = 0)
        {
            var args = new NotificationEventArgs()
            {
                Title = title,
                Message = message,
                Type = type,
                RondinId = rondinId
            };
            NotificationReceived?.Invoke(null, args);
        }

        public void Show(string title, string message, string type = "", int rondinId = 0)
        {
            Intent intent = new Intent(Platform.AppContext, typeof(MainActivity));
            intent.PutExtra(TitleKey, title);
            intent.PutExtra(MessageKey, message);
            intent.PutExtra(TypeKey, type);
            intent.PutExtra(RondinIdKey, rondinId);
            intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

            var pendingIntentFlags = (Build.VERSION.SdkInt >= BuildVersionCodes.S)
                ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
                : PendingIntentFlags.UpdateCurrent;

            PendingIntent pendingIntent = PendingIntent.GetActivity(Platform.AppContext, pendingIntentId++, intent, pendingIntentFlags);

            NotificationCompat.Builder builder = new NotificationCompat.Builder(Platform.AppContext, channelId)
                .SetContentIntent(pendingIntent)
                .SetContentTitle(title)
                .SetContentText(message)
                .SetAutoCancel(true)
                .SetDefaults((int)NotificationDefaults.All)
                .SetSmallIcon(Resource.Mipmap.appicon);

            // Intentar cargar ícono grande si existe
            try
            {
                var icon = BitmapFactory.DecodeResource(Platform.AppContext.Resources, Resource.Mipmap.appicon);
                builder.SetLargeIcon(icon);
            }
            catch { }

            Notification notification = builder.Build();
            compatManager.Notify(messageId++, notification);
        }

        void CreateNotificationChannel()
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
            {
                var channelNameJava = new Java.Lang.String(channelName);
                var channel = new NotificationChannel(channelId, channelNameJava, NotificationImportance.High)
                {
                    Description = channelDescription
                };
                NotificationManager manager = (NotificationManager)Platform.AppContext.GetSystemService(Context.NotificationService);
                if (manager != null)
                {
                    manager.CreateNotificationChannel(channel);
                }
                channelInitialized = true;
            }
            else
            {
                channelInitialized = true;
            }
        }

        long GetNotifyTime(DateTime notifyTime)
        {
            DateTime utcTime = TimeZoneInfo.ConvertTimeToUtc(notifyTime);
            double epochDiff = (new DateTime(1970, 1, 1) - DateTime.MinValue).TotalSeconds;
            long utcAlarmTime = utcTime.AddSeconds(-epochDiff).Ticks / 10000;
            return utcAlarmTime;
        }
    }
}