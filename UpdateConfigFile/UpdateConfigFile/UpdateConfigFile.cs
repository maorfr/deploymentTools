using System;
using System.IO;

namespace UpdateConfigFile
{
    public class UpdateConfigFile
    {
        private const string k_format = "%{0}%";

        public static void Main(string[] args)
        {

            var filePath = args[0];

            RemoveReadOnly(filePath);

            var lines = File.ReadAllLines(filePath);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var splits = line.Split('%');

                if (splits.Length == 1) // No replacements needed in this line
                {
                    continue;
                }

                Logger.Write("Possible replacement needed in line " + i + ":");
                Logger.Write(line);

                bool isReplaced = false;
                for (int j = 1; j < splits.Length; j++)
                {
                    if (j % 2 == 0) // assuming % will not be the first char
                        continue;   // parts to replace must be in odd indexes

                    var envVarToSearch = splits[j];
                    if (envVarToSearch.Contains(" "))
                    {
                        continue;
                    }
                    Logger.Write("Searching for environment variable: " + envVarToSearch);
                    var envVar = Environment.GetEnvironmentVariable(envVarToSearch);
                    if (envVar == null)
                    {
                        Logger.Write("Environment variable " + envVarToSearch + " not found");
                        continue;
                    }

                    Logger.Write("Found environment variable " + envVarToSearch + " with value: \"" + envVar + "\"");
                    var toReplace = string.Format(k_format, envVarToSearch);

                    line = line.Replace(toReplace, envVar);
                    isReplaced = true;
                }

                if (isReplaced)
                {
                    Logger.Write("Line after replacements:");
                    Logger.Write(line);
                    lines[i] = line;
                }
                else
                {
                    Logger.Write("No replacement needed");
                }
            }

            File.WriteAllLines(filePath, lines);
        }

        private static void RemoveReadOnly(string path)
        {
            FileAttributes attributes = File.GetAttributes(path);

            if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
            {
                attributes = RemoveAttribute(attributes, FileAttributes.ReadOnly);
                File.SetAttributes(path, attributes);
            }
        }

        private static FileAttributes RemoveAttribute(FileAttributes attributes, FileAttributes attributesToRemove)
        {
            return attributes & ~attributesToRemove;
        }

    }
}
