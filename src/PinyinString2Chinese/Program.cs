namespace PinyinString2Chinese;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(false, @"Local\PinyinString2Chinese");
        var owned = false;
        try
        {
            owned = mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }

        if (!owned)
        {
            MessageBox.Show(
                "拼音还原已经在运行。桌面上的悬浮球可以单击开关，托盘图标在任务栏右下角。",
                "拼音还原",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, args) => AppLog.Error(args.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception exception)
                    AppLog.Error(exception);
            };

            Application.Run(new TrayApplicationContext());
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
