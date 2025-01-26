namespace Client;

internal static class Cache
{
    private const string _defaultPath = "cache.txt";
    private const string _mutexName = "FileReadWriteIPAddressMutex";
    private static string _previousIPAddress = string.Empty;
    public static string PreviousIPAddress => _previousIPAddress;

    public static void SaveIPAddress(string data, string path = _defaultPath)
    {
        using Mutex mutex = new(false, _mutexName);
        bool lockTaken = false;

        try
        {
            lockTaken = mutex.WaitOne(Timeout.Infinite, false);

            if (lockTaken)
            {
                using FileStream fs = new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                using StreamWriter writer = new(fs);
                writer.WriteLine(data);
                _previousIPAddress = data;
            }
        }
        finally
        {
            if (lockTaken)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    public static string LoadCachedIPAddress(string path = _defaultPath)
    {
        using Mutex mutex = new(false, _mutexName);
        bool lockTaken = false;

        try
        {
            lockTaken = mutex.WaitOne(Timeout.Infinite, false);

            if (lockTaken)
            {
                using FileStream fs = new(path, FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
                using StreamReader reader = new(fs);
                _previousIPAddress = reader.ReadLine() ?? string.Empty;
            }
        }
        finally
        {
            if (lockTaken)
            {
                mutex.ReleaseMutex();
            }
        }

        return _previousIPAddress;
    }
}
