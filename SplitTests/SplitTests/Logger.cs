using System;
using System.IO;

namespace SplitTests
{
    public static class Logger
    {
        //private static string logPath = @"C:\Users\maorf\Desktop\log.txt";

        public static void Write(string message)
        {
            //File.AppendAllLines(logPath, new[] { message });
            Console.WriteLine(message);
        }

        public static void Warning(string message)
        {
            //File.AppendAllLines(logPath, new[] { message });
            Console.WriteLine(message);
        }
    }
}
