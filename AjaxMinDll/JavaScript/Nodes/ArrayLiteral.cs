// ArrayLiteral.cs
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

using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Microsoft.Ajax.Utilities.JavaScript;
using Microsoft.Ajax.Utilities.JavaScript.Visitors;

namespace Microsoft.Ajax.Utilities.JavaScript.Nodes
{
    public sealed class ArrayLiteral : Expression
    {
        private AstNodeList m_elements;
        public AstNodeList Elements
        {
            get { return m_elements; }
            set
            {
                if (value != m_elements)
                {
                    // break the parent reference in the old elements
                    if (m_elements != null && m_elements.Parent == this)
                    {
                        m_elements.Parent = null;
                    }

                    // set the new value
                    m_elements = value;

                    // set the parent reference
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        public ArrayLiteral(Context context, JSParser parser, AstNodeList elements)
            : base(context, parser)
        {
            Elements = elements;
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Elements);
            }
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            // if the old node isn't our element list, ignore the cal
            if (oldNode == Elements)
            {
                Elements = newNode as AstNodeList;
                return true;
            }
            return false;
        }

        public override string GetFunctionNameGuess(AstNode target)
        {
            string parentGuess = string.Empty;
            // find the index of the target item
            for (int ndx = 0; ndx < Elements.Count; ++ndx)
            {
                if (Elements[ndx] == target)
                {
                    // we'll append the index to the guess for this array
                    parentGuess = Parent == null ? string.Empty : Parent.GetFunctionNameGuess(this);
                    if (!string.IsNullOrEmpty(parentGuess))
                    {
                        parentGuess += '_' + ndx.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }

            return parentGuess;
        }
    }
}
