using System;
using System.IO;

namespace ChangeTestsStateByResult
{
    public static class Logger
    {
        static string logFile = "log.txt";

        public static void Write(string message)
        {
            File.AppendAllLines(logFile, new[]{message});
            Console.WriteLine(message);
        }
    }
}
