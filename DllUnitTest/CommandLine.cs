using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DllUnitTest
{
    using Microsoft.Ajax.Utilities;

    /// <summary>
    /// Summary description for CommandLine
    /// </summary>
    [TestClass]
    public class CommandLine
    {
        public CommandLine()
        {
            //
            // TODO: Add constructor logic here
            //
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
        //
        // You can use the following additional attributes as you write your tests:
        //
        // Use ClassInitialize to run code before running the first test in the class
        // [ClassInitialize()]
        // public static void MyClassInitialize(TestContext testContext) { }
        //
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
        //
        #endregion

        [TestMethod]
        public void ToArguments()
        {
            var testData = new ArgumentsData[] {
                new ArgumentsData(){CommandLine=null, Arguments=new string[0]},
                new ArgumentsData(){CommandLine="", Arguments=new string[0]},
                new ArgumentsData(){CommandLine="              ", Arguments=new string[0]},
                new ArgumentsData(){CommandLine="-ei:utf-8 -eo:utf-8 -warn:4 /G:jQuery -p -z", Arguments=new string[] {"-ei:utf-8","-eo:utf-8","-warn:4","/G:jQuery","-p","-z"}},
                new ArgumentsData(){CommandLine="\"foo bar.js\" -out \"c:\\test folder\\foo bar min.js\" ", Arguments=new string[] {"foo bar.js", "-out", "c:\\test folder\\foo bar min.js"}},
                new ArgumentsData(){CommandLine="foo\"bar\"ack", Arguments=new string[] {"foobarack"}},
                new ArgumentsData(){CommandLine="foo \"\"\"\" bar", Arguments=new string[] {"foo", "\"", "bar"}},
                new ArgumentsData(){CommandLine="now \" is the time \" for", Arguments=new string[] {"now"," is the time ","for"}},
                new ArgumentsData(){CommandLine="now \" is \"\"the\"\" time \" for", Arguments=new string[] {"now"," is \"the\" time ","for"}},
                new ArgumentsData(){CommandLine="now \"\" \" is \"\"the\"\" time \" for", Arguments=new string[] {"now", "", " is \"the\" time ","for"}},
            };

            var ndxTest = 0;
            foreach (var test in testData)
            {
                Trace.Write(string.Format("Parsing test {0}, command line: ", ++ndxTest));
                Trace.WriteLine(test.CommandLine ?? "<null pointer>");

                var argsActual = SwitchParser.ToArguments(test.CommandLine);
                var argsExpected = test.Arguments;

                // assume succesful unless proven otherwise
                var success = true;

                Assert.IsTrue(argsActual.Length == argsExpected.Length, "Parsed arguments length {0} not equal to expected arguments length {1}", argsActual.Length, argsExpected.Length);
                Trace.WriteLine(string.Format("    {0} arguments", argsActual.Length));
                for (var ndxArg = 0; ndxArg < argsActual.Length; ++ndxArg)
                {
                    var theSame = string.CompareOrdinal(argsActual[ndxArg], argsExpected[ndxArg]) == 0;
                    Trace.WriteLine(string.Format("        {0}: {1} {3} {2}", ndxArg + 1, argsActual[ndxArg], argsExpected[ndxArg], theSame ? "==" : "!="));
                    success = theSame ? success : false;
                }

                Assert.IsTrue(success, "TEST {0} FAILED!", ndxTest);
            }
        }

        [TestMethod]
        public void ToSettings()
        {
            var testData = new ArgumentsSettings[] {
                new ArgumentsSettings(){CommandLine="-warn:4 -ei:utf-8 -enc:out utf-8 /g:jQuery,$,Msn -p", JSSettings=new CodeSettings(){OutputMode=OutputMode.MultipleLines, LocalRenaming=LocalRenaming.KeepAll, KnownGlobalNamesList="jQuery,$,Msn"}, CssSettings=new CssSettings(){OutputMode=OutputMode.MultipleLines}, WarningLevel=4, EncodingInputName="utf-8", EncodingOutputName="utf-8"},
            };

            var ndxTest = 0;
            foreach (var test in testData)
            {
                Trace.Write(string.Format("Settings test {0}, command line: ", ++ndxTest));
                Trace.WriteLine(test.CommandLine ?? "<null pointer>");

                // parse the command line
                var switchParser = new SwitchParser();
                switchParser.Parse(test.CommandLine);

                // assume succesful unless proven otherwise
                var success = true;

                // test the top-level properties
                if (switchParser.WarningLevel == test.WarningLevel)
                {
                    Trace.WriteLine(string.Format("\tParsed warning level {0} matches expectations", switchParser.WarningLevel));
                }
                else
                {
                    Trace.WriteLine(string.Format("\tFAIL: Parsed warning level is {0}, expected is {1}", switchParser.WarningLevel, test.WarningLevel));
                    success = false;
                }

                if (string.CompareOrdinal(switchParser.EncodingInputName, test.EncodingInputName) == 0)
                {
                    Trace.WriteLine(string.Format("\tParsed input encoding {0} matches expectations", switchParser.EncodingInputName));
                }
                else
                {
                    Trace.WriteLine(string.Format("\tFAIL: Parsed input encoding is {0}, expected is {1}", switchParser.EncodingInputName, test.EncodingInputName));
                    success = false;
                }

                if (string.CompareOrdinal(switchParser.EncodingOutputName, test.EncodingOutputName) == 0)
                {
                    Trace.WriteLine(string.Format("\tParsed output encoding {0} matches expectations", switchParser.EncodingOutputName));
                }
                else
                {
                    Trace.WriteLine(string.Format("\tFAIL: Parsed output encoding is {0}, expected is {1}", switchParser.EncodingOutputName, test.EncodingOutputName));
                    success = false;
                }

                // if we care about the JS settings....
                if (test.JSSettings != null)
                {
                    var jsSuccess = CheckSettings(switchParser.JSSettings, test.JSSettings);
                    if (!jsSuccess)
                    {
                        success = false;
                    }
                }

                // if we care about the CSS settings....
                if (test.CssSettings != null)
                {
                    var cssSuccess = CheckSettings(switchParser.CssSettings, test.CssSettings);
                    if (!cssSuccess)
                    {
                        success = false;
                    }
                }


                Assert.IsTrue(success, "\t****TEST {0} FAILED!", ndxTest);
            }
        }

        private bool CheckSettings(object actual, object expected)
        {
            var success = true;
            var type = actual.GetType();
            foreach (var property in type.GetProperties(BindingFlags.GetProperty | BindingFlags.Instance | BindingFlags.Public))
            {
                var parsedProperty = property.GetValue(actual, null);
                var expectedProperty = property.GetValue(expected, null);

                if (parsedProperty is ICollection && expectedProperty is ICollection)
                {
                    // ignore collections for now
                }
                else if (!object.Equals(parsedProperty, expectedProperty))
                {
                    Trace.WriteLine(string.Format("\tFAIL: Parsed {3} property {2} is {0}, expected is {1}", parsedProperty, expectedProperty, property.Name, type.Name));
                    success = false;
                }
                else
                {
                    Trace.WriteLine(string.Format("\tParsed {2} property {1} is {0}", parsedProperty, property.Name, type.Name));
                }
            }
            return success;
        }

        private class ArgumentsData
        {
            public string CommandLine { get; set; }
            public string[] Arguments { get; set; }
        }

        private class ArgumentsSettings
        {
            public string CommandLine { get; set; }
            public CodeSettings JSSettings { get; set; }
            public CssSettings CssSettings { get; set; }
            public int WarningLevel { get; set; }
            public string EncodingInputName { get; set; }
            public string EncodingOutputName { get; set; }
        }
    }
}
