// MainClass-Css.cs
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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Microsoft.Ajax.Utilities
{
    public partial class MainClass
    {
        #region ProcessCssFile method

        private int ProcessCssFile(string combinedSourceCode, SwitchParser switchParser, StringBuilder outputBuilder)
        {
            int retVal = 0;

            // blank line before
            WriteProgress();

            try
            {
                // process input source...
                CssParser parser = new CssParser();
                parser.Settings = switchParser.CssSettings;
                parser.JSSettings = switchParser.JSSettings;
                parser.CssError += (sender, ea) =>
                    {
                        var error = ea.Error;
                        // ignore severity values greater than our severity level
                        if (error.Severity <= switchParser.WarningLevel)
                        {
                            // we found an error
                            m_errorsFound = true;

                            WriteError(error.ToString());
                        }
                    };

                // crunch the source and output to the string builder we were passed
                string crunchedStyles = parser.Parse(combinedSourceCode);
                if (!string.IsNullOrEmpty(crunchedStyles))
                {
                    Debug.WriteLine(crunchedStyles);
                    outputBuilder.Append(crunchedStyles);
                }
                else
                {
                    // resulting code is null or empty
                    Debug.WriteLine(AjaxMin.OutputEmpty);
                }
            }
            catch (IOException e)
            {
                // probably an error with the input file
                retVal = 1;
                System.Diagnostics.Debug.WriteLine(e.ToString());
                WriteError("AM-IO", e.Message);
            }

            return retVal;
        }

        #endregion
    }
}
