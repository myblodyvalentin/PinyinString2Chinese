namespace PinyinString2Chinese;

internal static class AppLog
{
    private static readonly object Gate = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PinyinString2Chinese");

    private static readonly string FilePath = Path.Combine(DirectoryPath, "app.log");

    internal static void Info(string message) => Write("INFO", message);

    internal static void Error(string message) => Write("ERROR", message);

    internal static void Error(Exception exception) =>
        Write("ERROR", exception.ToString());

    private static void Write(string level, string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(DirectoryPath);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 200_000)
                    File.Delete(FilePath);

                File.AppendAllText(FilePath, line);
            }
        }
        catch
        {
            // 日志失败不影响还原。
        }
    }
}
