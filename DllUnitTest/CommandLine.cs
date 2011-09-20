﻿using System;
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
                new ArgumentsSettings(){CommandLine="-minify:false -rename:none", JSSettings=new CodeSettings(){MinifyCode=false, LocalRenaming=LocalRenaming.KeepAll}, CssSettings=null, WarningLevel=0},
                new ArgumentsSettings(){CommandLine="-define:foo,bar,ack,gag,42", JSSettings=new CodeSettings(){PreprocessorDefineList="FOO,BAR,ACK,GAG"}, CssSettings=new CssSettings(){PreprocessorDefineList="FOO,BAR,ACK,GAG"}},
                new ArgumentsSettings(){CommandLine="-ignore:foo,bar,ack,gag", JSSettings=new CodeSettings(){IgnoreErrorList="FOO,BAR,ACK,GAG"}, CssSettings=new CssSettings(){IgnoreErrorList="FOO,BAR,ACK,GAG"}},
                new ArgumentsSettings(){CommandLine="/aspnet:T -pretty:8 -term:Yes", JSSettings=new CodeSettings(){AllowEmbeddedAspNetBlocks=true,TermSemicolons=true,LocalRenaming=LocalRenaming.KeepAll,OutputMode=OutputMode.MultipleLines,IndentSize=8}, CssSettings=new CssSettings(){AllowEmbeddedAspNetBlocks=true,TermSemicolons=true,OutputMode=OutputMode.MultipleLines,IndentSize=8}},
                new ArgumentsSettings(){CommandLine="/aspnet:F -pretty:0 -term:N", JSSettings=new CodeSettings(){AllowEmbeddedAspNetBlocks=false,TermSemicolons=false,LocalRenaming=LocalRenaming.KeepAll,OutputMode=OutputMode.MultipleLines,IndentSize=0}, CssSettings=new CssSettings(){AllowEmbeddedAspNetBlocks=false,TermSemicolons=false,OutputMode=OutputMode.MultipleLines,IndentSize=0}},
                new ArgumentsSettings(){CommandLine="-expr:minify -colors:hex -comments:All", JSSettings=null, CssSettings=new CssSettings(){MinifyExpressions=true,ColorNames=CssColor.Hex,CommentMode=CssComment.All}},
                new ArgumentsSettings(){CommandLine="-expr:raw -colors:MAJOR /comments:HaCkS", JSSettings=null, CssSettings=new CssSettings(){MinifyExpressions=false,ColorNames=CssColor.Major,CommentMode=CssComment.Hacks}},
                new ArgumentsSettings(){CommandLine="-cc:1 -comments:None -debug:true -inline:yes -literals:keep -literals:evAL -mac:Y -minify:T -new:Keep -reorder:yes -unused:remove -rename:all", 
                    JSSettings=new CodeSettings(){IgnoreConditionalCompilation=false,PreserveImportantComments=false,StripDebugStatements=false,InlineSafeStrings=true,CombineDuplicateLiterals=false,EvalLiteralExpressions=true,MacSafariQuirks=true,MinifyCode=true,CollapseToLiteral=false,ReorderScopeDeclarations=true,RemoveUnneededCode=true,LocalRenaming=LocalRenaming.CrunchAll}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-cc:0 -comments:ImportanT -debug:false -inline:no /literals:combine -literals:NoEval -mac:N -minify:F -new:Collapse -reorder:N -unused:keep -rename:localization", 
                    JSSettings=new CodeSettings(){IgnoreConditionalCompilation=true,PreserveImportantComments=true,StripDebugStatements=true,InlineSafeStrings=false,CombineDuplicateLiterals=true,EvalLiteralExpressions=false,MacSafariQuirks=false,MinifyCode=false,CollapseToLiteral=true,ReorderScopeDeclarations=false,RemoveUnneededCode=false, LocalRenaming=LocalRenaming.KeepLocalizationVars}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="–debug:,", JSSettings=new CodeSettings(){StripDebugStatements=false,DebugLookupList=""}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-debug:N,", JSSettings=new CodeSettings(){StripDebugStatements=true,DebugLookupList=""}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-debug:N,Foo,Bar,Ack.Gag.Barf,14,Name.42.First", JSSettings=new CodeSettings(){StripDebugStatements=true,DebugLookupList="Foo,Bar,Ack.Gag.Barf"}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-global:foo,bar,ack,gag,212", JSSettings=new CodeSettings(){KnownGlobalNamesList="foo,bar,ack,gag"}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-norename:foo,bar,ack,gag,105 -rename:NoProps", JSSettings=new CodeSettings(){NoAutoRenameList="foo,bar,ack,gag", ManualRenamesProperties=false}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-rename:foo=bar,ack=gag,105=106", JSSettings=new CodeSettings(){RenamePairs="foo=bar,ack=gag", ManualRenamesProperties=true}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="–fnames:lock -evals:ignore", JSSettings=new CodeSettings(){PreserveFunctionNames=true, RemoveFunctionExpressionNames=false, EvalTreatment=EvalTreatment.Ignore}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-fnames:keep -evals:immediate", JSSettings=new CodeSettings(){PreserveFunctionNames=false, RemoveFunctionExpressionNames=false, EvalTreatment=EvalTreatment.MakeImmediateSafe}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="-fnames:onlyref -evals:safeall", JSSettings=new CodeSettings(){PreserveFunctionNames=false, RemoveFunctionExpressionNames=true, EvalTreatment=EvalTreatment.MakeAllSafe}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="–kill:-1", JSSettings=new CodeSettings(){KillSwitch=-1}, CssSettings=new CssSettings(){KillSwitch=-1,CommentMode=CssComment.None}},
                new ArgumentsSettings(){CommandLine="–kill:0x1", JSSettings=new CodeSettings(){KillSwitch=1}, CssSettings=new CssSettings(){KillSwitch=1,CommentMode=CssComment.None}},
                new ArgumentsSettings(){CommandLine="-kill:2", JSSettings=new CodeSettings(){KillSwitch=2}, CssSettings=new CssSettings(){KillSwitch=2}},
                new ArgumentsSettings(){CommandLine="-kill:0xDAB0 -cc:BOOYAH! -warn:-1", JSSettings=new CodeSettings(){KillSwitch=0xdab0}, CssSettings=new CssSettings(){KillSwitch=55984}, WarningLevel=0},
                new ArgumentsSettings(){CommandLine="-enc:in ascii -EO:big5 -warn", JSSettings=null, CssSettings=null, EncodingInputName="ascii", EncodingOutputName="big5", WarningLevel=int.MaxValue},
                new ArgumentsSettings(){CommandLine="-css /js", JSSettings=new CodeSettings(), CssSettings=new CssSettings()},
                new ArgumentsSettings(){CommandLine="-rename:All -pretty:WHAM", JSSettings=new CodeSettings(){OutputMode=OutputMode.MultipleLines,IndentSize=4}, CssSettings=new CssSettings(){OutputMode=OutputMode.MultipleLines,IndentSize=4}},
                new ArgumentsSettings(){CommandLine="-rename:All -pretty:-10", JSSettings=new CodeSettings(){OutputMode=OutputMode.MultipleLines,IndentSize=4}, CssSettings=new CssSettings(){OutputMode=OutputMode.MultipleLines,IndentSize=4}},
                new ArgumentsSettings(){CommandLine="–rename:foo=bar,foo=ack", JSSettings=new CodeSettings(){RenamePairs="foo=bar"}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="–d -h -j -k -m", JSSettings=new CodeSettings(), CssSettings=null},
                new ArgumentsSettings(){CommandLine="-l -z -hCL", JSSettings=new CodeSettings() {CollapseToLiteral=false, TermSemicolons=true, CombineDuplicateLiterals=true, LocalRenaming=LocalRenaming.KeepLocalizationVars}, CssSettings=null},
                new ArgumentsSettings(){CommandLine="/HC", JSSettings=new CodeSettings() {CombineDuplicateLiterals=true}, CssSettings=null},
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

                var parsedCollection = parsedProperty as ICollection;
                var expectedCollection = expectedProperty as ICollection;
                if (parsedCollection != null || expectedCollection != null)
                {
                    // if one is null or not a collection, then it's a fail
                    if (parsedCollection == null || expectedCollection == null)
                    {
                        Trace.WriteLine(string.Format("\tFAIL: Parsed {3} property {2} is {0}, expected is {1}", parsedCollection, expectedCollection, property.Name, type.Name));
                        success = false;
                    }
                    else if (parsedCollection.Count != expectedCollection.Count)
                    {
                        Trace.WriteLine(string.Format("\tFAIL: Parsed {3} property {2} collection has {0} elements, expected has {1}", parsedCollection.Count, expectedCollection.Count, property.Name, type.Name));
                        success = false;
                    }
                    else
                    {
                        Trace.WriteLine(string.Format("\tParsed {2} property {1} has {0} elements, as expected", parsedCollection.Count, property.Name, type.Name));
                    }
                }
                else if (!object.Equals(parsedProperty, expectedProperty))
                {
                    Trace.WriteLine(string.Format("\tFAIL: Parsed {3} property {2} is {0}, expected is {1}", parsedProperty ?? "<null>", expectedProperty ?? "<null>", property.Name, type.Name));
                    success = false;
                }
                else
                {
                    Trace.WriteLine(string.Format("\tParsed {2} property {1} is {0}", parsedProperty ?? "<null>", property.Name, type.Name));
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
