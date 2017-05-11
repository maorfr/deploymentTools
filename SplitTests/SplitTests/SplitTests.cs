using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.TestManagement.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace SplitTests
{
    class SplitTests
    {
        private const int _defaultBucketSize = 20;
        private const string _testAttribute = "[TestMethod]";

        private static Dictionary<string, TestMap> _testsNameTestsMap;
        private static Dictionary<string, TestMap> _testsNameTestsMapAll;
        private static List<TestMap> _duplicateTests;
        private static List<string> _testsInSuite;
        private static Dictionary<string, int> _projectDuplicateCounter;
        private static string _solution;
        private static string _solutionRoot;
        private static string _projectName;
        private static int _bucketSize;
        private static WorkItemStore _store;
        private static ITestManagementTeamProject _testProject;

        public static void Main(string[] args)
        {
            //ValidateArgumens(new[] { @"C:\Users\maorf\Desktop\UIAutomation\OfekUIAutomation.sln", @"http://192.168.0.119:8080/tfs/DefaultCollection", "Ofek" });
            
            ValidateArgumens(args);
            SetBucketSize();
            InitiateTestsInSuite();
            MapAllTests();
            RemoveReadOnlyFromSolutionFolder();
            RemoveAllTestsFromSolution();
            DuplicateProjectsAndAddToSolution();
            UpdateAssociatedAutomation();
        }

        public static void ValidateArgumens(string[] args)
        {
            if (args.Length != 3)
                throw new ArgumentException("Input should be <solutionPath.sln> <TFS URL> <Project Name>.");

            ValidateTfs(args[1], args[2]);

            var localPath = TranslateToLocalPath(args[0]);

            if (!File.Exists(localPath))
                throw new FileNotFoundException("Solution file not found.");

            _solution = localPath;
            _solutionRoot = Path.GetDirectoryName(_solution);
        }

        private static string TranslateToLocalPath(string workspacePath)
        {
            var buildRepositoryLocalPath = Environment.GetEnvironmentVariable("BUILD_REPOSITORY_LOCALPATH");

            return workspacePath.Replace("$/" + _projectName, buildRepositoryLocalPath);
        }

        private static void ValidateTfs(string tfsUrl, string projectName)
        {
            try
            {
                var tfsUri = new Uri(tfsUrl);
                var tfs = new TfsTeamProjectCollection(tfsUri);

                
                _store = new WorkItemStore(tfs);
                var service = (ITestManagementService)tfs.GetService(typeof(ITestManagementService));
                _projectName = projectName;
                _testProject = service.GetTeamProject(_projectName);
        }
            catch
            {
                throw new ArgumentException("TFS URL or Prject Name is invalid.");
    }
}

        public static void SetBucketSize()
        {
            var bucketSizeEnvVar = Environment.GetEnvironmentVariable("BucketSize");

            if (bucketSizeEnvVar == null || bucketSizeEnvVar.Equals(string.Empty))
                _bucketSize = _defaultBucketSize;
            else
            {
                bool isParsed = Int32.TryParse(bucketSizeEnvVar, out _bucketSize);
                if (!isParsed)
                    throw new ArgumentException("Could not parse BucketSize environment variable.");
            }

            Logger.Write("BucketSize is set to: " + _bucketSize);
        }

        public static void InitiateTestsInSuite()
        {
            var testSuiteIdEnvVar = Environment.GetEnvironmentVariable("TestSuiteId");

            if (testSuiteIdEnvVar != null && !testSuiteIdEnvVar.Equals(string.Empty))
            {
                int testSuiteId;
                bool isParsed = Int32.TryParse(testSuiteIdEnvVar, out testSuiteId);
                if (!isParsed)
                    throw new ArgumentException("Could not parse TestSuiteId environment variable.");

                var suite = _testProject.TestSuites.Find(testSuiteId);

                var testCases = suite.AllTestCases;

                Logger.Write("***** Adding all tests found in suite " + testSuiteId + " (" + suite.Title + ")" + " *****");
                _testsInSuite = new List<string>();
                foreach (var testCase in testCases)
                {
                    var implementation = (ITmiTestImplementation) testCase.Implementation;

                    if (implementation == null) // skip not automated tests
                        continue;

                    var testMethodName = implementation.TestName;
                    var testName = GetTestName(testMethodName);

                    if (!_testsInSuite.Contains(testName))
                    {
                        _testsInSuite.Add(testName);
                        Logger.Write("Found test in suite: " + testName);
                    }
                }
            }
        }

        public static void MapAllTests()
        {
            Logger.Write("********** Mapping tests **********");
            _testsNameTestsMap = new Dictionary<string, TestMap>();
            _testsNameTestsMapAll = new Dictionary<string, TestMap>();
            _duplicateTests = new List<TestMap>();

            var projects = GetAllProjectsNamesInSolution();
            foreach (var project in projects)
            {
                var files = GetAllCodeFilesInProject(project);
                foreach (var file in files)
                {
                    if (DoesFileContainsTests(file))
                    {
                        AddToTestsMap(file, project);
                    }
                }
            }
        }

        private static IEnumerable<string> GetAllProjectsNamesInSolution()
        {
            var projects = new List<string>();

            var lines = File.ReadAllLines(_solution);

            foreach (var line in lines)
            {
                if (!line.StartsWith("Project"))
                    continue;

                var first = line.IndexOf('"');
                var second = line.IndexOf('"', first + 1);
                var startIndex = line.IndexOf('"', second + 1);
                var endIndex = line.IndexOf('"', startIndex + 1);

                var projectName = line.Substring(startIndex + 1, endIndex - startIndex - 1);
                projects.Add(projectName);
            }

            return projects;
        }

        private static IEnumerable<string> GetAllCodeFilesInProject(string project)
        {
            const string codeExtension = "*.cs";
            var dir = _solutionRoot + @"\" + project;
            if (!Directory.Exists(dir))
            {
                return new string[]{}; // log this
            }
            var files = Directory.GetFiles(dir, codeExtension, SearchOption.AllDirectories);

            return files;
        }

        private static bool DoesFileContainsTests(string file)
        {
            var lines = File.ReadAllLines(file);

            foreach (var line in lines)
            {
                if (line.Contains(_testAttribute))
                    return true;
            }

            return false;
        }

        private static void AddToTestsMap(string file, string project)
        {
            var lines = File.ReadAllLines(file);

            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains(_testAttribute))
                    continue;

                var tempTestMap = new TestMap
                {
                    ProjectThatContainsTest = project,
                    FilePathThatContainsTest = file,
                    TestStartLine = i
                };
                var tempTestName = FindTestName(lines, i);

                if (!_testsNameTestsMapAll.ContainsKey(tempTestName))
                {
                    _testsNameTestsMapAll.Add(tempTestName, tempTestMap);

                    if ((_testsInSuite == null) || (_testsInSuite != null && _testsInSuite.Contains(tempTestName)))
                    {
                        if (!_testsNameTestsMap.ContainsKey(tempTestName))
                        {
                            _testsNameTestsMap.Add(tempTestName, tempTestMap);
                            Logger.Write("Added a test to Tests Map!" + Environment.NewLine +
                                         "Test name: " + tempTestName + Environment.NewLine +
                                         "Test project: " + tempTestMap.ProjectThatContainsTest + Environment.NewLine +
                                         "Test file: " + tempTestMap.FilePathThatContainsTest + Environment.NewLine +
                                         "Line: " + tempTestMap.TestStartLine);
                        }
                    }
                }
                else
                {
                    _duplicateTests.Add(tempTestMap);
                    Logger.Warning("***** WARNING: Found a duplicate test: " + Environment.NewLine +
                        "***** Test name: " + tempTestName + Environment.NewLine +
                        "***** Test project: " + tempTestMap.ProjectThatContainsTest + Environment.NewLine +
                        "***** Test file: " + tempTestMap.FilePathThatContainsTest + Environment.NewLine +
                        "***** Line: " + tempTestMap.TestStartLine);
                }
            }
        }

        private static string FindTestName(string[] lines, int startLine)
        {
            bool found = false;
            int i = startLine;
            string testName = "";

            while (!found)
            {
                var line = lines[i];
                if (line.Contains("void"))
                {
                    var bracketsIndex = line.IndexOf('(');
                    line = line.Substring(0, bracketsIndex);

                    var lastSpace = line.LastIndexOf(' ');
                    testName = line.Substring(lastSpace + 1).Replace(" ", "").Replace("()", "");
                    break;
                }
                i++;
            }

            return testName;
        }

        public static void RemoveReadOnlyFromSolutionFolder()
        {
            Logger.Write("RemoveReadOnlyFromSolutionFolder");
            Logger.Write("Removing Read Only from folder: " + _solutionRoot);

            var di = new DirectoryInfo(_solutionRoot);

            foreach (var file in di.GetFiles("*", SearchOption.AllDirectories))
                file.Attributes &= ~FileAttributes.ReadOnly;

            Logger.Write("RemoveReadOnlyFromSolutionFolder complete");
        }

        public static void RemoveAllTestsFromSolution()
        {
            Logger.Write("RemoveAllTestsFromSolution");
            foreach (var testNameTestMap in _testsNameTestsMapAll)
            {
                RemoveTest(testNameTestMap.Value);
            }

            foreach (var duplicateTest in _duplicateTests)
            {
                RemoveTest(duplicateTest);
            }
            Logger.Write("RemoveAllTestsFromSolution complete");
        }

        public static void DuplicateProjectsAndAddToSolution()
        {
            Logger.Write("DuplicateProjectsAndAddToSolution");
            var duplicateProjectName = "";
            var previousProjectName = "";
            int index = 0;

            foreach (var testNameTestMap in _testsNameTestsMap)
            {
                var testMap = testNameTestMap.Value;
                bool isBucketSizeReached = index%_bucketSize == 0;
                bool hasProjectChanged = !testMap.ProjectThatContainsTest.Equals(previousProjectName);
                bool shouldDuplicate = isBucketSizeReached || hasProjectChanged;
                previousProjectName = testMap.ProjectThatContainsTest;

                var projectName = testMap.ProjectThatContainsTest;

                if (shouldDuplicate)
                {
                    duplicateProjectName = DuplicateProject(projectName);
                    Logger.Write("********** Duplicating project " + projectName + " to " + duplicateProjectName + " **********");
                }

                testMap.FilePathThatContainsTest = testMap.FilePathThatContainsTest.Replace(@"\" + projectName + @"\", @"\" + duplicateProjectName + @"\");
                ReAddTest(testMap.FilePathThatContainsTest, testMap, testNameTestMap.Key);

                testMap.NewProjectThatContainsTest = duplicateProjectName;

                index++;
            }
            Logger.Write("DuplicateProjectsAndAddToSolution complete");
        }

        private static string DuplicateProjectAndRemoveTestsExcept(string testName, TestMap testMap)
        {
            var projectName = testMap.ProjectThatContainsTest;
            var duplicateProjectName = DuplicateProject(projectName);
            RemoveTest(testMap); // remove from original project

            foreach (var testsNameTestsMap in _testsNameTestsMap)
            {
                var currentTestProjectName = testsNameTestsMap.Value.ProjectThatContainsTest;

                if (currentTestProjectName.Equals(projectName) &&
                    !testsNameTestsMap.Key.Equals(testName))
                {
                    var newFileToRemoveTestIn = testsNameTestsMap.Value.FilePathThatContainsTest.Replace(projectName,
                        duplicateProjectName);
                    RemoveTest(testsNameTestsMap.Value);
                }
            }

            return duplicateProjectName;
        }

        private static string DuplicateProjectAndReAddTest(string testName, TestMap testMap)
        {
            var projectName = testMap.ProjectThatContainsTest;
            var duplicateProjectName = DuplicateProject(projectName);

            testMap.FilePathThatContainsTest = testMap.FilePathThatContainsTest.Replace(projectName,
                        duplicateProjectName);
            ReAddTest(testMap.FilePathThatContainsTest, testMap, testName);

            return duplicateProjectName;
        }

        private static string DuplicateProject(string project)
        {
            if (_projectDuplicateCounter == null)
            {
                _projectDuplicateCounter = new Dictionary<string, int>();
            }

            if (!_projectDuplicateCounter.ContainsKey(project))
            {
                _projectDuplicateCounter.Add(project, 0);
            }

            int postfix = _projectDuplicateCounter[project] + 1;
            _projectDuplicateCounter[project] = postfix;
            string duplicateProjectName = project + AddLeadingZeros(postfix.ToString());
            var duplicateProjectGuid = DuplicateProject(project, duplicateProjectName);
            AddDProjectToSolution(project, duplicateProjectName, duplicateProjectGuid);

            return duplicateProjectName;
        }

        private static string AddLeadingZeros(string postfix)
        {
            var sb = new StringBuilder();
            int leadingZerosCount = _testsNameTestsMap.Count.ToString().Length - postfix.ToString().Length;

            for (int i = 0; i < leadingZerosCount; i++)
            {
                sb.Append("0");
            }

            return sb.ToString() + postfix;
        }

        private static string DuplicateProject(string srcProject, string dstProject)
        {
            var srcPath = _solutionRoot + @"\" + srcProject;
            var dstPath = _solutionRoot + @"\" + dstProject;

            foreach (string dirPath in Directory.GetDirectories(srcPath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(srcPath, dstPath));
            }

            foreach (string newPath in Directory.GetFiles(srcPath, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(srcPath, dstPath), true);
            }

            bool areEqual = false;

            while (!areEqual)
            {
                int srcPathCount = Directory.GetFiles(srcPath, "*.*", SearchOption.AllDirectories).Count();
                int dstPathCount = Directory.GetFiles(dstPath, "*.*", SearchOption.AllDirectories).Count();

                if (srcPathCount.Equals(dstPathCount))
                    areEqual = true;
                else
                    Thread.Sleep(50);
            }

            var duplicateProjectGuid = UpdateProjectFile(dstPath, srcProject, dstProject);
            return duplicateProjectGuid;
        }

        private static string UpdateProjectFile(string path, string oldFileName, string newFileName)
        {
            const string prjectFileExtension = ".csproj";
            const string projectGuidFormat = "    <ProjectGuid>{{{0}}}</ProjectGuid>";
            const string rootNamespaceFormat = "    <RootNamespace>{0}</RootNamespace>";
            const string assemblyNameFormat = "    <AssemblyName>{0}</AssemblyName>";

            var filePath = path + @"\" + oldFileName + prjectFileExtension;
            var updatedFilePath = path + @"\" + newFileName + prjectFileExtension;
            var lines = File.ReadAllLines(filePath);

            var newGuid = Guid.NewGuid().ToString().ToUpper();

            bool projectGuidReplaced = false;
            bool rootNamespaceReplaced = false;
            bool assemblyNameReplaced = false;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("<ProjectGuid>"))
                {
                    var replacement = string.Format(projectGuidFormat, newGuid);
                    lines[i] = replacement;
                    projectGuidReplaced = true;
                }

                if (lines[i].Contains("<RootNamespace>"))
                {
                    var replacement = string.Format(rootNamespaceFormat, newFileName);
                    lines[i] = replacement;
                    rootNamespaceReplaced = true;
                }

                if (lines[i].Contains("<AssemblyName>"))
                {
                    var replacement = string.Format(assemblyNameFormat, newFileName);
                    lines[i] = replacement;
                    assemblyNameReplaced = true;
                }
            }

            if (!projectGuidReplaced || !rootNamespaceReplaced || !assemblyNameReplaced)
            {
                throw new KeyNotFoundException();
            }

            File.WriteAllLines(filePath, lines);
            File.Move(filePath, updatedFilePath);

            return newGuid;
        }

        private static void RemoveTest(TestMap testMap)
        {
            const string replacement = @"/*" + _testAttribute + @"*/";
            var file = testMap.FilePathThatContainsTest;
            var lines = File.ReadAllLines(file);

            if (!lines[testMap.TestStartLine].Contains(replacement))
                lines[testMap.TestStartLine] = lines[testMap.TestStartLine].Replace(_testAttribute, replacement);

            File.WriteAllLines(file, lines);
        }

        private static void ReAddTest(string file, TestMap testMap, string testName)
        {
            const string replacement = @"/*" + _testAttribute + @"*/";
            var lines = File.ReadAllLines(file);

            if (lines[testMap.TestStartLine].Contains(replacement))
                lines[testMap.TestStartLine] = lines[testMap.TestStartLine].Replace(replacement, _testAttribute);

            File.WriteAllLines(file, lines);

            Logger.Write("ReAdded " + testName + " to " + file);
        }

        private static void AddDProjectToSolution(string originalProject, string newProject, string newGuid)
        {
            var solutionLines = File.ReadAllLines(_solution);
            var solutionList = solutionLines.ToList();
            string projectDeclarationLine = "";
            int projectDeclarationLineIndex = -1;

            for (int i = 0; i < solutionLines.Length; i++)
            {
                var line = solutionLines[i];
                if (line.StartsWith("Project") && line.Contains("\"" + originalProject + "\""))
                {
                    projectDeclarationLine = line;
                    projectDeclarationLineIndex = i;
                    break;
                }
            }

            var newProjectLine = projectDeclarationLine.Replace(originalProject, newProject);
            var guidStartIndex = newProjectLine.LastIndexOf("{");
            var guidEndIndex = newProjectLine.LastIndexOf("}");
            var oldGuid = newProjectLine.Substring(guidStartIndex + 1, guidEndIndex - guidStartIndex - 1);
            newProjectLine = newProjectLine.Substring(0, guidStartIndex + 1) + newGuid + "}\"";
            
            solutionList.Insert(projectDeclarationLineIndex, "EndProject");
            solutionList.Insert(projectDeclarationLineIndex, newProjectLine);

            for (int i = 0; i < solutionList.Count; i++)
            {
                var line = solutionList[i];
                if (line.Trim().StartsWith("{" + oldGuid + "}"))
                {
                    solutionList.Insert(i, line.Replace(oldGuid, newGuid));
                    i++;
                }
            }

            File.WriteAllLines(_solution, solutionList);
        }

        public static void UpdateAssociatedAutomation()
        {
            WorkItemCollection tests = GetAllAutomatedTestsFromTfs();

            foreach (var rawtest in tests)
            {
                WorkItem testItem = rawtest as WorkItem;
                ITestCase test = _testProject.TestCases.Find(testItem.Id);
                ITmiTestImplementation implementation = (ITmiTestImplementation)test.Implementation;

                string associatedAutomation = implementation.TestName;
                string testType = implementation.TestType;
                string testName = GetTestName(associatedAutomation);

                if (_testsNameTestsMap.ContainsKey(testName))
                {
                    TestMap testMap = _testsNameTestsMap[testName];

                    AssociateAutomation(test, associatedAutomation, testType, testMap.NewProjectThatContainsTest + ".dll");
                }
            }
        }

        private static string GetTestName(string fullTestName)
        {
            int lastDotIndex = fullTestName.LastIndexOf(".");
            return fullTestName.Substring(lastDotIndex + 1);
        }

        private static WorkItemCollection GetAllAutomatedTestsFromTfs()
        {
            WorkItemCollection tests = _store.Query(
                "Select * " +
                "From WorkItems " +
                "Where [Work Item Type] = 'Test Case' " +
                "And [Team Project] = '" + _projectName + "'" +
                "And [Automation status] = 'Automated'");

            return tests;
        }

        private static void AssociateAutomation(ITestCase testCase, string automationTestName, string testType, string filename)
        {
            var crypto = new SHA1CryptoServiceProvider();
            var bytes = new byte[16];
            Array.Copy(crypto.ComputeHash(Encoding.Unicode.GetBytes(automationTestName)), bytes, bytes.Length);
            var automationGuid = new Guid(bytes);

            testCase.Implementation = testCase.Project.CreateTmiTestImplementation(
                    automationTestName, testType,
                    filename, automationGuid);

            testCase.Save();
        }

        private class TestMap
        {
            public string ProjectThatContainsTest;
            public string NewProjectThatContainsTest;
            public string FilePathThatContainsTest;
            public int TestStartLine;
        }
    }
}
