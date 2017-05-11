using System.IO;
using System;

namespace UpdateConfigFile
{
    public static class Logger
    {
        static bool FirstUse = true;
        static string LogPath = @"Log.txt";

        private static void ClearLog()
        {
            File.Delete(LogPath);
        }

        public static void Write(string message)
        {
            if (FirstUse)
            {
                ClearLog();
                FirstUse = false;
            }

            File.AppendAllText(LogPath, Environment.NewLine + DateTime.Now + ": " + message);
            Console.Write(Environment.NewLine + DateTime.Now + ": " + message);
        }

        public static void Ok()
        {
            File.AppendAllText(LogPath, "OK");
            Console.Write("OK");
        }

        public static void Error()
        {
            File.AppendAllText(LogPath, "ERROR");
            Console.Write("ERROR");
        }

        public static void NA()
        {
            File.AppendAllText(LogPath, "Not applicable");
            Console.Write("Not applicable");
        }
    }
}
