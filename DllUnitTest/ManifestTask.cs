using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DllUnitTest
{
    using Microsoft.Ajax.Minifier.Tasks;

    /// <summary>
    /// Summary description for ManifestTask
    /// </summary>
    [TestClass]
    public class ManifestTask
    {
        #region private fields

        private static string s_inputFolder;

        private static string s_outputFolder;

        private static string s_expectedFolder;

        #endregion

        public ManifestTask()
        {
        }

        private TestContext testContextInstance;

        /// <summary>
        ///Gets or sets the test context which provides
        ///information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        #region Additional test attributes

        // Use ClassInitialize to run code before running the first test in the class
        [ClassInitialize()]
        public static void MyClassInitialize(TestContext testContext) 
        {
            s_inputFolder = Path.Combine(testContext.DeploymentDirectory, "Dll", "Input", "Manifest");
            s_outputFolder = Path.Combine(testContext.DeploymentDirectory, "Dll", "Output", "Manifest");
            s_expectedFolder = Path.Combine(testContext.DeploymentDirectory, "Dll", "Expected", "Manifest");

            // make sure the output folder exists
            Directory.CreateDirectory(s_outputFolder);
        }

        // Use ClassCleanup to run code after all tests in a class have run
        // [ClassCleanup()]
        // public static void MyClassCleanup() { }
        //
        // Use TestInitialize to run code before running each test 
        // [TestInitialize()]
        // public void MyTestInitialize() { }
        //
        // Use TestCleanup to run code after each test has run
        // [TestCleanup()]
        // public void MyTestCleanup() { }

        #endregion

        [TestMethod]
        public void ManifestTaskTest()
        {
            // create the task, set it up, and execute it
            var task = new AjaxMinManifestTask();
            task.InputFolder = s_inputFolder;
            task.SourceFolder = "TestData/Dll/Input/Manifest/";
            task.OutputFolder = s_outputFolder;
            task.Configuration = "Debug";
            task.ProjectDefaultSwitches = "-define:FOO=bar";
            task.Manifests = new[] { new TaskItem() { ItemSpec = @"Dll\Manifest.xml" } };

            // our mockup build engine
            var buildEngine = new TestBuildEngine()
            {
                MockProjectPath = Path.Combine(testContextInstance.DeploymentDirectory, "mock.csproj")
            };
            task.BuildEngine = buildEngine;

            var success = task.Execute();
            Trace.Write("TASK RESULT: ");
            Trace.WriteLine(success);

            Trace.WriteLine(string.Empty);
            Trace.WriteLine("BUILD MESSAGES:");
            foreach(var logMessage in buildEngine.LogMessages)
            {
                Trace.WriteLine(logMessage);
            }

            // check overall success
            Assert.IsFalse(success, "expected the task to fail (source has errors)");

            // make sure all the files we expect were created
            Assert.IsTrue(File.Exists(Path.Combine(s_outputFolder, "test1.js")), "test1.js does not exist");
            Assert.IsTrue(File.Exists(Path.Combine(s_outputFolder, "test1.xml")), "test1.xml does not exist");
            Assert.IsTrue(File.Exists(Path.Combine(s_outputFolder, "test2.js")), "test2.js does not exist");
            Assert.IsTrue(File.Exists(Path.Combine(s_outputFolder, "test1.css")), "test1.css does not exist");

            // verify output file contents
            var test1JSVerify = VerifyFileContents("test1.js");
            var test2JSVerify = VerifyFileContents("test2.js");
            var test1CssVerify = VerifyFileContents("test1.css");

            // TODO: verify map file


            Assert.IsTrue(test1JSVerify, "Test1.js output doesn't match");
            Assert.IsTrue(test2JSVerify, "Test2.js output doesn't match");
            Assert.IsTrue(test1CssVerify, "Test1.css output doesn't match");
        }

        private bool VerifyFileContents(string fileName)
        {
            Trace.WriteLine("");
            Trace.Write("VERIFY OUTPUTFILE: ");
            Trace.WriteLine(fileName);

            var outputPath = Path.Combine(s_outputFolder, fileName);
            var expectedPath = Path.Combine(s_expectedFolder, fileName);

            Trace.WriteLine(string.Format("odd \"{1}\" \"{0}\"", outputPath, expectedPath));

            string expectedCode;
            using (var reader = new StreamReader(expectedPath))
            {
                expectedCode = reader.ReadToEnd();
            }

            Trace.WriteLine("EXPECTED:");
            Trace.WriteLine(expectedCode);

            string outputCode;
            using (var reader = new StreamReader(outputPath))
            {
                outputCode = reader.ReadToEnd();
            }

            Trace.WriteLine("ACTUAL:");
            Trace.WriteLine(outputCode);

            return string.CompareOrdinal(outputCode, expectedCode) == 0;
        }
    }
}
