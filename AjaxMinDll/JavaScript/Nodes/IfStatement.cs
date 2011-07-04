// if.cs
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

    public sealed class IfStatement : AstNode
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

        private Block m_trueBlock;
        public Block TrueBlock
        {
            get { return m_trueBlock; }
            set
            {
                if (value != m_trueBlock)
                {
                    if (m_trueBlock != null && m_trueBlock.Parent == this)
                    {
                        m_trueBlock.Parent = null;
                    }
                    m_trueBlock = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        private Block m_falseBlock;
        public Block FalseBlock
        {
            get { return m_falseBlock; }
            set
            {
                if (value != m_falseBlock)
                {
                    if (m_falseBlock != null && m_falseBlock.Parent == this)
                    {
                        m_falseBlock.Parent = null;
                    }
                    m_falseBlock = value;
                    if (value != null)
                    {
                        value.Parent = this;
                    }
                }
            }
        }

        public IfStatement(Context context, JSParser parser, AstNode condition, AstNode trueBranch, AstNode falseBranch)
            : base(context, parser)
        {
            Condition = condition;
            TrueBlock = ForceToBlock(trueBranch);
            FalseBlock = ForceToBlock(falseBranch);
        }

        public override void Accept(IVisitor visitor)
        {
            if (visitor != null)
            {
                visitor.Visit(this);
            }
        }

        public void SwapBranches()
        {
            Block temp = m_trueBlock;
            m_trueBlock = m_falseBlock;
            m_falseBlock = temp;
        }

        public override IEnumerable<AstNode> Children
        {
            get
            {
                return EnumerateNonNullNodes(Condition, TrueBlock, FalseBlock);
            }
        }

        public override bool ReplaceChild(AstNode oldNode, AstNode newNode)
        {
            if (Condition == oldNode)
            {
                Condition = newNode;
                return true;
            }
            if (TrueBlock == oldNode)
            {
                TrueBlock = ForceToBlock(newNode);
                return true;
            }
            if (FalseBlock == oldNode)
            {
                FalseBlock = ForceToBlock(newNode);
                return true;
            }
            return false;
        }

        public override bool RequiresSeparator
        {
            get
            {
                // if we have an else block, then the if statement
                // requires a separator if the else block does. 
                // otherwise only if the true case requires one.
                if (FalseBlock != null)
                {
                    return FalseBlock.RequiresSeparator;
                }
                if (TrueBlock != null)
                {
                    return TrueBlock.RequiresSeparator;
                }
                return true;
            }
        }

        internal override bool EndsWithEmptyBlock
        {
            get
            {
                if (FalseBlock != null)
                {
                    return FalseBlock.EndsWithEmptyBlock;
                }
                if (TrueBlock != null)
                {
                    return TrueBlock.EndsWithEmptyBlock;
                }
                return true;
            }
        }

        //public override string ToCode(ToCodeFormat format)
        //{
        //    StringBuilder sb = new StringBuilder();
        //    sb.Append("if(");
        //    sb.Append(Condition.ToCode());
        //    sb.Append(')');

        //    // if we're in Safari-quirks mode, we will need to wrap the if block
        //    // in curly braces if it only includes a function declaration. Safari
        //    // throws parsing errors in those situations
        //    ToCodeFormat elseFormat = ToCodeFormat.Normal;
        //    if (FalseBlock != null && FalseBlock.Count == 1)
        //    {
        //        if (Parser.Settings.MacSafariQuirks
        //            && FalseBlock[0] is FunctionObject)
        //        {
        //            elseFormat = ToCodeFormat.AlwaysBraces;
        //        }
        //        else if (FalseBlock[0] is IfStatement)
        //        {
        //            elseFormat = ToCodeFormat.ElseIf;
        //        }
        //    }

        //    // get the else block -- we need to know if there is anything in order
        //    // to fully determine if the true-branch needs curly-braces
        //    string elseBlock = (
        //        FalseBlock == null
        //        ? string.Empty
        //        : FalseBlock.ToCode(elseFormat));

        //    // we'll need to force the true block to be enclosed in curly braces if
        //    // there is an else block and the true block contains a single statement
        //    // that ends in an if that doesn't have an else block
        //    ToCodeFormat trueFormat = (FalseBlock != null
        //        && TrueBlock != null
        //        && TrueBlock.EncloseBlock(EncloseBlockType.IfWithoutElse)
        //        ? ToCodeFormat.AlwaysBraces
        //        : ToCodeFormat.Normal);

        //    if (elseBlock.Length > 0
        //      && TrueBlock != null
        //      && TrueBlock.EncloseBlock(EncloseBlockType.SingleDoWhile))
        //    {
        //        trueFormat = ToCodeFormat.AlwaysBraces;
        //    }

        //    // if we're in Safari-quirks mode, we will need to wrap the if block
        //    // in curly braces if it only includes a function declaration. Safari
        //    // throws parsing errors in those situations
        //    if (Parser.Settings.MacSafariQuirks
        //        && TrueBlock != null
        //        && TrueBlock.Count == 1
        //        && TrueBlock[0] is FunctionObject)
        //    {
        //        trueFormat = ToCodeFormat.AlwaysBraces;
        //    }

        //    // add the true block
        //    string trueBlock = (
        //        TrueBlock == null
        //        ? string.Empty
        //        : TrueBlock.ToCode(trueFormat));
        //    sb.Append(trueBlock);

        //    if (elseBlock.Length > 0)
        //    {
        //        if (trueFormat != ToCodeFormat.AlwaysBraces
        //            && !trueBlock.EndsWith(";", StringComparison.Ordinal)
        //            && (TrueBlock == null || TrueBlock.RequiresSeparator))
        //        {
        //            sb.Append(';');
        //        }

        //        // if we are in pretty-print mode, drop the else onto a new line
        //        Parser.Settings.NewLine(sb);
        //        sb.Append("else");
        //        // if the first character could be interpreted as a continuation
        //        // of the "else" statement, then we need to add a space
        //        if (JSScanner.StartsWithIdentifierPart(elseBlock))
        //        {
        //            sb.Append(' ');
        //        }

        //        sb.Append(elseBlock);
        //    }
        //    return sb.ToString();
        //}
    }
}