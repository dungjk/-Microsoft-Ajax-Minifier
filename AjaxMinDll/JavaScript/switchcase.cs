// switchcase.cs
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
using System.Text;

namespace Microsoft.Ajax.Utilities
{
    public sealed class SwitchCase : AstNode
    {
        public AstNode CaseValue { get; private set; }
        public Block Statements { get; private set; }

        internal bool IsDefault
        {
            get { return (CaseValue == null); }
        }

        public SwitchCase(Context context, JSParser parser, AstNode caseValue, Block statements)
            : base(context, parser)
        {
            CaseValue = caseValue;
            if (caseValue != null)
            {
                caseValue.Parent = this;
            }

            Statements = statements;
            if (statements != null)
            {
                statements.Parent = this;
            }
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        internal override string GetFunctionGuess(AstNode target)
        {
            return CaseValue.GetFunctionGuess(target);
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(CaseValue, Statements);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (CaseValue == oldNode)
            {
                CaseValue = newNode;
                if (newNode != null) { newNode.Parent = this; }
                return true;
            }
            if (Statements == oldNode)
            {
                if (newNode == null)
                {
                    // remove it
                    Statements = null;
                    return true;
                }
                else
                {
                    // if the new node isn't a block, ignore the call
                    Block newBlock = newNode as Block;
                    if (newBlock != null)
                    {
                        Statements = newBlock;
                        newNode.Parent = this;
                        return true;
                    }
                }
            }
            return false;
        }

        internal override bool RequiresSeparator
        {
            get
            {
                // no statements doesn't require a separator.
                // otherwise only if statements require it
                if (Statements == null || Statements.Count == 0)
                {
                    return false;
                }

                return Statements[Statements.Count - 1].RequiresSeparator;
            }
        }
    }
}