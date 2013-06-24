// AjaxMinManifestCleanTask.cs
//
// Copyright 2013 Microsoft Corporation
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
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Ajax.Utilities;
using Microsoft.Ajax.Utilities.Configuration;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.Ajax.Minifier.Tasks
{
    /// <summary>
    /// Use this task to clean the outputs for a given set of Manifest files
    /// </summary>
    public class AjaxMinManifestCleanTask : AjaxMinManifestBaseTask
    {
        #region constructor

        public AjaxMinManifestCleanTask()
        {
            // we don't want to throw errors if an input file doesn't exist -- we just want to delete 
            // all the output files.
            this.ThrowInputMissingErrors = false;
        }

        #endregion

        #region base task overrides 

        /// <summary>
        /// Process an output group by deleting the output files if they exist.
        /// </summary>
        /// <param name="outputGroup">the OutputGroup being processed</param>
        /// <param name="outputFileInfo">FileInfo for the desired output file</param>
        /// <param name="symbolsFileInfo">FileInfo for the optional desired symbol file</param>
        /// <param name="defaultSettings">default settings for this output group</param>
        /// <param name="manifestModifiedTime">modified time for the manifest</param>
        protected override void ProcessOutputGroup(OutputGroup outputGroup, FileInfo outputFileInfo, FileInfo symbolsFileInfo, SwitchParser defaultSettings, DateTime manifestModifiedTime)
        {
            // we don't care about the inputs, we just want to delete the outputs and be done
            outputFileInfo.IfNotNull( fi => fi.Delete());
            symbolsFileInfo.IfNotNull(fi => fi.Delete());
        }

        protected override void GenerateJavaScript(OutputGroup outputGroup, IList<InputGroup> inputGroups, CodeSettings settings, string outputPath, Encoding outputEncoding)
        {
            // shouldn't get called because we override the ProcessOutputGroup method
            throw new NotImplementedException();
        }

        protected override void GenerateStyleSheet(OutputGroup outputGroup, IList<InputGroup> inputGroups, CssSettings cssSettings, CodeSettings codeSettings, string outputPath, Encoding outputEncoding)
        {
            // shouldn't get called because we override the ProcessOutputGroup method
            throw new NotImplementedException();
        }

        #endregion
    }
}
