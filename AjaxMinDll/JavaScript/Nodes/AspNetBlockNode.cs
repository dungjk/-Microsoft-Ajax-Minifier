// AspNetBlockNode.cs
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
using System.Text;

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{
	public sealed class AspNetBlockNode : AstNode
	{
		private bool m_blockTerminatedByExplicitSemicolon;

		public string BlockText {get; private set;}

		public AspNetBlockNode(Context context, JSParser parser, string aspNetBlockText,
			bool blockTerminatedByExplicitSemicolon)
			: base(context, parser)
		{
			this.BlockText = aspNetBlockText;
			this.m_blockTerminatedByExplicitSemicolon = blockTerminatedByExplicitSemicolon;
		}

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

		public override bool RequiresSeparator
		{
			get
			{
				return this.m_blockTerminatedByExplicitSemicolon;
			}
		}
	}
}
