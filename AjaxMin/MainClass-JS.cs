// MainClass-JS.cs
//
// Copyright 2010 Microsoft Corporation
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml;

namespace Microsoft.Ajax.Utilities
{
    public partial class MainClass
    {
        #region JS-only settings

        // whether to only preprocess (true), or to completely parse and analyze code (false)
        private bool m_preprocessOnly; // = false;

        #endregion

        #region file processing

        private int PreprocessJSFile(string sourceFileName, string encodingName, StringBuilder outputBuilder, bool isLastFile, ref long sourceLength)
        {
            // blank line before
            WriteProgress();

            // read our chunk of code
            var source = ReadInputFile(sourceFileName, encodingName, ref sourceLength);

            // create the a parser object for our chunk of code
            JSParser parser = new JSParser(source);

            // set up the file context for the parser
            parser.FileContext = string.IsNullOrEmpty(sourceFileName) ? "stdin" : sourceFileName;

            // hook the engine events
            parser.CompilerError += OnCompilerError;
            parser.UndefinedReference += OnUndefinedReference;

            // pull our JS settings from the switch-parser class
            CodeSettings settings = m_switchParser.JSSettings;

            // if this isn't the last file, make SURE we terminate the last statement with
            // a semicolon, since we'll be adding more code for the next file. But save the previous
            // setting so can restore it before we leave
            var termSemicolons = settings.TermSemicolons;
            if (!isLastFile)
            {
                settings.TermSemicolons = true;
            }

            // we only want to preprocess the code. Call that api on the parser
            var resultingCode = parser.PreprocessOnly(settings);

            // make sure we restore the intended temrinating-semicolon setting, 
            // since we may have changed it earlier
            settings.TermSemicolons = termSemicolons;

            if (!string.IsNullOrEmpty(resultingCode))
            {
                // always output the crunched code to debug stream
                System.Diagnostics.Debug.WriteLine(resultingCode);

                // send the output code to the output stream
                outputBuilder.Append(resultingCode);
            }
            else
            {
                // resulting code is null or empty
                Debug.WriteLine(AjaxMin.OutputEmpty);
            }

            return 0;
        }

        private int ProcessJSFileEcho(string sourceFileName, string encodingName, StringBuilder outputBuilder, ref long sourceLength)
        {
            // blank line before
            WriteProgress();

            // read our chunk of code
            var source = ReadInputFile(sourceFileName, encodingName, ref sourceLength);
            if (!string.IsNullOrEmpty(source))
            {
                // create the a parser object for our chunk of code
                JSParser parser = new JSParser(source);

                // set up the file context for the parser
                parser.FileContext = string.IsNullOrEmpty(sourceFileName) ? "stdin" : sourceFileName;

                // hook the engine events
                parser.CompilerError += OnCompilerError;
                parser.UndefinedReference += OnUndefinedReference;

                // pull our JS settings from the switch-parser class
                CodeSettings settings = m_switchParser.JSSettings;

                Block scriptBlock = parser.Parse(settings);
                if (scriptBlock != null)
                {
                    if (m_switchParser.AnalyzeMode)
                    {
                        // blank line before
                        WriteProgress();

                        // output our report
                        CreateReport(parser.GlobalScope);
                    }
                }
                else
                {
                    // no code?
                    WriteProgress(AjaxMin.NoParsedCode);
                }

                // send the output code to the output stream
                outputBuilder.Append(source);
            }
            else
            {
                // resulting code is null or empty
                Debug.WriteLine(AjaxMin.OutputEmpty);
            }

            return 0;
        }

        private int ProcessJSFile(string sourceFileName, string encodingName, OutputVisitor outputVisitor, bool isLastFile, ref long sourceLength)
        {
            // blank line before
            WriteProgress();

            // read our chunk of code
            var source = ReadInputFile(sourceFileName, encodingName, ref sourceLength);

            // create the a parser object for our chunk of code
            JSParser parser = new JSParser(source);

            // set up the file context for the parser
            parser.FileContext = string.IsNullOrEmpty(sourceFileName) ? "stdin" : sourceFileName;

            // hook the engine events
            parser.CompilerError += OnCompilerError;
            parser.UndefinedReference += OnUndefinedReference;

            // pull our JS settings from the switch-parser class
            CodeSettings settings = m_switchParser.JSSettings;

            // if this isn't the last file, make SURE we terminate the last statement with
            // a semicolon, since we'll be adding more code for the next file. But save the previous
            // setting so can restore it before we leave
            var termSemicolons = settings.TermSemicolons;
            if (!isLastFile)
            {
                settings.TermSemicolons = true;
            }

            Block scriptBlock = parser.Parse(settings);
            if (scriptBlock != null)
            {
                if (m_switchParser.AnalyzeMode)
                {
                    // blank line before
                    WriteProgress();

                    // output our report
                    CreateReport(parser.GlobalScope);
                }

                // crunch the output and write it to debug stream, but make sure
                // the settings we use to output THIS chunk are correct
                outputVisitor.Settings = settings;
                scriptBlock.Accept(outputVisitor);
            }
            else
            {
                // no code?
                WriteProgress(AjaxMin.NoParsedCode);
            }

            // make sure we restore the intended temrinating-semicolon setting, 
            // since we may have changed it earlier
            settings.TermSemicolons = termSemicolons;
            return 0;
        }

        #endregion

        #region CreateJSFromResourceStrings method

        private static string CreateJSFromResourceStrings(ResourceStrings resourceStrings)
        {
            IDictionaryEnumerator enumerator = resourceStrings.GetEnumerator();

            StringBuilder sb = new StringBuilder();
            // start the var statement using the requested name and open the initializer object literal
            sb.Append("var ");
            sb.Append(resourceStrings.Name);
            sb.Append("={");

            // we're going to need to insert commas between each pair, so we'll use a boolean
            // flag to indicate that we're on the first pair. When we output the first pair, we'll
            // set the flag to false. When the flag is false, we're about to insert another pair, so
            // we'll add the comma just before.
            bool firstItem = true;

            // loop through all items in the collection
            while (enumerator.MoveNext())
            {
                // if this isn't the first item, we need to add a comma separator
                if (!firstItem)
                {
                    sb.Append(',');
                }
                else
                {
                    // next loop is no longer the first item
                    firstItem = false;
                }

                // append the key as the name, a colon to separate the name and value,
                // and then the value
                // must quote if not valid JS identifier format, or if it is, but it's a keyword
                // (use strict mode just to be safe)
                string propertyName = enumerator.Key.ToString();
                if (!JSScanner.IsValidIdentifier(propertyName) || JSScanner.IsKeyword(propertyName, true))
                {
                    sb.Append("\"");
                    // because we are using quotes for the delimiters, replace any instances
                    // of a quote character (") with an escaped quote character (\")
                    sb.Append(propertyName.Replace("\"", "\\\""));
                    sb.Append("\"");
                }
                else
                {
                    sb.Append(propertyName);
                }
                sb.Append(':');

                // make sure the Value is properly escaped, quoted, and whatever we
                // need to do to make sure it's a proper JS string.
                // pass false for whether this string is an argument to a RegExp constructor.
                // pass false for whether to use W3Strict formatting for character escapes (use maximum browser compatibility)
                // pass true for ecma strict mode
                string stringValue = ConstantWrapper.EscapeString(
                    enumerator.Value.ToString(),
                    false,
                    false,
                    true
                    );
                sb.Append(stringValue);
            }

            // close the object literal and return the string
            sb.AppendLine("};");
            return sb.ToString();
        }

        #endregion

        #region Variable Renaming method

        private void ProcessRenamingFile(string filePath)
        {
            string xml;
            using (var reader = new StreamReader(filePath))
            {
                // read the XML file
                xml = reader.ReadToEnd();
            }

            try
            {
                // this doesn't catch any exceptions, so we need to handle them
                m_switchParser.ParseRenamingXml(xml);
            }
            catch (XmlException e)
            {
                // throw an error indicating the XML error
                System.Diagnostics.Debug.WriteLine(e.ToString());
                throw new UsageException(ConsoleOutputMode.Console, Extensions.FormatInvariant(AjaxMin.InputXmlError, e.Message));
            }
        }

        #endregion
        
        #region reporting methods

        private void CreateReport(GlobalScope globalScope)
        {
            string reportText;
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (IScopeReport scopeReport = CreateScopeReport())
                {
                    scopeReport.CreateReport(writer, globalScope, m_switchParser.JSSettings.MinifyCode);
                }
                reportText = writer.ToString();
            }

            if (!string.IsNullOrEmpty(reportText))
            {
                if (string.IsNullOrEmpty(m_switchParser.ReportPath))
                {
                    // no report path specified; send to console
                    WriteProgress(reportText);
                    WriteProgress();
                }
                else
                {
                    // report path specified -- write to the file.
                    // don't append; use UTF-8 as the output format.
                    // let any exceptions bubble up.
                    using (var writer = new StreamWriter(m_switchParser.ReportPath, false, Encoding.UTF8))
                    {
                        writer.Write(reportText);
                    }
                }
            }
        }

        private IScopeReport CreateScopeReport()
        {
            // check the switch parser for a report format.
            // At this time we only have two: XML or DEFAULT. If it's XML, use
            // the XML report; all other values use the default report.
            // No error checking at this time. 
            if (string.CompareOrdinal(m_switchParser.ReportFormat, "XML") == 0)
            {
                return new XmlScopeReport();
            }

            return new DefaultScopeReport();
        }

        #endregion

        #region Error-handling Members

        private void OnCompilerError(object sender, JScriptExceptionEventArgs e)
        {
            ContextError error = e.Error;
            // ignore severity values greater than our severity level
            // also ignore errors that are in our ignore list (if any)
            if (error.Severity <= m_switchParser.WarningLevel)
            {
                // we found an error
                m_errorsFound = true;

                // write the error out
                WriteError(error.ToString());
            }
        }

        private void OnUndefinedReference(object sender, UndefinedReferenceEventArgs e)
        {
            var parser = sender as JSParser;
            if (parser != null)
            {
                parser.GlobalScope.AddUndefinedReference(e.Exception);
            }
        }

        #endregion
    }
}
