// CssSettings.cs
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

namespace Microsoft.Ajax.Utilities
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Text;

    /// <summary>
    /// Settings Object for CSS Minifier
    /// </summary>
    public class CssSettings : CommonSettings
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CssSettings"/> class with default settings.
        /// </summary>
        public CssSettings()
        {
            ColorNames = CssColor.Strict;
            CommentMode = CssComment.Important;
            MinifyExpressions = true;
			AllowEmbeddedAspNetBlocks = false;
        }

        public CssSettings Clone()
        {
            // create the new settings object and copy all the properties from
            // the current settings
            var newSettings = new CssSettings()
            {
                AllowEmbeddedAspNetBlocks = this.AllowEmbeddedAspNetBlocks,
                ColorNames = this.ColorNames,
                CommentMode = this.CommentMode,
                IgnoreErrorList = this.IgnoreErrorList,
                IndentSize = this.IndentSize,
                KillSwitch = this.KillSwitch,
                MinifyExpressions = this.MinifyExpressions,
                OutputMode = this.OutputMode,
                PreprocessorDefineList = this.PreprocessorDefineList,
                TermSemicolons = this.TermSemicolons,
            };

            // add the resource strings (if any)
            if (this.ResourceStrings != null)
            {
                newSettings.AddResourceStrings(this.ResourceStrings);
            }

            return newSettings;
        }

        /// <summary>
        /// Gets or sets ColorNames setting.
        /// </summary>
        public CssColor ColorNames
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets CommentMode setting.
        /// </summary>
        public CssComment CommentMode
        {
            get; set;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to minify the javascript within expression functions
        /// </summary>
        public bool MinifyExpressions
        {
            get; set;
        }

		/// <summary>
		/// Gets or sets whether embedded asp.net blocks (&lt;% %>) should be recognized and output as is.
		/// </summary>
		public bool AllowEmbeddedAspNetBlocks
		{
			get;
			set;
		}
    }
}
