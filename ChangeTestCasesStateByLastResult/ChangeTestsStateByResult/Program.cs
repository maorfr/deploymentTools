using System;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.TestManagement.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using System.Collections.Generic;

namespace ChangeTestsStateByResult
{
    class Program
    {
        static string TfsUrl { get; set; }
        static string ProjectName { get; set; }
        static int TestPlanId { get; set; }
        static List<int> TestSuiteIds { get; set; }

        static void Main(string[] args)
        {
            UpdateEnvironmentVariables();
            ChangeTestCasesStateByLastResult();
        }

        static void UpdateEnvironmentVariables()
        {
            TfsUrl = Environment.GetEnvironmentVariable("SYSTEM_TEAMFOUNDATIONCOLLECTIONURI");
            ProjectName = Environment.GetEnvironmentVariable("SYSTEM_TEAMPROJECT");
            TestPlanId = int.Parse(Environment.GetEnvironmentVariable("TestPlanId"));
            var suiteIds = Environment.GetEnvironmentVariable("TestSuiteIds").Replace(" ", "").Split(',');
            TestSuiteIds = new List<int>();

            foreach (var suiteId in suiteIds)
            {
                TestSuiteIds.Add(int.Parse(suiteId));
            }

            //TfsUrl = "http://nt101:8080/tfs/DefaultCollection";
            //ProjectName = "PLM-TC-10";
            //TestPlanId = 134191;
            //TestSuiteIds.Add(146925);
        }

        static void ChangeTestCasesStateByLastResult()
        {
            var tfsCollection = new TfsTeamProjectCollection(new Uri(TfsUrl));
            tfsCollection.EnsureAuthenticated();

            var testManagementService = tfsCollection.GetService<ITestManagementService>();
            var workItemStore = tfsCollection.GetService<WorkItemStore>();
            var teamProject = testManagementService.GetTeamProject(ProjectName);
            var testPlan = teamProject.TestPlans.Find(TestPlanId);

            foreach (var suiteId in TestSuiteIds)
            {
                Logger.Write("");
                Logger.Write(string.Format("Intake logs for {0}", DateTime.Now));
                Logger.Write("");

                string testSuiteName = "";

                try
                {
                    testSuiteName = teamProject.TestSuites.Find(suiteId).TestSuiteEntry.Title;
                }
                catch (Exception)
                {
                    Logger.Write(string.Format("Could not find Test Suite with id '{0}'.", suiteId));
                    continue;
                }
                
                string queryForTestPointsForSpecificTestSuite = string.Format("SELECT * FROM TestPoint WHERE SuiteId = {0}", suiteId);
                var testPoints = testPlan.QueryTestPoints(queryForTestPointsForSpecificTestSuite);
                
                Logger.Write(string.Format("Changes in suite '{0}' ({1}):", testSuiteName, suiteId));

                foreach (var testPoint in testPoints)
                {
                    var testId = testPoint.TestCaseId;
                    var result = testPoint.MostRecentResultOutcome.ToString();

                    WorkItemCollection workItems = workItemStore.Query(string.Format("Select [id], [State] From WorkItems Where [id] = '{0}' ", testId));

                    var workItem = workItems[0];

                    var currentState = workItem.State;
                    if (currentState == result)
                        continue;

                    try
                    {
                        workItem.Open();
                        workItem.State = "Design";
                        workItem.Save();

                        workItem.Open();
                        workItem.State = result;
                        workItem.Save();
                        Logger.Write(string.Format("Test Case {0} state changed: '{1}' -> '{2}'", testId, currentState, result));
                    }
                    catch (Exception ex)
                    {
                        Logger.Write("##### Error: " + ex.Message);
                    }
                }

                Logger.Write(string.Format("Suite '{0}' ({1}) complete.", testSuiteName, suiteId));
            }
        }
    }
}
