// CommonSettings.cs
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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;

namespace Microsoft.Ajax.Utilities
{
    /// <summary>
    /// Output mode setting
    /// </summary>
    public enum OutputMode
    {
        /// <summary>
        /// Output the minified code on a single line for maximum minification.
        /// LineBreakThreshold may still break the single line into multiple lines
        /// at a syntactically correct point after the given line length is reached.
        /// Not easily human-readable.
        /// </summary>
        SingleLine,

        /// <summary>
        /// Output the minified code on multiple lines to increase readability
        /// </summary>
        MultipleLines
    }

    /// <summary>
    /// Describes how to output the opening curly-brace for blocks when the OutputMode
    /// is set to MultipleLines. 
    /// </summary>
    public enum BlockStart
    {
        /// <summary>
        /// Output the opening curly-brace block-start character on its own new line. Ex:
        /// if (condition)
        /// {
        ///     ...
        /// }
        /// </summary>
        NewLine = 0,

        /// <summary>
        /// Output the opening curly-brace block-start character at the end of the previous line. Ex:
        /// if (condition) {
        ///     ...
        /// }
        /// </summary>
        SameLine,

        /// <summary>
        /// Output the opening curly-brace block-start character on the same line or a new line
        /// depending on how it was specified in the sources. 
        /// </summary>
        UseSource
    }

    /// <summary>
    /// Common settings shared between CSS and JS settings objects
    /// </summary>
    public class CommonSettings
    {
        protected CommonSettings()
        {
            // defaults
            IndentSize = 4;
            OutputMode = OutputMode.SingleLine;
            TermSemicolons = false;
            KillSwitch = 0;
            LineBreakThreshold = int.MaxValue - 1000;
            AllowEmbeddedAspNetBlocks = false;
        }

        /// <summary>
        /// Gets or sets a boolean value indicating whether embedded asp.net blocks (&lt;% %>) should be recognized and output as is. Default is false.
        /// </summary>
        public bool AllowEmbeddedAspNetBlocks
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the opening curly brace for blocks is
        /// on its own line (NewLine, default) or on the same line as the preceding code (SameLine)
        /// or taking a hint from the source code position (UseSource). Only relevant when OutputMode is 
        /// set to MultipleLines.
        /// </summary>
        public BlockStart BlocksStartOnSameLine
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets an integer value specifying the number of spaces per indent level when in MultipleLines output mode. (Default = 4)
        /// </summary>
        public int IndentSize
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets the column position at which the line will be broken at the next available opportunity.
        /// Default value is int.MaxValue - 1000.
        /// </summary>
        public int LineBreakThreshold
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets a value indicating the output mode:
        /// SingleLine (default) - output all code on a single line;
        /// MultipleLines - break the output into multiple lines to be more human-readable;
        /// SingleLine mode may still result in multiple lines if the LineBreakThreshold is set to a small enough value.
        /// </summary>
        public OutputMode OutputMode
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets a boolean value indicting whether to add a semicolon at the end of the parsed code (true) or not (false, default)
        /// </summary>
        public bool TermSemicolons
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets a long integer value containing kill switch flags for each individual mod to the parsed code tree. Allows for
        /// callers to turn off specific modifications if desired. Default is 0, meaning no kill switches are set.
        /// </summary>
        public long KillSwitch
        {
            get;
            set;
        }

        #region Indent methods

        // this is the indent level and size for the pretty-print
        private int m_indentLevel;// = 0;

        internal void Indent()
        {
            ++m_indentLevel;
        }

        internal void Unindent()
        {
            Debug.Assert(m_indentLevel > 0);
            if (m_indentLevel > 0)
            {
                --m_indentLevel;
            }
        }

        internal string TabSpaces
        {
            get
            {
                return new string(' ', m_indentLevel * IndentSize);
            }
        }

        #endregion

        #region IgnoreErrors list

        /// <summary>
        /// Gets or sets a flag for whether to ignore ALL errors found in the input code.
        /// Default is false.
        /// </summary>
        public bool IgnoreAllErrors { get; set; }

        /// <summary>
        /// Collection of errors to ignore
        /// </summary>
        public ReadOnlyCollection<string> IgnoreErrors { get; private set; }

        /// <summary>
        /// Set the collection of errors to ignore
        /// </summary>
        /// <param name="definedNames">array of error code strings</param>
        /// <returns>number of error codes successfully added to the collection</returns>
        public int SetIgnoreErrors(params string[] ignoreErrors)
        {
            int numAdded = 0;
            if (ignoreErrors == null || ignoreErrors.Length == 0)
            {
                IgnoreErrors = null;
            }
            else
            {
                var uniqueCodes = new List<string>(ignoreErrors.Length);
                for (var ndx = 0; ndx < ignoreErrors.Length; ++ndx)
                {
                    string errorCode = ignoreErrors[ndx].Trim().ToUpperInvariant();
                    if (!uniqueCodes.Contains(errorCode))
                    {
                        uniqueCodes.Add(errorCode);
                    }
                }
                IgnoreErrors = new ReadOnlyCollection<string>(uniqueCodes);
                numAdded = IgnoreErrors.Count;
            }

            return numAdded;
        }

        /// <summary>
        /// string representation of the list of debug lookups, comma-separated
        /// </summary>
        public string IgnoreErrorList
        {
            get
            {
                // createa string builder and add each of the debug lookups to it
                // one-by-one, separating them with a comma
                var sb = new StringBuilder();
                if (IgnoreErrors != null)
                {
                    foreach (var errorCode in IgnoreErrors)
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append(',');
                        }
                        sb.Append(errorCode);
                    }
                }
                return sb.ToString();
            }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    SetIgnoreErrors(value.Split(','));
                }
                else
                {
                    SetIgnoreErrors(null);
                }
            }
        }

        #endregion

        #region Preprocessor defines

        /// <summary>
        /// Collection of names to define for the preprocessor
        /// </summary>
        public ReadOnlyCollection<string> PreprocessorDefines 
        { 
            get 
            {
                var defines = new List<string>();
                if (PreprocessorValues != null)
                {
                    defines.AddRange(PreprocessorValues.Keys);
                }

                return new ReadOnlyCollection<string>(defines); 
            } 
        }

        /// <summary>
        /// dictionary of defines and their values
        /// </summary>
        public IDictionary<string, string> PreprocessorValues { get; private set; }

        /// <summary>
        /// Set the collection of defined names for the preprocessor
        /// </summary>
        /// <param name="definedNames">array of defined name strings</param>
        /// <returns>number of names successfully added to the collection</returns>
        public int SetPreprocessorDefines(params string[] definedNames)
        {
            if (definedNames == null || definedNames.Length == 0)
            {
                PreprocessorValues = null;
            }
            else
            {
                // create a list with a capacity equal to the number of items in the array
                PreprocessorValues = new Dictionary<string, string>(definedNames.Length, StringComparer.OrdinalIgnoreCase);

                // validate that each name in the array is a valid JS identifier
                foreach (var define in definedNames)
                {
                    string trimmedName;
                    var ndxEquals = define.IndexOf('=');
                    if (ndxEquals < 0)
                    {
                        trimmedName = define.Trim();
                    }
                    else
                    {
                        trimmedName = define.Substring(0, ndxEquals).Trim();
                    }

                    // must be a valid JS identifier
                    if (JSScanner.IsValidIdentifier(trimmedName))
                    {
                        PreprocessorValues.Add(trimmedName, ndxEquals < 0 ? string.Empty : define.Substring(ndxEquals + 1));
                    }
                }
            }

            return PreprocessorValues == null ? 0 : PreprocessorValues.Count;
        }

        /// <summary>
        /// Set the dictionary of preprocessor defines and values
        /// </summary>
        /// <param name="defines">dictionary to set</param>
        public int SetPreprocessorValues(IDictionary<string, string> defines)
        {
            if (defines != null && defines.Count > 0)
            {
                PreprocessorValues = new Dictionary<string, string>(defines.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var define in defines.Keys)
                {
                    if (JSScanner.IsValidIdentifier(define))
                    {
                        PreprocessorValues.Add(define, defines[define]);
                    }
                }
            }
            else
            {
                // clear it out
                PreprocessorValues = null;
            }

            return PreprocessorValues == null ? 0 : PreprocessorValues.Count;
        }

        /// <summary>
        /// string representation of the list of names defined for the preprocessor, comma-separated
        /// </summary>
        public string PreprocessorDefineList
        {
            get
            {
                // createa string builder and add each of the defined names to it
                // one-by-one, separating them with a comma
                var sb = new StringBuilder();
                if (PreprocessorValues != null)
                {
                    foreach (var definedName in PreprocessorValues.Keys)
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append(',');
                        }

                        sb.Append(definedName);
                        var defineValue = PreprocessorValues[definedName];
                        if (!string.IsNullOrEmpty(defineValue))
                        {
                            sb.Append('=');

                            // TODO: how can I escape any commas?
                            sb.Append(defineValue);
                        }
                    }
                }

                return sb.ToString();
            }
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    SetPreprocessorDefines(value.Split(','));
                }
                else
                {
                    PreprocessorValues = null;
                }
            }
        }

        #endregion

        #region Resource Strings

        /// <summary>
        /// Collection of names to define for the preprocessor
        /// </summary>
        private List<ResourceStrings> m_resourceStrings;
        public IList<ResourceStrings> ResourceStrings { get { return m_resourceStrings; } }

        public void AddResourceStrings(ResourceStrings resourceStrings)
        {
            // if we haven't createed the collection yet, do so now
            if (m_resourceStrings == null)
            {
                m_resourceStrings = new List<ResourceStrings>();
            }

            // add it
            m_resourceStrings.Add(resourceStrings);
        }

        public void AddResourceStrings(IEnumerable<ResourceStrings> collection)
        {
            // if we haven't created the collection yet, do so now
            if (m_resourceStrings == null)
            {
                m_resourceStrings = new List<ResourceStrings>();
            }

            // just add the whole collection
            m_resourceStrings.AddRange(collection);
        }

        public void ClearResourceStrings()
        {
            if (m_resourceStrings != null)
            {
                // clear it and set our pointer to null
                m_resourceStrings.Clear();
                m_resourceStrings = null;
            }
        }

        public void RemoveResourceStrings(ResourceStrings resourceStrings)
        {
            if (m_resourceStrings != null)
            {
                m_resourceStrings.Remove(resourceStrings);
            }
        }

        #endregion
    }
}
