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

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Nodes;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities
{
    public partial class MainClass
    {
        /// <summary>
        /// Undefined global variables will be added to this list
        /// </summary>
        private List<UndefinedReferenceException> m_undefined;// = null;

        #region JS-only settings

        private CodeSettings m_jsSettings = new CodeSettings();

        // whether to analyze the resulting script for common problems
        // (as opposed to just crunching it)
        private bool m_analyze;// = false;

        /// <summary>
        /// List of expected global variables we don't want to assume are undefined
        /// </summary>
        private List<string> m_globals;// = null;

        /// <summary>
        /// List of names we don't want automatically renamed
        /// </summary>
        private List<string> m_noAutoRename; // = null;

        // set of names (variables or functions) that we want to always RENAME to something else
        private Dictionary<string, string> m_renameMap;

        // whether to only preprocess (true), or to completely parse and analyze code (false)
        private bool m_preprocessOnly; // = false;

        // list of identifier names to consider "debug" lookups
        private List<string> m_debugLookups; // = null;

        #endregion

        #region file processing

        private int ProcessJSFile(string sourceFileName, string encodingName, ResourceStrings resourceStrings, StringBuilder outputBuilder, ref bool lastEndedSemicolon, ref long sourceLength)
        {
            int retVal = 0;

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

            // put the resource strings object into the parser
            parser.ResourceStrings = resourceStrings;

            // set our flags
            m_jsSettings.SetKnownGlobalNames(m_globals == null ? null : m_globals.ToArray());
            m_jsSettings.SetNoAutoRename(m_noAutoRename == null ? null : m_noAutoRename.ToArray());

            // if there are defined preprocessor names
            if (m_defines != null && m_defines.Count > 0)
            {
                // set the list of defined names to our array of names
                m_jsSettings.SetPreprocessorDefines(m_defines.ToArray());
            }

            // if there are rename entries...
            if (m_renameMap != null && m_renameMap.Count > 0)
            {
                // add each of them to the parser
                foreach (var sourceName in m_renameMap.Keys)
                {
                    m_jsSettings.AddRenamePair(sourceName, m_renameMap[sourceName]);
                }
            }

            // if the lookups collection is not null, replace any current lookups with
            // whatever the collection is (which might be empty)
            if (m_debugLookups != null)
            {
                m_jsSettings.SetDebugLookups(m_debugLookups.ToArray());
            }

            if (m_preprocessOnly)
            {
                // we only want to preprocess the code. Call that api on the parser
                using (var writer = new StringWriter(outputBuilder, CultureInfo.InvariantCulture))
                {
                    writer.Write(parser.PreprocessOnly(m_jsSettings));
                }
            }
            else
            {
                Block program = parser.Parse(m_jsSettings);
                if (program != null)
                {
                    if (m_analyze)
                    {
                        // blank line before
                        WriteProgress();

                        // output our report
                        ReportVisitor.Report(program, WriteProgress);
                    }

                    // see if any previous code ended in something other than a semicolon.
                    // if there was output and it DIDN'T end in a semicolon, we will ask the output
                    // visitor to output one before it outputs anything else.
                    var prependSemicolon = (outputBuilder.Length > 0 && outputBuilder[outputBuilder.Length - 1] != ';');

                    if (m_echoInput)
                    {
                        // send the original source (the input) to the output stream
                        outputBuilder.Append(source);
                    }
                    else
                    {
                        // send the minified results to the output stream
                        m_outputSettings.LeaveLiteralsUnchanged = !m_jsSettings.IsModificationAllowed(TreeModifications.MinifyNumericLiterals);
                        using (var writer = new StringWriter(outputBuilder, CultureInfo.InvariantCulture))
                        {
                            OutputVisitor.Output(program, writer, prependSemicolon, m_outputSettings);
                        }
                    }
                }
                else
                {
                    // no code?
                    WriteProgress(StringMgr.GetString("NoParsedCode"));
                }
            }

            // check if this string ended in a semi-colon so we'll know if
            // we need to add one between this code and the next block (if any)
            if (outputBuilder.Length > 0)
            {
                lastEndedSemicolon = (outputBuilder[outputBuilder.Length - 1] == ';');
            }

            //if (!string.IsNullOrEmpty(resultingCode))
            //{
            //    // always output the crunched code to debug stream
            //    System.Diagnostics.Debug.WriteLine(resultingCode);

            //    // if the last block of code didn't end in a semi-colon,
            //    // then we need to add one now
            //    if (!lastEndedSemicolon)
            //    {
            //        outputBuilder.Append(';');
            //    }

            //    // we'll output either the crunched code (normal) or
            //    // the raw source if we're just echoing the input
            //    string outputCode = (m_echoInput ? source : resultingCode);

            //    // send the output code to the output stream
            //    outputBuilder.Append(outputCode);
            //}
            //else
            //{
            //    // resulting code is null or empty
            //    Debug.WriteLine(StringMgr.GetString("OutputEmpty"));
            //}

            return retVal;
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
            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.Load(filePath);

                // get all the <rename> nodes in the document
                var renameNodes = xmlDoc.SelectNodes("//rename");

                // not an error if there are no variables to rename; but if there are no nodes, then
                // there's nothing to process
                if (renameNodes.Count > 0)
                {
                    // process each <rename> node
                    for (var ndx = 0; ndx < renameNodes.Count; ++ndx)
                    {
                        var renameNode = renameNodes[ndx];

                        // get the from and to attributes
                        var fromAttribute = renameNode.Attributes["from"];
                        var toAttribute = renameNode.Attributes["to"];

                        // need to have both, and their values both need to be non-null and non-empty
                        if (fromAttribute != null && !string.IsNullOrEmpty(fromAttribute.Value)
                            && toAttribute != null && !string.IsNullOrEmpty(toAttribute.Value))
                        {
                            // create the map if it doesn't yet exist
                            if (m_renameMap == null)
                            {
                                m_renameMap = new Dictionary<string, string>();
                            }

                            // if one or the other name is invalid, the pair will be ignored
                            m_renameMap.Add(fromAttribute.Value, toAttribute.Value);
                        }
                    }
                }

                // get all the <norename> nodes in the document
                var norenameNodes = xmlDoc.SelectNodes("//norename");

                // not an error if there aren't any
                if (norenameNodes.Count > 0)
                {
                    for (var ndx = 0; ndx < norenameNodes.Count; ++ndx)
                    {
                        var node = norenameNodes[ndx];
                        var idAttribute = node.Attributes["id"];
                        if (idAttribute != null && !string.IsNullOrEmpty(idAttribute.Value))
                        {
                            // if we haven't created it yet, do it now
                            if (m_noAutoRename == null)
                            {
                                m_noAutoRename = new List<string>();
                            }

                            m_noAutoRename.Add(idAttribute.Value);
                        }
                    }
                }
            }
            catch (XmlException e)
            {
                // throw an error indicating the XML error
                System.Diagnostics.Debug.WriteLine(e.ToString());
                throw new UsageException(ConsoleOutputMode.Console, "InputXmlError", e.Message);
            }
        }

        #endregion

        #region Error-handling Members

        private void OnCompilerError(object sender, JScriptExceptionEventArgs e)
        {
            ContextError error = e.Error;
            // ignore severity values greater than our severity level
            if (error.Severity <= m_warningLevel)
            {
                // we found an error
                m_errorsFound = true;

                // write the error out
                WriteError(error.ToString());
            }
        }

        private void OnUndefinedReference(object sender, UndefinedReferenceEventArgs e)
        {
            if (m_undefined == null)
            {
                m_undefined = new List<UndefinedReferenceException>();
            }
            m_undefined.Add(e.Exception);
        }

        #endregion
    }
}
