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
using System.Text;

namespace Microsoft.Ajax.Utilities
{
    public enum OutputMode
    {
        SingleLine,
        MultipleLines
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
        }

        /// <summary>
        /// Number of spaces per indent level when in MultipleLines output mode
        /// </summary>
        public int IndentSize
        {
            get;
            set;
        }

        /// <summary>
        /// Output mode:
        /// SingleLine - output all code on a single line
        /// MultipleLines - break the output into multiple lines to be more human-readable
        /// </summary>
        public OutputMode OutputMode
        {
            get;
            set;
        }

        /// <summary>
        /// Gets or sets a flag for whether to add a semicolon at the end of the parsed code
        /// </summary>
        public bool TermSemicolons
        {
            get;
            set;
        }

        /// <summary>
        /// Kill switch flags for each individual mod to the parsed code tree. Allows for
        /// callers to turn off specific modifications if desired.
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
            --m_indentLevel;
        }

        // put indent level and size together for a new-line
        internal bool NewLine(StringBuilder sb)
        {
            bool addNewLine = (OutputMode == OutputMode.MultipleLines);
            if (addNewLine)
            {
                sb.AppendLine();
                if (m_indentLevel > 0 && IndentSize > 0)
                {
                    sb.Append(new string(' ', m_indentLevel * IndentSize));
                }
            }

            return addNewLine;
        }

        #endregion

        #region IgnoreErrors list

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
        public ReadOnlyCollection<string> PreprocessorDefines { get; private set; }

        /// <summary>
        /// Set the collection of defined names for the preprocessor
        /// </summary>
        /// <param name="definedNames">array of defined name strings</param>
        /// <returns>number of names successfully added to the collection</returns>
        public int SetPreprocessorDefines(params string[] definedNames)
        {
            int numAdded = 0;
            if (definedNames == null || definedNames.Length == 0)
            {
                PreprocessorDefines = null;
            }
            else
            {
                // create a list with a capacity equal to the number of items in the array
                var checkedNames = new List<string>(definedNames.Length);

                // validate that each name in the array is a valid JS identifier
                foreach (var name in definedNames)
                {
                    // must be a valid JS identifier
                    string trimmedName = name.Trim();
                    if (JSScanner.IsValidIdentifier(trimmedName))
                    {
                        checkedNames.Add(trimmedName);
                    }
                }
                PreprocessorDefines = new ReadOnlyCollection<string>(checkedNames);
                numAdded = checkedNames.Count;
            }

            return numAdded;
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
                if (PreprocessorDefines != null)
                {
                    foreach (var definedName in PreprocessorDefines)
                    {
                        if (sb.Length > 0)
                        {
                            sb.Append(',');
                        }
                        sb.Append(definedName);
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
                    SetPreprocessorDefines(null);
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
            // if we haven't createed the collection yet, do so now
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
