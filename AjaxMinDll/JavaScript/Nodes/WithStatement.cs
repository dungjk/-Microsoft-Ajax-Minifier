// with.cs
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
    public sealed class WithStatement : AstNode
    {
        private AstNode m_withObject;
        public AstNode WithObject
        {
            get { return m_withObject; }
            set
            {
                if (value != m_withObject)
                {
                    if (m_withObject != null && m_withObject.Parent == this)
                    {
                        m_withObject.Parent = null;
                    }
                    m_withObject = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        private Block m_block;
        public Block Body
        {
            get { return m_block; }
            set
            {
                if (value != m_block)
                {
                    if (m_block != null && m_block.Parent == this)
                    {
                        m_block.Parent = null;
                    }
                    m_block = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        public WithStatement(Context context, JSParser parser, AstNode obj, AstNode body)
            : base(context, parser)
        {
            WithObject = obj;
            Body = ForceToBlock(body);
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override string GetFunctionNameGuess(AstNode target)
        {
            return "with";
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(WithObject, Body);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (WithObject == oldNode)
            {
                WithObject = newNode;
                return true;
            }
            if (Body == oldNode)
            {
                Body = ForceToBlock(newNode);
                return true;
            }
            return false;
        }

        public override bool RequiresSeparator
        {
            get
            {
                // requires a separator if the body does
                return Body == null ? true : Body.RequiresSeparator;
            }
        }

        internal override bool EndsWithEmptyBlock
        {
            get
            {
                return Body == null ? true : Body.EndsWithEmptyBlock;
            }
        }
    }
}