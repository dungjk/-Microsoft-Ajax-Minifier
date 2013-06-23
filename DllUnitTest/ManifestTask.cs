namespace DllUnitTest
{
    using System.Diagnostics;
    using System.IO;
    using Microsoft.Ajax.Minifier.Tasks;
    using Microsoft.Build.Utilities;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

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
            var testClassName = testContext.FullyQualifiedTestClassName.Substring(
                testContext.FullyQualifiedTestClassName.LastIndexOf('.') + 1);
            s_inputFolder = Path.Combine(testContext.DeploymentDirectory, "Dll", "Input", testClassName);
            s_outputFolder = Path.Combine(testContext.DeploymentDirectory, "Dll", "Output", testClassName);
            s_expectedFolder = Path.Combine(testContext.DeploymentDirectory, "Dll", "Expected", testClassName);

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
            var task = CreateAndSetupTask();
            task.Manifests = new[] { new TaskItem() { ItemSpec = @"Dll\Input\ManifestTask\Manifest.xml" } };

            // check overall success
            var success = ExecuteAndLog(task);
            Assert.IsTrue(success, "expected the task to succeed");

            // make sure all the files we expect were created
            Assert.IsTrue(File.Exists(Path.Combine(s_outputFolder, "test1.js")), "test1.js does not exist");
            Assert.IsTrue(File.Exists(Path.Combine(s_outputFolder, "test1.xml")), "test1.xml does not exist");
            Assert.IsTrue(File.Exists(Path.Combine(s_outputFolder, "test2.js")), "test2.js does not exist");
            Assert.IsTrue(File.Exists(Path.Combine(s_outputFolder, "test1.css")), "test1.css does not exist");

            // verify output file contents
            var test1JSVerify = VerifyFileContents("test1.js");
            var test2JSVerify = VerifyFileContents("test2.js");
            var test1CssVerify = VerifyFileContents("test1.css");

            // TODO: verify map file contents

            Assert.IsTrue(test1JSVerify, "Test1.js output doesn't match");
            Assert.IsTrue(test2JSVerify, "Test2.js output doesn't match");
            Assert.IsTrue(test1CssVerify, "Test1.css output doesn't match");
        }

        [TestMethod]
        public void ManifestPPOnly()
        {
            // create the task, set it up, and execute it
            var task = CreateAndSetupTask();
            task.Manifests = new[] { new TaskItem() { ItemSpec = @"Dll\Input\ManifestTask\Manifest_pponly.xml" } };
            task.ProjectDefaultSwitches = "-pponly";

            // check overall success
            var success = ExecuteAndLog(task);
            Assert.IsTrue(success, "expected the task to succeed");

            // make sure all the files we expect were created
            Assert.IsTrue(File.Exists(Path.Combine(s_outputFolder, "test1_pponly.js")), "test1_pponly.js does not exist");
            Assert.IsTrue(File.Exists(Path.Combine(s_outputFolder, "test2_pponly.js")), "test2_pponly.js does not exist");

            // the symbol map should NOT have been created, since this is pponly
            Assert.IsFalse(File.Exists(Path.Combine(s_outputFolder, "test1_pponly.xml")), "test1_pponly.xml should not exist");
            Assert.IsFalse(File.Exists(Path.Combine(s_outputFolder, "test2_pponly.xml")), "test2_pponly.xml should not exist");

            // verify output file contents
            var test1JSVerify = VerifyFileContents("test1_pponly.js");
            var test2JSVerify = VerifyFileContents("test2_pponly.js");

            Assert.IsTrue(test1JSVerify, "Test1_pponly.js output doesn't match");
            Assert.IsTrue(test2JSVerify, "Test2_pponly.js output doesn't match");
        }

        [TestMethod]
        public void ManifestTaskFail()
        {
            // create the task, set it up, and execute it
            var task = CreateAndSetupTask();
            task.Manifests = new[] { new TaskItem() { ItemSpec = @"Dll\Input\ManifestTask\ManifestFail.xml" } };

            // check overall success
            var success = ExecuteAndLog(task);
            Assert.IsFalse(success, "expected the task to fail because of errors");
            Assert.IsTrue(((TestBuildEngine)task.BuildEngine).LogMessages.Count > 0, "expect error messages");

            // make sure all the output file did not get created
            Assert.IsFalse(File.Exists(Path.Combine(s_outputFolder, "failoutput.js")), "failoutput.js should not exist");
            Assert.IsFalse(File.Exists(Path.Combine(s_outputFolder, "failoutput.css")), "failoutput.css should not exist");
        }

        [TestMethod]
        public void ManifestExternalGroups()
        {
            // create the task, set it up, and execute it
            var task = CreateAndSetupTask();
            task.Manifests = new[] { new TaskItem { ItemSpec = @"Dll\Input\ManifestTask\ManifestExternal.xml" } };

            // check overall success
            var success = ExecuteAndLog(task);
            Assert.IsTrue(success, "expected the task to succeed");

            // make sure all the files we expect were created
            Assert.IsTrue(File.Exists(Path.Combine(s_outputFolder, "test_ext.js")), "test_ext.js does not exist");

            // verify output file contents
            var test1JSVerify = VerifyFileContents("test_ext.js");

            Assert.IsTrue(test1JSVerify, "Test_ext.js output doesn't match");
        }

        [TestMethod]
        public void ManifestExternal()
        {
            // create the task, set it up, and execute it
            var task = CreateAndSetupTask();
            task.TreatWarningsAsErrors = true;
            task.Manifests = new[] { new TaskItem { ItemSpec = @"Dll\Input\ManifestTask\Manifestjq2.xml" } };

            // check overall success
            var success = ExecuteAndLog(task);
            Assert.IsTrue(success, "expected the task to succeed");

            // make sure all the files we expect were created
            Assert.IsTrue(File.Exists(Path.Combine(s_outputFolder, "test_jq2.js")), "test_jq2.js does not exist");
        }

        private AjaxMinManifestTask CreateAndSetupTask()
        {
            var task = new AjaxMinManifestTask();
            task.InputFolder = s_inputFolder;
            task.SourceFolder = "TestData/Dll/Input/ManifestTask/";
            task.OutputFolder = s_outputFolder;
            task.Configuration = "Debug";
            task.ProjectDefaultSwitches = "-define:FOO=bar";

            task.BuildEngine = new TestBuildEngine()
            {
                MockProjectPath = Path.Combine(testContextInstance.DeploymentDirectory, "mock.csproj")
            };

            return task;
        }

        private bool ExecuteAndLog(AjaxMinManifestTask task)
        {
            var success = task.Execute();
            Trace.Write("TASK RESULT: ");
            Trace.WriteLine(success);

            Trace.WriteLine(string.Empty);
            Trace.WriteLine("BUILD MESSAGES:");
            foreach (var logMessage in ((TestBuildEngine)task.BuildEngine).LogMessages)
            {
                Trace.WriteLine(logMessage);
            }

            return success;
        }

        private bool VerifyFileContents(string fileName, string suffix = null)
        {
            Trace.WriteLine("");
            Trace.Write("VERIFY OUTPUTFILE: ");
            Trace.WriteLine(fileName);

            var outputPath = Path.Combine(s_outputFolder, fileName);
            var expectedPath = Path.Combine(s_expectedFolder, 
                string.IsNullOrWhiteSpace(suffix) 
                    ? fileName 
                    : Path.GetFileNameWithoutExtension(fileName) + '_' + suffix + Path.GetExtension(fileName));

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
