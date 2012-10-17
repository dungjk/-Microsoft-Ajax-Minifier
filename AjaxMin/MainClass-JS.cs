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
    using Configuration;

    public partial class MainClass
    {
        #region file processing

        private int PreprocessJSFile(string combinedSourceFile, SwitchParser switchParser, StringBuilder outputBuilder)
        {
            // blank line before
            WriteProgress();

            // create the a parser object for our chunk of code
            JSParser parser = new JSParser(combinedSourceFile);

            // hook the engine events
            parser.UndefinedReference += OnUndefinedReference;
            parser.CompilerError += (sender, ea) =>
                {
                    var error = ea.Error;

                    // ignore severity values greater than our severity level
                    // also ignore errors that are in our ignore list (if any)
                    if (error.Severity <= switchParser.WarningLevel)
                    {
                        // we found an error
                        m_errorsFound = true;

                        // write the error out
                        WriteError(error.ToString());
                    }
                };

            // we only want to preprocess the code. Call that api on the parser
            var resultingCode = parser.PreprocessOnly(switchParser.JSSettings);

            if (!string.IsNullOrEmpty(resultingCode))
            {
                // always output the crunched code to debug stream
                Debug.WriteLine(resultingCode);

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

        private int ProcessJSFileEcho(string combinedSourceCode, SwitchParser switchParser, StringBuilder outputBuilder)
        {
            // blank line before
            WriteProgress();

            // create the a parser object for our chunk of code
            JSParser parser = new JSParser(combinedSourceCode);

            // hook the engine events
            parser.UndefinedReference += OnUndefinedReference;
            parser.CompilerError += (sender, ea) =>
            {
                var error = ea.Error;

                // ignore severity values greater than our severity level
                // also ignore errors that are in our ignore list (if any)
                if (error.Severity <= switchParser.WarningLevel)
                {
                    // we found an error
                    m_errorsFound = true;

                    // write the error out
                    WriteError(error.ToString());
                }
            };

            Block scriptBlock = parser.Parse(switchParser.JSSettings);
            if (scriptBlock != null)
            {
                if (switchParser.AnalyzeMode)
                {
                    // blank line before
                    WriteProgress();

                    // output our report
                    CreateReport(parser.GlobalScope, switchParser);
                }
            }
            else
            {
                // no code?
                WriteProgress(AjaxMin.NoParsedCode);
            }

            // send the output code to the output stream
            outputBuilder.Append(combinedSourceCode);

            return 0;
        }

        private int ProcessJSFile(string combinedSourceCode, SwitchParser switchParser, StringBuilder outputBuilder, string outputPath)
        {
            var returnCode = 0;

            // blank line before
            WriteProgress();

            // create the a parser object for our chunk of code
            JSParser parser = new JSParser(combinedSourceCode);

            // hook the engine events
            parser.UndefinedReference += OnUndefinedReference;
            parser.CompilerError += (sender, ea) =>
            {
                var error = ea.Error;

                // ignore severity values greater than our severity level
                // also ignore errors that are in our ignore list (if any)
                if (error.Severity <= switchParser.WarningLevel)
                {
                    // we found an error
                    m_errorsFound = true;

                    // write the error out
                    WriteError(error.ToString());
                }
            };

            Block scriptBlock = parser.Parse(switchParser.JSSettings);
            if (scriptBlock != null)
            {
                if (switchParser.AnalyzeMode)
                {
                    // blank line before
                    WriteProgress();

                    // output our report
                    CreateReport(parser.GlobalScope, switchParser);
                }

                // crunch the output and write it to debug stream, but make sure
                // the settings we use to output THIS chunk are correct
                using (var writer = new StringWriter(outputBuilder, CultureInfo.InvariantCulture))
                {
                    if (switchParser.JSSettings.Format == JavaScriptFormat.JSON)
                    {
                        if (!JSONOutputVisitor.Apply(writer, scriptBlock))
                        {
                            returnCode = 1;
                        }
                    }
                    else
                    {
                        OutputVisitor.Apply(writer, scriptBlock, switchParser.JSSettings);

                        // give the symbols map a chance to write something at the bottom of the source file
                        if (switchParser.JSSettings.SymbolsMap != null)
                        {
                            switchParser.JSSettings.SymbolsMap.EndFile(
                                writer, 
                                outputPath, 
                                m_symbolsMapFile,
                                switchParser.JSSettings.OutputMode == OutputMode.SingleLine ? "\n" : "\r\n");
                        }
                    }
                }
            }
            else
            {
                // no code?
                WriteProgress(AjaxMin.NoParsedCode);
            }

            return returnCode;
        }

        #endregion

        #region CreateJSFromResourceStrings method

        private static string CreateJSFromResourceStrings(ResourceStrings resourceStrings)
        {
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
            foreach(var keyPair in resourceStrings.NameValuePairs)
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
                string propertyName = keyPair.Key;
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
                    keyPair.Value,
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
            var fileReader = new StreamReader(filePath);
            try
            {
                using (var reader = XmlReader.Create(fileReader))
                {
                    fileReader = null;

                    // let the manifest factory do all the heavy lifting of parsing the XML
                    // into config objects
                    var config = ManifestFactory.Create(reader);
                    if (config != null)
                    {
                        // add any rename pairs
                        foreach (var pair in config.RenameIdentifiers)
                        {
                            m_switchParser.JSSettings.AddRenamePair(pair.Key, pair.Value);
                        }

                        // add any no-rename identifiers
                        m_switchParser.JSSettings.SetNoAutoRenames(config.NoRenameIdentifiers);
                    }
                }
            }
            catch (XmlException e)
            {
                // throw an error indicating the XML error
                System.Diagnostics.Debug.WriteLine(e.ToString());
                throw new UsageException(ConsoleOutputMode.Console, AjaxMin.InputXmlError.FormatInvariant(e.Message));
            }
            finally
            {
                if (fileReader != null)
                {
                    fileReader.Close();
                    fileReader = null;
                }
            }
        }

        #endregion
        
        #region reporting methods

        private void CreateReport(GlobalScope globalScope, SwitchParser switchParser)
        {
            string reportText;
            using (var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                using (IScopeReport scopeReport = CreateScopeReport(switchParser))
                {
                    scopeReport.CreateReport(writer, globalScope, switchParser.JSSettings.MinifyCode);
                }
                reportText = writer.ToString();
            }

            if (!string.IsNullOrEmpty(reportText))
            {
                if (string.IsNullOrEmpty(switchParser.ReportPath))
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
                    using (var writer = new StreamWriter(switchParser.ReportPath, false, Encoding.UTF8))
                    {
                        writer.Write(reportText);
                    }
                }
            }
        }

        private static IScopeReport CreateScopeReport(SwitchParser switchParser)
        {
            // check the switch parser for a report format.
            // At this time we only have two: XML or DEFAULT. If it's XML, use
            // the XML report; all other values use the default report.
            // No error checking at this time. 
            if (string.CompareOrdinal(switchParser.ReportFormat, "XML") == 0)
            {
                return new XmlScopeReport();
            }

            return new DefaultScopeReport();
        }

        #endregion

        #region Error-handling Members

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
