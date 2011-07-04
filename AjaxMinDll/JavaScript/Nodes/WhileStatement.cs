// while.cs
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
    public sealed class WhileStatement : AstNode
    {
        private AstNode m_condition;
        public AstNode Condition
        {
            get { return m_condition; }
            set
            {
                if (value != m_condition)
                {
                    if (m_condition != null && m_condition.Parent == this)
                    {
                        m_condition.Parent = null;
                    }
                    m_condition = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        private Block m_body;
        public Block Body
        {
            get { return m_body; }
            set
            {
                if (value != m_body)
                {
                    if (m_body != null && m_body.Parent == this)
                    {
                        m_body.Parent = null;
                    }
                    m_body = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        public WhileStatement(Context context, JSParser parser, AstNode condition, AstNode body)
            : base(context, parser)
        {
            Condition = condition;
            Body = ForceToBlock(body);
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Condition, Body);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (Condition == oldNode)
            {
                Condition = newNode;
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