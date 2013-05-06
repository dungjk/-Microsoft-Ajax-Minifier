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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace Microsoft.Ajax.Utilities
{
    public partial class MainClass
    {
        #region ProcessCssFile method

        private int ProcessCssFile(IList<InputGroup> inputGroups, SwitchParser switchParser, StringBuilder outputBuilder)
        {
            int retVal = 0;

            // blank line before
            WriteProgress();

            // we can share the same parser object
            CssParser parser = new CssParser();
            parser.Settings = switchParser.CssSettings;
            parser.JSSettings = switchParser.JSSettings;

            var ndx = 0;
            foreach(var inputGroup in inputGroups)
            {
                // process input source...
                parser.CssError += (sender, ea) =>
                    {
                        var error = ea.Error;
                        if (inputGroup.Origin == Configuration.SourceOrigin.Project || error.Severity == 0)
                        {
                            // ignore severity values greater than our severity level
                            if (error.Severity <= switchParser.WarningLevel)
                            {
                                // we found an error
                                m_errorsFound = true;

                                WriteError(error.ToString());
                            }
                        }
                    };

                // crunch the source and output to the string builder we were passed
                var crunchedStyles = parser.Parse(inputGroup.Source);
                if (!string.IsNullOrEmpty(crunchedStyles))
                {
                    Debug.WriteLine(crunchedStyles);
                    if (ndx++ > 0)
                    {
                        // separate input group outputs with an appropriate newline
                        outputBuilder.Append(switchParser.CssSettings.LineTerminator);
                    }

                    outputBuilder.Append(crunchedStyles);
                }
                else
                {
                    // resulting code is null or empty
                    Debug.WriteLine(AjaxMin.OutputEmpty);
                }
            }

            return retVal;
        }

        #endregion
    }
}
